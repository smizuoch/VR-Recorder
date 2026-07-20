[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NativeBuildRoot,

    [Parameter(Mandatory = $true)]
    [string]$ProductionFfmpegRoot,

    [Parameter(Mandatory = $true)]
    [string]$OracleFfmpegRoot,

    [Parameter(Mandatory = $true)]
    [string]$OpenVrRoot,

    [Parameter(Mandatory = $true)]
    [string]$MsvcRuntimeRoot,

    [Parameter(Mandatory = $true)]
    [string]$LegalBundleDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

function Resolve-ExistingDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.Directory]::Exists($fullPath)) {
        throw "$Name must be an existing directory: $fullPath"
    }

    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must not be a reparse point: $fullPath"
    }

    return $fullPath
}

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

    if ($item.Length -le 0) {
        throw "$Name must not be empty: $fullPath"
    }

    return $fullPath
}

function Add-RuntimeEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]]$Entries,

        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [Parameter(Mandatory = $true)]
        [string]$InputPath,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Target,

        [Parameter(Mandatory = $true)]
        [string]$Role,

        [Parameter(Mandatory = $true)]
        [string]$ComponentId,

        [Parameter(Mandatory = $true)]
        [string]$DeploymentKind
    )

    if ($Source -cnotmatch '^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$' -or
        $Target -cnotmatch '^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$') {
        throw "A runtime source or target path is not canonical: $Source -> $Target"
    }

    $input = Resolve-ExistingFile -Path $InputPath -Name $Target
    $destination = Join-Path $SourceRoot ($Source -replace '/', '\')
    [System.IO.Directory]::CreateDirectory(
        [System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    [System.IO.File]::Copy($input, $destination, $false)
    $copied = Get-Item -LiteralPath $destination -Force
    $sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $Entries.Add([ordered]@{
        source = $Source
        target = $Target
        role = $Role
        componentId = $ComponentId
        platform = 'windows-x64'
        deploymentKind = $DeploymentKind
        sha256 = $sha256
        length = $copied.Length
    })
}

$repositoryRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $PSScriptRoot '..') `
    -Name 'Repository root'
$nativeRoot = Resolve-ExistingDirectory `
    -Path $NativeBuildRoot `
    -Name 'Native build root'
$productionFfmpeg = Resolve-ExistingDirectory `
    -Path $ProductionFfmpegRoot `
    -Name 'Production FFmpeg root'
$oracleFfmpeg = Resolve-ExistingDirectory `
    -Path $OracleFfmpegRoot `
    -Name 'Oracle FFmpeg root'
$openVr = Resolve-ExistingDirectory `
    -Path $OpenVrRoot `
    -Name 'OpenVR root'
$msvcRuntime = Resolve-ExistingDirectory `
    -Path $MsvcRuntimeRoot `
    -Name 'MSVC runtime root'
$legalRoot = Resolve-ExistingDirectory `
    -Path $LegalBundleDirectory `
    -Name 'Legal Bundle directory'

$legalManifest = Resolve-ExistingFile `
    -Path (Join-Path $legalRoot 'LEGAL-MANIFEST.sha256') `
    -Name 'Legal manifest'
$legalCatalog = Resolve-ExistingFile `
    -Path (Join-Path $legalRoot 'THIRD-PARTY-COMPONENTS.json') `
    -Name 'Legal component catalog'
$catalog = Get-Content -LiteralPath $legalCatalog -Raw | ConvertFrom-Json
if ($catalog.bundleId -isnot [string] -or
    -not [System.Uri]::IsWellFormedUriString(
        $catalog.bundleId,
        [System.UriKind]::Absolute)) {
    throw 'The Legal component catalog does not contain an absolute bundleId.'
}
$legalManifestSha256 = (Get-FileHash `
    -LiteralPath $legalManifest `
    -Algorithm SHA256).Hash.ToLowerInvariant()

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([System.IO.Directory]::Exists($outputRoot) -or
    [System.IO.File]::Exists($outputRoot)) {
    throw "OutputDirectory must not already exist: $outputRoot"
}

$outputParent = [System.IO.Path]::GetDirectoryName($outputRoot)
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw 'OutputDirectory must have a parent directory.'
}
[System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
$scratchRoot = Join-Path `
    $outputParent `
    ".$([System.IO.Path]::GetFileName($outputRoot)).staging-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($scratchRoot) | Out-Null

try {
    $sourceRoot = Join-Path $scratchRoot 'source'
    [System.IO.Directory]::CreateDirectory($sourceRoot) | Out-Null
    $entries = [System.Collections.Generic.List[object]]::new()

    Add-RuntimeEntry $entries $sourceRoot `
        (Join-Path $nativeRoot "src\VRRecorder.Native\$Configuration\vrrecorder_native.dll") `
        'native/vrrecorder_native.dll' 'vrrecorder_native.dll' `
        'first-party-native' 'vr-recorder' 'native-library'
    Add-RuntimeEntry $entries $sourceRoot `
        (Join-Path $nativeRoot "native-factory-selection-$Configuration.json") `
        'evidence/native-factory-selection.json' `
        'native-factory-selection.json' `
        'factory-selection-evidence' 'vr-recorder' 'evidence'

    foreach ($fileName in @(
            'avcodec-62.dll',
            'avformat-62.dll',
            'avutil-60.dll',
            'swresample-6.dll')) {
        Add-RuntimeEntry $entries $sourceRoot `
            (Join-Path $productionFfmpeg "bin\$fileName") `
            "ffmpeg/$fileName" $fileName `
            'ffmpeg-runtime' 'ffmpeg' 'native-library'
    }
    Add-RuntimeEntry $entries $sourceRoot `
        (Join-Path $productionFfmpeg 'bin\libvpl.dll') `
        'ffmpeg/libvpl.dll' 'libvpl.dll' `
        'encoder-runtime' 'libvpl' 'native-library'
    Add-RuntimeEntry $entries $sourceRoot `
        (Join-Path $oracleFfmpeg 'bin\ffprobe.exe') `
        'ffmpeg-oracle/ffprobe.exe' 'ffprobe.exe' `
        'diagnostic-tool' 'ffmpeg' 'executable'
    Add-RuntimeEntry $entries $sourceRoot `
        (Join-Path $openVr 'bin\openvr_api.dll') `
        'openvr/openvr_api.dll' 'openvr_api.dll' `
        'openvr-runtime' 'openvr' 'native-library'

    foreach ($fileName in @(
            'msvcp140.dll',
            'msvcp140_atomic_wait.dll',
            'vcruntime140.dll',
            'vcruntime140_1.dll')) {
        Add-RuntimeEntry $entries $sourceRoot `
            (Join-Path $msvcRuntime $fileName) `
            "msvc/$fileName" $fileName `
            'toolchain-runtime' 'msvc-runtime' 'native-library'
    }

    $openVrAssets = Join-Path `
        $repositoryRoot `
        'src\VRRecorder.Infrastructure.SteamVr\OpenVr'
    foreach ($asset in @(
            @('steamvr.vrmanifest', 'OpenVr/steamvr.vrmanifest', 'openvr-manifest'),
            @('actions.json', 'OpenVr/actions.json', 'openvr-manifest'),
            @('bindings\knuckles.json', 'OpenVr/bindings/knuckles.json', 'openvr-binding'),
            @('bindings\oculus_touch.json', 'OpenVr/bindings/oculus_touch.json', 'openvr-binding'),
            @('bindings\vive_controller.json', 'OpenVr/bindings/vive_controller.json', 'openvr-binding'))) {
        Add-RuntimeEntry $entries $sourceRoot `
            (Join-Path $openVrAssets $asset[0]) `
            ("assets/" + $asset[1]) $asset[1] `
            $asset[2] 'openvr' 'asset'
    }

    $manifest = [ordered]@{
        schemaVersion = 2
        profile = 'full-production-hardware-validation-v1'
        runtimeIdentifier = 'win-x64'
        legalBundle = [ordered]@{
            bundleId = $catalog.bundleId
            manifestSha256 = $legalManifestSha256
        }
        entries = @($entries | Sort-Object { $_.target })
    }
    $manifestPath = Join-Path `
        $scratchRoot `
        'windows-runtime-staging-manifest.v2.json'
    [System.IO.File]::WriteAllText(
        $manifestPath,
        (($manifest | ConvertTo-Json -Depth 8) + "`n"),
        $utf8WithoutBom)

    [System.IO.Directory]::Move($scratchRoot, $outputRoot)
    Write-Output (Join-Path `
        $outputRoot `
        'windows-runtime-staging-manifest.v2.json')
}
finally {
    if ([System.IO.Directory]::Exists($scratchRoot)) {
        $resolvedScratch = [System.IO.Path]::GetFullPath($scratchRoot)
        $requiredPrefix = $outputParent.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if ($resolvedScratch.StartsWith(
                $requiredPrefix,
                [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedScratch).Contains(
                '.staging-',
                [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
        }
        else {
            throw "Refusing to remove unexpected scratch path: $resolvedScratch"
        }
    }
}
