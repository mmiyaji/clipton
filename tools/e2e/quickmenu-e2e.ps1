# Quick menu keyboard-navigation E2E for the default display mode.
# Launches an isolated Clipton instance, seeds clipboard history, opens the
# quick menu with the configured hotkey, then verifies menu state through UI
# Automation. Every wait is bounded so GitHub Actions cannot hang indefinitely.
param(
    [string]$ExePath = (Join-Path $PSScriptRoot "..\..\src\Clipton.WinUI\bin\Debug\net8.0-windows10.0.19041.0\Clipton.WinUI.exe"),
    [string]$DataDir = (Join-Path $env:TEMP ("clipton-e2e-data-" + [Guid]::NewGuid().ToString("N"))),
    [string]$Hotkey = "Ctrl+Alt+V",
    [int]$StartupTimeoutSeconds = 12,
    [int]$ScenarioTimeoutSeconds = 90,
    [switch]$KillExisting
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

Add-Type -Namespace E2E -Name Native -MemberDefinition @'
[DllImport("user32.dll")]
public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
'@

$script:failures = 0
$script:scenarioDeadline = [DateTimeOffset]::UtcNow.AddSeconds($ScenarioTimeoutSeconds)

function Assert {
    param([bool]$Condition, [string]$Name, [string]$Detail = "")
    if ($Condition) {
        Write-Output "PASS: $Name"
    } else {
        Write-Output "FAIL: $Name $Detail"
        $script:failures++
    }
}

function Assert-ScenarioBudget {
    if ([DateTimeOffset]::UtcNow -ge $script:scenarioDeadline) {
        throw "E2E scenario exceeded ${ScenarioTimeoutSeconds}s."
    }
}

function Get-KeyCode {
    param([string]$Key)
    $normalized = $Key.Trim().ToUpperInvariant()
    if ($normalized.Length -eq 1) {
        $c = [int][char]$normalized[0]
        if (($c -ge [int][char]'A' -and $c -le [int][char]'Z') -or ($c -ge [int][char]'0' -and $c -le [int][char]'9')) {
            return [byte]$c
        }
    }

    if ($normalized -eq "SPACE") { return [byte]0x20 }
    if ($normalized -match '^F([1-9]|1[0-9]|2[0-4])$') {
        return [byte](0x70 + [int]$Matches[1] - 1)
    }

    throw "Unsupported hotkey key: $Key"
}

function Convert-Hotkey {
    param([string]$Value)
    $modifiers = New-Object System.Collections.Generic.List[byte]
    $key = $null
    foreach ($part in ($Value -split '\+' | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })) {
        switch ($part.ToUpperInvariant()) {
            "CTRL" { $modifiers.Add(0x11); continue }
            "CONTROL" { $modifiers.Add(0x11); continue }
            "SHIFT" { $modifiers.Add(0x10); continue }
            "ALT" { $modifiers.Add(0x12); continue }
            default { $key = $part; continue }
        }
    }

    if ($null -eq $key) {
        throw "Hotkey does not contain a key: $Value"
    }

    return [pscustomobject]@{
        Modifiers = [byte[]]$modifiers.ToArray()
        Key = Get-KeyCode $key
    }
}

function Send-Key {
    param([byte[]]$Modifiers = @(), [byte]$Key)
    Assert-ScenarioBudget
    foreach ($m in $Modifiers) { [E2E.Native]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
    [E2E.Native]::keybd_event($Key, 0, 0, [UIntPtr]::Zero)
    [E2E.Native]::keybd_event($Key, 0, 2, [UIntPtr]::Zero)
    $releasedModifiers = [byte[]]$Modifiers.Clone()
    [Array]::Reverse($releasedModifiers)
    foreach ($m in $releasedModifiers) { [E2E.Native]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 120
}

function Send-Hotkey {
    param([string]$Value)
    $gesture = Convert-Hotkey $Value
    Send-Key -Modifiers $gesture.Modifiers -Key $gesture.Key
}

function Get-FocusedName {
    try {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($null -eq $focused) { return "" }
        return $focused.Current.Name
    } catch { return "" }
}

function Get-MenuItems {
    param([int]$AppPid)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppPid)
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCondition)
    $menuItemCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    $items = @()
    foreach ($window in $windows) {
        $found = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuItemCondition)
        foreach ($item in $found) { $items += $item }
    }
    return $items
}

function Wait-Until {
    param([scriptblock]$Predicate, [int]$TimeoutMs = 3000, [int]$IntervalMs = 150)
    $deadline = [DateTimeOffset]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Assert-ScenarioBudget
        if (& $Predicate) { return $true }
        Start-Sleep -Milliseconds $IntervalMs
    }
    return & $Predicate
}

function Set-ClipboardWithRetry {
    param([string]$Value, [int]$Attempts = 8)
    for ($i = 0; $i -lt $Attempts; $i++) {
        try {
            Set-Clipboard -Value $Value
            return
        } catch {
            if ($i -eq $Attempts - 1) {
                throw
            }

            Start-Sleep -Milliseconds 150
        }
    }
}

function Write-E2ESettings {
    param([string]$Root, [string]$ConfiguredHotkey)
    New-Item -ItemType Directory -Force $Root | Out-Null
    $settings = [ordered]@{
        Hotkey = $ConfiguredHotkey
        InitialLaunchCompleted = $true
        HideSettingsWindowOnStartup = $true
        PersistEncryptedHistory = $true
        HistoryPersistenceConfigured = $true
        DiagnosticLoggingEnabled = $true
        ClipboardCaptureDelayMilliseconds = 50
        MaxHistoryItems = 200
        QuickMenuDisplayMode = "default"
        QuickMenuTopLevelHistoryItems = 5
        QuickMenuImagePreviewSize = "small"
        QuickMenuShowShortcutHints = $true
        Locale = "en"
        Theme = "light"
    }
    $settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $Root "settings.json") -Encoding UTF8
}

function Stop-CliptonProcess {
    param([System.Diagnostics.Process]$Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
            $Process.WaitForExit(3000) | Out-Null
        }
    } catch {}
}

$app = $null
$originalClipboard = $null
$exitCode = 1
try {
    $ExePath = [IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path $ExePath)) { throw "Clipton exe not found: $ExePath" }

    if ($KillExisting) {
        Get-Process Clipton.WinUI -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
    }

    if (Test-Path $DataDir) { Remove-Item -Recurse -Force $DataDir }
    Write-E2ESettings -Root $DataDir -ConfiguredHotkey $Hotkey

    try { $originalClipboard = Get-Clipboard -Raw -ErrorAction Stop } catch {}

    $app = Start-Process -FilePath $ExePath -ArgumentList "--data-dir", "`"$DataDir`"" -PassThru
    Write-Output "INFO: launched pid=$($app.Id)"
    $started = Wait-Until { -not $app.HasExited } ($StartupTimeoutSeconds * 1000) 200
    Assert $started "process remains alive after launch"

    # Seed 10 history entries so the quick menu gets a lazy "6-10" range folder.
    1..10 | ForEach-Object {
        Set-ClipboardWithRetry -Value "E2E item $_"
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 900

    # --- scenario 1: open menu, initial focus -----------------------------
    Send-Hotkey $Hotkey
    $opened = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -ge 5 } 6000
    Assert $opened "quick menu opens via hotkey"
    Start-Sleep -Milliseconds 600

    $rootNames = (Get-MenuItems -AppPid $app.Id) | ForEach-Object { $_.Current.Name }
    Write-Output ("INFO: root items = " + (($rootNames | Select-Object -First 12) -join " | "))
    $folderName = $rootNames | Where-Object { $_ -match '^\d+\s*-\s*\d+' } | Select-Object -First 1
    Assert ($null -ne $folderName) "range folder exists in root menu" "(names: $($rootNames -join ', '))"
    Assert ((Get-FocusedName) -like "E2E item*") "first history item has initial focus" "(focused: '$(Get-FocusedName)')"

    # --- scenario 2: arrow down to the folder -----------------------------
    $reached = $false
    for ($i = 0; $i -lt 20 -and -not $reached; $i++) {
        if ((Get-FocusedName) -eq $folderName) { $reached = $true; break }
        Send-Key -Key 0x28
    }
    Assert $reached "Down navigates to folder '$folderName'" "(focused: '$(Get-FocusedName)')"

    # --- scenario 3: Right opens lazy folder and focuses first child -------
    Send-Key -Key 0x27
    $submenuVisible = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -gt $rootNames.Count } 3000
    Assert $submenuVisible "Right opens folder and shows lazy-loaded submenu" "(focused: '$(Get-FocusedName)')"
    $itemCountInSubmenu = (Get-MenuItems -AppPid $app.Id).Count
    Assert ($itemCountInSubmenu -gt $rootNames.Count) "submenu items visible" "(count: $itemCountInSubmenu)"

    Send-Key -Key 0x28
    Assert ((Get-MenuItems -AppPid $app.Id).Count -ge $itemCountInSubmenu) "Down keeps submenu navigable" "(focused: '$(Get-FocusedName)')"

    # --- scenario 4: Left closes submenu, focus back on folder ------------
    Send-Key -Key 0x25
    $backOnFolder = Wait-Until { (Get-FocusedName) -eq $folderName } 2000
    Assert $backOnFolder "Left returns focus to folder" "(focused: '$(Get-FocusedName)')"
    $menuStillOpen = (Get-MenuItems -AppPid $app.Id).Count -ge 5
    Assert $menuStillOpen "root menu still open after Left"
    $submenuClosed = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -le ($rootNames.Count + 1) } 2000
    Assert $submenuClosed "submenu closed after Left" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

    # --- scenario 5: rapid Left/Left does not dismiss the root menu --------
    Send-Key -Key 0x27
    $reopened = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -gt $rootNames.Count } 3000
    Assert $reopened "Right reopens folder (materialized path)" "(focused: '$(Get-FocusedName)')"
    Send-Key -Key 0x25
    Send-Key -Key 0x25
    Start-Sleep -Milliseconds 700
    $survivedDoubleLeft = (Get-MenuItems -AppPid $app.Id).Count -ge 5
    Assert $survivedDoubleLeft "menu survives rapid double Left" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

    # --- scenario 6: Esc dismisses everything -----------------------------
    Send-Key -Key 0x1B
    $dismissed = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -eq 0 } 3000
    Assert $dismissed "Esc dismisses the whole menu" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

    # --- scenario 7: reopen via hotkey (Reopen path) ----------------------
    Send-Hotkey $Hotkey
    $reopenedMenu = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -ge 5 } 6000
    Assert $reopenedMenu "menu reopens via hotkey"
    Start-Sleep -Milliseconds 600
    Assert ((Get-FocusedName) -like "E2E item*") "initial focus set on reopen" "(focused: '$(Get-FocusedName)')"
    Send-Key -Key 0x1B
    Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -eq 0 } 3000 | Out-Null

    Write-Output ""
    if ($script:failures -eq 0) {
        Write-Output "E2E RESULT: ALL PASS"
        $exitCode = 0
    } else {
        Write-Output "E2E RESULT: $script:failures FAILURE(S)"
        $exitCode = 1
    }
} catch {
    Write-Output "E2E ERROR: $($_.Exception.Message)"
    $exitCode = 1
} finally {
    Stop-CliptonProcess -Process $app
    if ($null -ne $originalClipboard) {
        try { Set-Clipboard -Value $originalClipboard } catch {}
    }
}

exit $exitCode
