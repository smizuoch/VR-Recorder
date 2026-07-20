[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [Parameter(Mandatory = $true)]
    [string]$PackagingIdentity,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.File]::Exists($fullPath)) {
        throw "$Name must be an existing file: $fullPath"
    }

    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must not be a reparse point: $fullPath"
    }

    return $fullPath
}

function Get-MakeAppxPath {
    $kitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidates = @(Get-ChildItem -LiteralPath $kitsBin `
        -Directory -ErrorAction Stop | ForEach-Object {
            $version = [System.Version]'0.0'
            $path = Join-Path $_.FullName 'x64\makeappx.exe'
            if ([System.Version]::TryParse($_.Name, [ref]$version) -and
                [System.IO.File]::Exists($path)) {
                [pscustomobject]@{
                    Path = $path
                    Version = $version
                }
            }
        } | Sort-Object Version -Descending)
    if ($candidates.Count -eq 0) {
        throw 'MakeAppx was not found in the installed Windows SDK.'
    }

    return $candidates[0].Path
}

function Get-DefenderCommandPath {
    $platformRoot = Join-Path `
        $env:ProgramData `
        'Microsoft\Windows Defender\Platform'
    $candidates = if ([System.IO.Directory]::Exists($platformRoot)) {
        @(Get-ChildItem -LiteralPath $platformRoot `
            -Directory -ErrorAction Stop | ForEach-Object {
                $version = [System.Version]'0.0'
                $path = Join-Path $_.FullName 'MpCmdRun.exe'
                if ([System.Version]::TryParse(
                        ($_.Name -replace '-.*$', ''),
                        [ref]$version) -and
                    [System.IO.File]::Exists($path)) {
                    [pscustomobject]@{
                        Path = $path
                        Version = $version
                    }
                }
            } | Sort-Object Version -Descending)
    }
    else {
        @()
    }
    if ($candidates.Count -ne 0) {
        return $candidates[0]
    }

    $fallback = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
    if (-not [System.IO.File]::Exists($fallback)) {
        throw 'Microsoft Defender MpCmdRun.exe was not found.'
    }
    return [pscustomobject]@{
        Path = $fallback
        Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
            $fallback).FileVersion
    }
}

function Assert-LegalManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApplicationRoot,

        [Parameter(Mandatory = $true)]
        [psobject]$Identity
    )

    $manifest = Resolve-ExistingFile `
        -Path (Join-Path $ApplicationRoot 'LEGAL-MANIFEST.sha256') `
        -Name 'Packaged Legal manifest'
    $manifestSha256 = (Get-FileHash `
        -LiteralPath $manifest `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($manifestSha256 -cne
        $Identity.validatedPayload.legalManifestSha256) {
        throw 'The packaged Legal manifest does not match payload identity.'
    }

    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $sbomSha256 = $null
    foreach ($line in [System.IO.File]::ReadAllLines($manifest)) {
        if ($line -cnotmatch '^([0-9a-f]{64})  ([A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*)$') {
            throw 'The packaged Legal manifest contains an invalid line.'
        }
        $expectedSha256 = $Matches[1]
        $relativePath = $Matches[2]
        if (-not $paths.Add($relativePath)) {
            throw "The packaged Legal manifest repeats a path: $relativePath"
        }
        $path = Resolve-ExistingFile `
            -Path (Join-Path $ApplicationRoot ($relativePath -replace '/', '\')) `
            -Name $relativePath
        $actualSha256 = (Get-FileHash `
            -LiteralPath $path `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -cne $expectedSha256) {
            throw "The packaged Legal file hash is invalid: $relativePath"
        }
        if ($relativePath -ceq 'SBOM/manifest.spdx.json') {
            $sbomSha256 = $actualSha256
        }
    }
    if ($null -eq $sbomSha256) {
        throw 'The packaged Legal manifest does not authenticate the SPDX SBOM.'
    }

    $sbomPath = Join-Path $ApplicationRoot 'SBOM\manifest.spdx.json'
    $sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
    if ($sbom.spdxVersion -cne 'SPDX-2.3' -or
        $sbom.documentNamespace -cne
            $Identity.validatedPayload.legalBundleId) {
        throw 'The packaged SPDX SBOM identity is invalid.'
    }

    $legalDirectories = @(
        'LICENSES',
        'COPYRIGHTS',
        'SBOM',
        'SOURCE-OFFERS',
        'SOURCES',
        'RIGHTS'
    )
    foreach ($directory in $legalDirectories) {
        $directoryPath = Join-Path $ApplicationRoot $directory
        if (-not [System.IO.Directory]::Exists($directoryPath)) {
            continue
        }
        foreach ($path in [System.IO.Directory]::EnumerateFiles(
                $directoryPath,
                '*',
                [System.IO.SearchOption]::AllDirectories)) {
            $relativePath = [System.IO.Path]::GetRelativePath(
                $ApplicationRoot,
                $path) -replace '\', '/'
            if (-not $paths.Contains($relativePath)) {
                throw "An unauthenticated packaged Legal file exists: $relativePath"
            }
        }
    }

    return [pscustomobject]@{
        ManifestSha256 = $manifestSha256
        SbomSha256 = $sbomSha256
    }
}

$packagePath = Resolve-ExistingFile -Path $Package -Name 'Store MSIX package'
$packagingIdentityPath = Resolve-ExistingFile `
    -Path $PackagingIdentity `
    -Name 'Store packaging identity'
$identity = Get-Content -LiteralPath $packagingIdentityPath -Raw |
    ConvertFrom-Json
$packageSha256 = (Get-FileHash `
    -LiteralPath $packagePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($identity.schemaVersion -ne 1 -or
    $identity.packageFileName -cne [System.IO.Path]::GetFileName($packagePath) -or
    $identity.packageSha256 -cne $packageSha256) {
    throw 'The Store package does not match its packaging identity.'
}

$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
if ([System.IO.File]::Exists($outputPath) -or
    [System.IO.Directory]::Exists($outputPath)) {
    throw "OutputFile must not already exist: $outputPath"
}
$outputParent = [System.IO.Path]::GetDirectoryName($outputPath)
[System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
$scratchRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "vrrecorder-store-scan-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($scratchRoot) | Out-Null

try {
    $unpackedRoot = Join-Path $scratchRoot 'unpacked'
    $makeAppx = Get-MakeAppxPath
    & $makeAppx unpack /p $packagePath /d $unpackedRoot /o /v
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx unpack failed with exit code $LASTEXITCODE."
    }

    $privateKeyFiles = @([System.IO.Directory]::EnumerateFiles(
        $unpackedRoot,
        '*',
        [System.IO.SearchOption]::AllDirectories) | Where-Object {
            [System.IO.Path]::GetExtension($_) -in @(
                '.pfx', '.p12', '.pvk', '.snk', '.key', '.pem')
        })
    if ($privateKeyFiles.Count -ne 0) {
        throw 'A private-key file is present in the Store package.'
    }

    $legal = Assert-LegalManifest `
        -ApplicationRoot (Join-Path $unpackedRoot 'app') `
        -Identity $identity
    $defender = Get-DefenderCommandPath
    $defenderStatus = Get-MpComputerStatus
    if (-not $defenderStatus.AntivirusEnabled -or
        [string]::IsNullOrWhiteSpace(
            $defenderStatus.AntivirusSignatureVersion)) {
        throw 'Microsoft Defender Antivirus is not enabled and current.'
    }

    foreach ($scanTarget in @($packagePath, $unpackedRoot)) {
        & $defender.Path `
            -Scan `
            -ScanType 3 `
            -File $scanTarget `
            -DisableRemediation
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Microsoft Defender reported an unsuccessful scan for ' +
                "$scanTarget with exit code $LASTEXITCODE.")
        }
    }

    $capturedAtUtc = [DateTime]::UtcNow.ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $evidence = [ordered]@{
        schemaVersion = 1
        evidenceKind = 'store-final-scan-v1'
        packageSha256 = $packageSha256
        legalManifestSha256 = $legal.ManifestSha256
        sbomSha256 = $legal.SbomSha256
        scanner = 'Microsoft Defender Antivirus'
        scannerVersion = $defender.Version.ToString()
        definitionVersion = $defenderStatus.AntivirusSignatureVersion
        malwareScanPassed = $true
        legalBundleVerified = $true
        sbomPresent = $true
        privateKeysAbsent = $true
        capturedAtUtc = $capturedAtUtc
    }
    [System.IO.File]::WriteAllText(
        $outputPath,
        (($evidence | ConvertTo-Json -Depth 4) + "`n"),
        $utf8WithoutBom)
    Write-Output $outputPath
}
finally {
    if ([System.IO.Directory]::Exists($scratchRoot)) {
        $resolvedScratch = [System.IO.Path]::GetFullPath($scratchRoot)
        $temporaryPrefix = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::GetTempPath()).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if ($resolvedScratch.StartsWith(
                $temporaryPrefix,
                [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedScratch).StartsWith(
                'vrrecorder-store-scan-',
                [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
        }
        else {
            throw "Refusing to remove unexpected scratch path: $resolvedScratch"
        }
    }
}
