param(
    [string]$PackageRoot = "packaging\Clipton.Package\bin\x64\Release\AppPackages",
    [int64]$MinPackageBytes = 1024,
    [string[]]$ExpectedLanguages = @("en-US", "ja-JP", "de-DE", "es-ES", "fr-FR", "ko-KR", "zh-Hans")
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

function Get-PackageManifestLanguagesFromStream {
    param(
        [System.IO.Stream]$Stream,
        [string]$PackageName
    )

    $results = New-Object System.Collections.Generic.List[object]
    $archive = New-Object System.IO.Compression.ZipArchive($Stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        $manifestEntry = $archive.Entries | Where-Object { $_.FullName -eq "AppxManifest.xml" } | Select-Object -First 1
        if ($null -ne $manifestEntry) {
            $manifestStream = $manifestEntry.Open()
            try {
                $reader = New-Object System.IO.StreamReader($manifestStream)
                try {
                    [xml]$manifest = $reader.ReadToEnd()
                } finally {
                    $reader.Dispose()
                }
            } finally {
                $manifestStream.Dispose()
            }

            $languages = @($manifest.SelectNodes("/*[local-name()='Package']/*[local-name()='Resources']/*[local-name()='Resource']") |
                ForEach-Object { $_.GetAttribute("Language") } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $results.Add([pscustomobject]@{
                Package = $PackageName
                Languages = $languages
            })
        }

        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -notmatch '\.(msix|appx|msixbundle|appxbundle)$') {
                continue
            }

            $memory = New-Object System.IO.MemoryStream
            try {
                $nestedStream = $entry.Open()
                try {
                    $nestedStream.CopyTo($memory)
                } finally {
                    $nestedStream.Dispose()
                }

                $memory.Position = 0
                $nestedName = "$PackageName::$($entry.FullName)"
                foreach ($nestedResult in Get-PackageManifestLanguagesFromStream -Stream $memory -PackageName $nestedName) {
                    $results.Add($nestedResult)
                }
            } finally {
                $memory.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }

    return $results.ToArray()
}

function Get-PackageManifestLanguages {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return @(Get-PackageManifestLanguagesFromStream -Stream $stream -PackageName ([System.IO.Path]::GetFileName($Path)))
    } finally {
        $stream.Dispose()
    }
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

        $isDependencyPackage = $package.FullName -match '[\\/]Dependencies[\\/]'
        if (-not $isDependencyPackage) {
            $manifestLanguages = @(Get-PackageManifestLanguages $package.FullName |
                ForEach-Object { $_.Languages } |
                Sort-Object -Unique)
            $missingLanguages = @($ExpectedLanguages | Where-Object { $manifestLanguages -notcontains $_ })
            if ($missingLanguages.Count -gt 0) {
                $failures.Add("$($package.Name) is missing declared Store language resources: $($missingLanguages -join ', '). Found: $($manifestLanguages -join ', ').")
            }
        }
    } catch {
        $failures.Add("$($package.Name) could not be opened as a package archive: $($_.Exception.Message)")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Store package verification failed with $($failures.Count) issue(s)."
}

Write-Host "Store package verification passed for $($packages.Count) artifact(s)."
