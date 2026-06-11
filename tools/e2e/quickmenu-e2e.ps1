# Quick menu keyboard-navigation E2E for the default display mode.
# Launches an isolated Clipton instance, seeds clipboard history, opens the
# quick menu with Ctrl+Shift+V, then drives folder navigation with arrow keys
# and verifies menu state through UI Automation.
param(
    [string]$ExePath = (Join-Path $PSScriptRoot "..\..\src\Clipton.WinUI\bin\Debug\net8.0-windows10.0.19041.0\Clipton.WinUI.exe"),
    [string]$DataDir = (Join-Path $env:TEMP "clipton-e2e-data")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

Add-Type -Namespace E2E -Name Native -MemberDefinition @'
[DllImport("user32.dll")]
public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
'@

$script:failures = 0

function Send-Key {
    param([byte[]]$Modifiers = @(), [byte]$Key)
    foreach ($m in $Modifiers) { [E2E.Native]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
    [E2E.Native]::keybd_event($Key, 0, 0, [UIntPtr]::Zero)
    [E2E.Native]::keybd_event($Key, 0, 2, [UIntPtr]::Zero)
    foreach ($m in $Modifiers) { [E2E.Native]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 120
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

function Assert {
    param([bool]$Condition, [string]$Name, [string]$Detail = "")
    if ($Condition) {
        Write-Output "PASS: $Name"
    } else {
        Write-Output "FAIL: $Name $Detail"
        $script:failures++
    }
}

function Wait-Until {
    param([scriptblock]$Predicate, [int]$TimeoutMs = 3000, [int]$IntervalMs = 150)
    $deadline = [Environment]::TickCount + $TimeoutMs
    while ([Environment]::TickCount -lt $deadline) {
        if (& $Predicate) { return $true }
        Start-Sleep -Milliseconds $IntervalMs
    }
    return & $Predicate
}

# --- setup ---------------------------------------------------------------
$ExePath = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $ExePath)) { throw "Clipton exe not found: $ExePath" }

Get-Process Clipton.WinUI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
if (Test-Path $DataDir) { Remove-Item -Recurse -Force $DataDir }
New-Item -ItemType Directory -Force $DataDir | Out-Null

$originalClipboard = $null
try { $originalClipboard = Get-Clipboard -Raw -ErrorAction Stop } catch {}

$app = Start-Process -FilePath $ExePath -ArgumentList "--data-dir", "`"$DataDir`"" -PassThru
Write-Output "INFO: launched pid=$($app.Id)"
Start-Sleep -Seconds 4

# Seed 10 history entries so the quick menu gets a lazy "6-10" range folder.
1..10 | ForEach-Object {
    Set-Clipboard -Value "E2E item $_"
    Start-Sleep -Milliseconds 350
}
Start-Sleep -Milliseconds 800

# --- scenario 1: open menu, initial focus --------------------------------
Send-Key -Modifiers 0x11, 0x10 -Key 0x56   # Ctrl+Shift+V
$opened = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -ge 5 } 6000
Assert $opened "quick menu opens via hotkey"
Start-Sleep -Milliseconds 600   # delayed initial focus (180ms) + margin

$rootNames = (Get-MenuItems -AppPid $app.Id) | ForEach-Object { $_.Current.Name }
Write-Output ("INFO: root items = " + (($rootNames | Select-Object -First 12) -join " | "))
$folderName = $rootNames | Where-Object { $_ -match '^\d+\s*-\s*\d+' } | Select-Object -First 1
Assert ($null -ne $folderName) "range folder exists in root menu" "(names: $($rootNames -join ', '))"
Assert ((Get-FocusedName) -like "E2E item*") "first history item has initial focus" "(focused: '$(Get-FocusedName)')"

# --- scenario 2: arrow down to the folder ---------------------------------
$reached = $false
for ($i = 0; $i -lt 20 -and -not $reached; $i++) {
    if ((Get-FocusedName) -eq $folderName) { $reached = $true; break }
    Send-Key -Key 0x28   # Down
}
Assert $reached "Down navigates to folder '$folderName'" "(focused: '$(Get-FocusedName)')"

# --- scenario 3: Right opens lazy folder and focuses first child ----------
Send-Key -Key 0x27   # Right
$childFocused = Wait-Until { (Get-FocusedName) -like "E2E item*" } 3000
Assert $childFocused "Right opens folder; first child focused after lazy load" "(focused: '$(Get-FocusedName)')"
$itemCountInSubmenu = (Get-MenuItems -AppPid $app.Id).Count
Assert ($itemCountInSubmenu -gt $rootNames.Count) "submenu items visible" "(count: $itemCountInSubmenu)"

Send-Key -Key 0x28   # Down inside submenu
Assert ((Get-FocusedName) -like "E2E item*") "Down moves inside submenu" "(focused: '$(Get-FocusedName)')"

# --- scenario 4: Left closes submenu, focus back on folder, menu intact ---
Send-Key -Key 0x25   # Left
$backOnFolder = Wait-Until { (Get-FocusedName) -eq $folderName } 2000
Assert $backOnFolder "Left returns focus to folder" "(focused: '$(Get-FocusedName)')"
$menuStillOpen = (Get-MenuItems -AppPid $app.Id).Count -ge 5
Assert $menuStillOpen "root menu still open after Left"
$submenuClosed = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -le ($rootNames.Count + 1) } 2000
Assert $submenuClosed "submenu closed after Left" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

# --- scenario 5: reopen folder, rapid Left/Left does not kill the menu ----
Send-Key -Key 0x27   # Right
$reopened = Wait-Until { (Get-FocusedName) -like "E2E item*" } 3000
Assert $reopened "Right reopens folder (materialized path)" "(focused: '$(Get-FocusedName)')"
Send-Key -Key 0x25   # Left
Send-Key -Key 0x25   # Left again immediately (regression: whole-menu dismiss)
Start-Sleep -Milliseconds 700
$survivedDoubleLeft = (Get-MenuItems -AppPid $app.Id).Count -ge 5
Assert $survivedDoubleLeft "menu survives rapid double Left" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

# --- scenario 6: Esc dismisses everything ---------------------------------
Send-Key -Key 0x1B   # Esc
$dismissed = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -eq 0 } 3000
Assert $dismissed "Esc dismisses the whole menu" "(count: $((Get-MenuItems -AppPid $app.Id).Count))"

# --- scenario 7: reopen via hotkey (Reopen path) ---------------------------
Send-Key -Modifiers 0x11, 0x10 -Key 0x56
$reopenedMenu = Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -ge 5 } 6000
Assert $reopenedMenu "menu reopens via hotkey"
Start-Sleep -Milliseconds 600
Assert ((Get-FocusedName) -like "E2E item*") "initial focus set on reopen" "(focused: '$(Get-FocusedName)')"
Send-Key -Key 0x1B
Wait-Until { (Get-MenuItems -AppPid $app.Id).Count -eq 0 } 3000 | Out-Null

# --- teardown --------------------------------------------------------------
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
if ($null -ne $originalClipboard) {
    try { Set-Clipboard -Value $originalClipboard } catch {}
}

Write-Output ""
if ($script:failures -eq 0) {
    Write-Output "E2E RESULT: ALL PASS"
    exit 0
} else {
    Write-Output "E2E RESULT: $script:failures FAILURE(S)"
    exit 1
}
