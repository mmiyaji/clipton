param(
    [string]$PackageRoot = "packaging\Clipton.Package\bin\x64\Release\AppPackages",
    [int64]$MinPackageBytes = 1024
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-ZipEntries {
    param([string]$Path)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    } finally {
        $archive.Dispose()
    }
}

function Test-NestedPackageManifest {
    param([string]$Path)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -notmatch '\.(msix|appx|msixbundle|appxbundle)$') {
                continue
            }

            $memory = New-Object System.IO.MemoryStream
            try {
                $stream = $entry.Open()
                try {
                    $stream.CopyTo($memory)
                } finally {
                    $stream.Dispose()
                }

                $memory.Position = 0
                $nested = New-Object System.IO.Compression.ZipArchive($memory, [System.IO.Compression.ZipArchiveMode]::Read, $true)
                try {
                    $names = @($nested.Entries | ForEach-Object { $_.FullName })
                    if ($names -contains "AppxManifest.xml" -or $names -contains "AppxMetadata/AppxBundleManifest.xml") {
                        return $true
                    }
                } finally {
                    $nested.Dispose()
                }
            } finally {
                $memory.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }

    return $false
}

$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path $root)) {
    throw "Package root was not found: $root"
}

$packages = Get-ChildItem -Path $root -Recurse -File -Include "*.msixupload","*.appxupload","*.msixbundle","*.appxbundle","*.msix","*.appx" |
    Sort-Object FullName

if ($packages.Count -eq 0) {
    throw "No Store package artifacts were found under $root."
}

$uploadPackages = @($packages | Where-Object { $_.Extension -in ".msixupload", ".appxupload" })
if ($uploadPackages.Count -eq 0) {
    throw "No Store upload package (*.msixupload or *.appxupload) was produced."
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($package in $packages) {
    Write-Host "Checking package: $($package.FullName)"
    if ($package.Length -lt $MinPackageBytes) {
        $failures.Add("$($package.Name) is unexpectedly small: $($package.Length) bytes.")
        continue
    }

    try {
        $entries = Get-ZipEntries $package.FullName
        $hasDirectManifest = $entries -contains "AppxManifest.xml" -or $entries -contains "AppxMetadata/AppxBundleManifest.xml"
        $hasNestedManifest = $false
        if (-not $hasDirectManifest -and ($package.Extension -in ".msixupload", ".appxupload")) {
            $hasNestedManifest = Test-NestedPackageManifest $package.FullName
        }

        if (-not $hasDirectManifest -and -not $hasNestedManifest) {
            $failures.Add("$($package.Name) does not contain an Appx manifest directly or in a nested package.")
        }
    } catch {
        $failures.Add("$($package.Name) could not be opened as a package archive: $($_.Exception.Message)")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Store package verification failed with $($failures.Count) issue(s)."
}

Write-Host "Store package verification passed for $($packages.Count) artifact(s)."
