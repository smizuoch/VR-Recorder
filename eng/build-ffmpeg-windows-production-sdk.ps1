#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter()]
    [string] $SdkRoot = (Join-Path $env:LOCALAPPDATA `
        'VRRecorder\Dependencies\ffmpeg-8.1.2-windows-msvc-x64'),

    [Parameter()]
    [string] $VisualStudioRoot = 'C:\BuildTools',

    [Parameter()]
    [string] $Msys2Root = 'C:\msys64',

    [Parameter()]
    [ValidateRange(1, 128)]
    [int] $BuildJobs = [Math]::Max(1, [Environment]::ProcessorCount)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$ffmpegVersion = '8.1.2'
$ffmpegTag = 'n8.1.2'
$sourceCommit = '38b88335f99e76ed89ff3c93f877fdefce736c13'
$sourceSha256 = '464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c'
$sourceUrl = "https://ffmpeg.org/releases/ffmpeg-$ffmpegVersion.tar.xz"
$sourcePatchUpstreamCommit = 'cec19d7ddf725896dfbf79a4c308550d83eab5ec'
$sourcePatchUpstreamUrl = 'https://code.ffmpeg.org/FFmpeg/FFmpeg/pulls/23039'
$sourcePatchSha256 = 'c8aca5fee1f02dbd1a1623de0333013e0c41fb691adf0ede3d4479ee32ac41c0'
$requiredMsvcVersion = '19.44.35228'
$requiredWindowsSdkVersion = '10.0.26100.0'
$ownershipToken = 'vr-recorder FFmpeg Windows production SDK workspace v1'
$ownerRelativePath = 'share\vrrecorder\windows-production-sdk-owned.txt'
$evidenceRelativePath = 'share\vrrecorder\ffmpeg-build-evidence.json'
$sourceArchiveRelativePath = `
    "share\vrrecorder\sources\ffmpeg-$ffmpegVersion.tar.xz"
$sourcePatchRelativePath = `
    'share\vrrecorder\patches\ffmpeg-8.1.2\0001-configure-redo-enabling-cbs-in-lavf.patch'
$recipeRelativePath = `
    'share\vrrecorder\build-recipes\ffmpeg-windows-x64.md'
$sourcePatchSourcePath = Join-Path $PSScriptRoot `
    'patches\ffmpeg-8.1.2\0001-configure-redo-enabling-cbs-in-lavf.patch'
$recipeSourcePath = Join-Path $PSScriptRoot `
    'ffmpeg-windows-production-build-recipe.md'
$recipeSha256 = '3579cddeb30c04a3a17bf3956ebbbfe87dccdd12081c0432fb4626e049beff01'
$artifactPaths = @(
    'bin/avcodec-62.dll',
    'bin/avformat-62.dll',
    'bin/avutil-60.dll',
    'bin/swresample-6.dll',
    'lib/avcodec.lib',
    'lib/avformat.lib',
    'lib/avutil.lib',
    'lib/swresample.lib'
)
$configureArgumentSuffix = @(
    '--toolchain=msvc',
    '--enable-cross-compile',
    '--host-cc=cl.exe',
    '--arch=x86_64',
    '--target-os=win32',
    '--enable-shared',
    '--disable-static',
    '--disable-programs',
    '--disable-doc',
    '--disable-network',
    '--disable-autodetect',
    '--disable-everything',
    '--disable-avdevice',
    '--disable-avfilter',
    '--disable-swscale',
    '--disable-iconv',
    '--disable-zlib',
    '--disable-bzlib',
    '--disable-lzma',
    '--disable-debug',
    '--disable-iamf',
    '--disable-x86asm',
    '--enable-avcodec',
    '--enable-avformat',
    '--enable-avutil',
    '--enable-swresample',
    '--enable-d3d11va',
    '--enable-mediafoundation',
    '--enable-encoder=aac',
    '--enable-encoder=h264_mf',
    '--enable-muxer=mp4',
    '--enable-protocol=file'
)

function Get-NormalizedAbsolutePath {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "Path must be absolute: $Path"
    }
    return [IO.Path]::TrimEndingDirectorySeparator(
        [IO.Path]::GetFullPath($Path))
}

function Test-IsPathInside {
    param(
        [Parameter(Mandatory)] [string] $Parent,
        [Parameter(Mandatory)] [string] $Child
    )

    $relative = [IO.Path]::GetRelativePath($Parent, $Child)
    return -not [IO.Path]::IsPathFullyQualified($relative) -and
        $relative -ne '..' -and
        -not $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)")
}

function Get-Sha256 {
    param([Parameter(Mandatory)] [string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-OwnedDirectory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $MarkerPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Expected owned directory is missing: $Path"
    }
    if (-not (Test-Path -LiteralPath $MarkerPath -PathType Leaf) -or
        (Get-Content -LiteralPath $MarkerPath -Raw).Trim() -ne $ownershipToken) {
        throw "Refusing directory without the exact ownership marker: $Path"
    }
}

function Remove-OwnedRoot {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ExpectedPath,
        [Parameter(Mandatory)] [string] $MarkerPath
    )

    $normalized = Get-NormalizedAbsolutePath $Path
    if ($normalized -ne (Get-NormalizedAbsolutePath $ExpectedPath)) {
        throw "Refusing to remove a root other than the named target: $normalized"
    }
    Assert-OwnedDirectory $normalized $MarkerPath
    Remove-Item -LiteralPath $normalized -Recurse -Force
}

function Remove-WorkChild {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $WorkRoot,
        [Parameter(Mandatory)] [string] $WorkMarkerPath
    )

    $normalized = Get-NormalizedAbsolutePath $Path
    if (-not (Test-IsPathInside $WorkRoot $normalized) -or
        $normalized -eq $WorkRoot) {
        throw "Refusing to remove a path outside the owned work root: $normalized"
    }
    Assert-OwnedDirectory $WorkRoot $WorkMarkerPath
    if (Test-Path -LiteralPath $normalized) {
        Remove-Item -LiteralPath $normalized -Recurse -Force
    }
}

function Convert-ToMsysPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Cygpath
    )

    $converted = (& $Cygpath -u $Path | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($converted)) {
        throw "MSYS2 could not convert path: $Path"
    }
    return $converted
}

function Convert-ToBashLiteral {
    param([Parameter(Mandatory)] [string] $Value)

    if ($Value.Contains("'")) {
        throw "MSYS2 build paths and arguments must not contain a single quote"
    }
    return "'$Value'"
}

function Import-VcVarsEnvironment {
    param(
        [Parameter(Mandatory)] [string] $VcVarsPath,
        [Parameter(Mandatory)] [string] $EnvironmentScriptPath
    )

    $content = @(
        '@echo off',
        "call `"$VcVarsPath`" >nul",
        'if errorlevel 1 exit /b %errorlevel%',
        'set'
    )
    [IO.File]::WriteAllLines(
        $EnvironmentScriptPath,
        $content,
        [Text.Encoding]::ASCII)
    $environmentLines = & $env:ComSpec /d /c $EnvironmentScriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "vcvars64 failed with exit code $LASTEXITCODE"
    }
    foreach ($line in $environmentLines) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }
        $name = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        Set-Item -LiteralPath "Env:$name" -Value $value
    }
}

function Get-EnabledComponents {
    param(
        [Parameter(Mandatory)] [string] $ConfigComponentsPath,
        [Parameter(Mandatory)] [string] $Suffix
    )

    $pattern = "^#define CONFIG_(.+)_${Suffix} 1$"
    return @(Get-Content -LiteralPath $ConfigComponentsPath |
        ForEach-Object {
            if ($_ -match $pattern) {
                $Matches[1].ToLowerInvariant()
            }
        } |
        Sort-Object)
}

function Assert-ExactSet {
    param(
        [Parameter(Mandatory)] [string] $Description,
        [Parameter()] [string[]] $Actual = @(),
        [Parameter()] [string[]] $Expected = @()
    )

    $actualText = (@($Actual | Sort-Object) -join ',')
    $expectedText = (@($Expected | Sort-Object) -join ',')
    if ($actualText -ne $expectedText) {
        throw "Unexpected $Description. Expected [$expectedText], actual [$actualText]"
    }
}

function Test-ExistingSdk {
    param([Parameter(Mandatory)] [string] $Root)

    try {
        $ownerPath = Join-Path $Root $ownerRelativePath
        Assert-OwnedDirectory $Root $ownerPath
        $evidencePath = Join-Path $Root $evidenceRelativePath
        if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
            return $false
        }
        $evidence = Get-Content -LiteralPath $evidencePath -Raw |
            ConvertFrom-Json
        if ($evidence.schemaVersion -ne 3 -or
            $evidence.version -ne $ffmpegVersion -or
            $evidence.sourceArchiveSha256 -ne $sourceSha256 -or
            $evidence.sourcePatchSha256 -ne $sourcePatchSha256 -or
            $evidence.sourcePatchUpstreamCommit -ne
                $sourcePatchUpstreamCommit -or
            $evidence.sourcePatchUpstreamUrl -ne $sourcePatchUpstreamUrl -or
            $evidence.buildRecipeSha256 -ne $recipeSha256 -or
            $evidence.msvcCompilerVersion -ne $requiredMsvcVersion -or
            $evidence.windowsSdkVersion -ne $requiredWindowsSdkVersion) {
            return $false
        }
        $sourcePath = Join-Path $Root $sourceArchiveRelativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
            (Get-Sha256 $sourcePath) -ne $sourceSha256 -or
            (Get-Item -LiteralPath $sourcePath).Length -ne
                $evidence.sourceArchiveLength) {
            return $false
        }
        $sourcePatchPath = Join-Path $Root $sourcePatchRelativePath
        if ($evidence.sourcePatchPath -ne
                $sourcePatchRelativePath.Replace('\', '/') -or
            -not (Test-Path -LiteralPath $sourcePatchPath -PathType Leaf) -or
            (Get-Sha256 $sourcePatchPath) -ne $sourcePatchSha256 -or
            (Get-Item -LiteralPath $sourcePatchPath).Length -ne
                $evidence.sourcePatchLength) {
            return $false
        }
        $recipePath = Join-Path $Root $recipeRelativePath
        if (-not (Test-Path -LiteralPath $recipePath -PathType Leaf) -or
            (Get-Sha256 $recipePath) -ne $evidence.buildRecipeSha256 -or
            (Get-Item -LiteralPath $recipePath).Length -ne
                $evidence.buildRecipeLength) {
            return $false
        }
        if (@($evidence.artifacts).Count -ne $artifactPaths.Count) {
            return $false
        }
        for ($index = 0; $index -lt $artifactPaths.Count; $index++) {
            $expectedPath = $artifactPaths[$index]
            $entry = $evidence.artifacts[$index]
            $actualPath = Join-Path $Root $expectedPath
            if ($entry.path -ne $expectedPath -or
                -not (Test-Path -LiteralPath $actualPath -PathType Leaf) -or
                (Get-Item -LiteralPath $actualPath).Length -ne $entry.length -or
                (Get-Sha256 $actualPath) -ne $entry.sha256) {
                return $false
            }
        }
        return $true
    } catch {
        return $false
    }
}

$SdkRoot = Get-NormalizedAbsolutePath $SdkRoot
$VisualStudioRoot = Get-NormalizedAbsolutePath $VisualStudioRoot
$Msys2Root = Get-NormalizedAbsolutePath $Msys2Root
$driveRoot = [IO.Path]::GetPathRoot($SdkRoot)
if ($SdkRoot -eq [IO.Path]::TrimEndingDirectorySeparator($driveRoot) -or
    $SdkRoot -eq (Get-NormalizedAbsolutePath $HOME)) {
    throw "Refusing unsafe SDK root: $SdkRoot"
}

$workRoot = Get-NormalizedAbsolutePath "$SdkRoot.work"
$workMarkerPath = Join-Path $workRoot 'windows-production-work-owned.txt'
$sdkOwnerPath = Join-Path $SdkRoot $ownerRelativePath
$archivePath = Join-Path $workRoot "ffmpeg-$ffmpegVersion.tar.xz"
$sourceRoot = Join-Path $workRoot 'source'
$buildLogPath = Join-Path $workRoot 'build.log'
$vcEnvironmentScriptPath = Join-Path $workRoot 'vcvars-env.cmd'
$msysBuildScriptPath = Join-Path $workRoot 'build-ffmpeg.sh'

if (-not (Test-Path -LiteralPath $recipeSourcePath -PathType Leaf)) {
    throw "Canonical build recipe is missing: $recipeSourcePath"
}
if ((Get-Sha256 $recipeSourcePath) -ne $recipeSha256) {
    throw 'Canonical build recipe SHA-256 does not match'
}
if (-not (Test-Path -LiteralPath $sourcePatchSourcePath -PathType Leaf)) {
    throw "Canonical FFmpeg source patch is missing: $sourcePatchSourcePath"
}
if ((Get-Sha256 $sourcePatchSourcePath) -ne $sourcePatchSha256) {
    throw 'Canonical FFmpeg source patch SHA-256 does not match'
}

if (Test-Path -LiteralPath $SdkRoot) {
    if (Test-ExistingSdk $SdkRoot) {
        Write-Output $SdkRoot
        exit 0
    }
    Remove-OwnedRoot $SdkRoot $SdkRoot $sdkOwnerPath
}

if (Test-Path -LiteralPath $workRoot) {
    Assert-OwnedDirectory $workRoot $workMarkerPath
} else {
    New-Item -ItemType Directory -Path $workRoot | Out-Null
    [IO.File]::WriteAllText(
        $workMarkerPath,
        "$ownershipToken`n",
        [Text.UTF8Encoding]::new($false))
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    $temporaryArchive = "$archivePath.download"
    if (Test-Path -LiteralPath $temporaryArchive) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
    Invoke-WebRequest -Uri $sourceUrl -OutFile $temporaryArchive
    if ((Get-Sha256 $temporaryArchive) -ne $sourceSha256) {
        Remove-Item -LiteralPath $temporaryArchive -Force
        throw 'Downloaded FFmpeg source archive SHA-256 does not match'
    }
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath
}
if ((Get-Sha256 $archivePath) -ne $sourceSha256) {
    throw "Cached FFmpeg source archive SHA-256 does not match: $archivePath"
}

Remove-WorkChild $sourceRoot $workRoot $workMarkerPath
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
& tar.exe --extract --xz --file $archivePath --directory $sourceRoot `
    --strip-components=1
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg source extraction failed with exit code $LASTEXITCODE"
}

$gitPath = (Get-Command git.exe -CommandType Application).Source
& $gitPath -C $sourceRoot apply --check $sourcePatchSourcePath
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg source patch preflight failed with exit code $LASTEXITCODE"
}
& $gitPath -C $sourceRoot apply $sourcePatchSourcePath
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg source patch application failed with exit code $LASTEXITCODE"
}

$vcVarsPath = Join-Path $VisualStudioRoot `
    'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcVarsPath -PathType Leaf)) {
    throw "vcvars64.bat is missing: $vcVarsPath"
}
Import-VcVarsEnvironment $vcVarsPath $vcEnvironmentScriptPath

$clPath = (Get-Command cl.exe -CommandType Application).Source
$linkPath = (Get-Command link.exe -CommandType Application).Source
$rcPath = (Get-Command rc.exe -CommandType Application).Source
$clOutput = (& $clPath 2>&1 | Out-String)
if ($clOutput -notmatch 'Version ([0-9.]+) for x64') {
    throw "Could not read the x64 MSVC compiler version: $clOutput"
}
$actualMsvcVersion = $Matches[1]
$actualWindowsSdkVersion = $env:WindowsSDKVersion.TrimEnd('\')
if ($actualMsvcVersion -ne $requiredMsvcVersion) {
    throw "MSVC version drift: expected $requiredMsvcVersion, actual $actualMsvcVersion"
}
if ($actualWindowsSdkVersion -ne $requiredWindowsSdkVersion) {
    throw "Windows SDK version drift: expected $requiredWindowsSdkVersion, actual $actualWindowsSdkVersion"
}

$bashPath = Join-Path $Msys2Root 'usr\bin\bash.exe'
$cygpathPath = Join-Path $Msys2Root 'usr\bin\cygpath.exe'
$makePath = Join-Path $Msys2Root 'usr\bin\make.exe'
foreach ($requiredPath in @($bashPath, $cygpathPath, $makePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required MSYS2 build tool is missing: $requiredPath"
    }
}

New-Item -ItemType Directory -Path $SdkRoot | Out-Null
$sdkOwnerParent = Split-Path -Parent $sdkOwnerPath
New-Item -ItemType Directory -Path $sdkOwnerParent -Force | Out-Null
[IO.File]::WriteAllText(
    $sdkOwnerPath,
    "$ownershipToken`n",
    [Text.UTF8Encoding]::new($false))

$sourceMsys = Convert-ToMsysPath $sourceRoot $cygpathPath
$sdkMsys = Convert-ToMsysPath $SdkRoot $cygpathPath
$buildScriptMsys = Convert-ToMsysPath $msysBuildScriptPath $cygpathPath
$msvcBinMsys = Convert-ToMsysPath (Split-Path -Parent $clPath) $cygpathPath
$windowsSdkBinMsys = Convert-ToMsysPath (Split-Path -Parent $rcPath) $cygpathPath
$configureArguments = @("--prefix=$sdkMsys") + $configureArgumentSuffix
$bashArguments = $configureArguments |
    ForEach-Object { Convert-ToBashLiteral $_ }
$buildScript = @(
    'set -euo pipefail',
    "export PATH=$(Convert-ToBashLiteral "$msvcBinMsys`:$windowsSdkBinMsys`:/usr/bin")",
    "cd $(Convert-ToBashLiteral $sourceMsys)",
    "./configure $($bashArguments -join ' ')",
    "make -j$BuildJobs",
    'make install'
) -join "`n"
[IO.File]::WriteAllText(
    $msysBuildScriptPath,
    "$buildScript`n",
    [Text.UTF8Encoding]::new($false))

$env:MSYS2_PATH_TYPE = 'inherit'
& $bashPath --noprofile --norc $buildScriptMsys 2>&1 |
    Tee-Object -FilePath $buildLogPath
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg Windows build failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path (Join-Path $SdkRoot 'lib') -Force |
    Out-Null
foreach ($component in @('avcodec', 'avformat', 'avutil', 'swresample')) {
    $installedImportLibrary = Join-Path $SdkRoot "bin\$component.lib"
    $contractImportLibrary = Join-Path $SdkRoot "lib\$component.lib"
    if (-not (Test-Path -LiteralPath $installedImportLibrary -PathType Leaf)) {
        throw "Installed MSVC import library is missing: $installedImportLibrary"
    }
    Move-Item -LiteralPath $installedImportLibrary `
        -Destination $contractImportLibrary
}

$componentsPath = Join-Path $sourceRoot 'config_components.h'
if (-not (Test-Path -LiteralPath $componentsPath -PathType Leaf)) {
    throw "FFmpeg component readback is missing: $componentsPath"
}
$enabledEncoders = Get-EnabledComponents $componentsPath 'ENCODER'
$enabledDecoders = Get-EnabledComponents $componentsPath 'DECODER'
$enabledMuxers = Get-EnabledComponents $componentsPath 'MUXER'
$enabledDemuxers = Get-EnabledComponents $componentsPath 'DEMUXER'
$enabledParsers = Get-EnabledComponents $componentsPath 'PARSER'
$enabledBitstreamFilters = Get-EnabledComponents $componentsPath 'BSF'
$enabledProtocols = Get-EnabledComponents $componentsPath 'PROTOCOL'
Assert-ExactSet 'encoders' $enabledEncoders @('aac', 'h264_mf')
Assert-ExactSet 'decoders' $enabledDecoders @()
Assert-ExactSet 'muxers' $enabledMuxers @('mov', 'mp4')
Assert-ExactSet 'demuxers' $enabledDemuxers @()
Assert-ExactSet 'parsers' $enabledParsers @('ac3')
Assert-ExactSet `
    'bitstream filters' `
    $enabledBitstreamFilters `
    @('aac_adtstoasc', 'vp9_superframe')
Assert-ExactSet 'protocols' $enabledProtocols @('file')

foreach ($program in @('ffmpeg.exe', 'ffplay.exe', 'ffprobe.exe')) {
    if (Test-Path -LiteralPath (Join-Path $SdkRoot "bin\$program")) {
        throw "Production SDK must not contain programs: $program"
    }
}
foreach ($path in $artifactPaths) {
    $absolutePath = Join-Path $SdkRoot $path
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Expected production SDK artifact is missing: $path"
    }
}

$sourceDestination = Join-Path $SdkRoot $sourceArchiveRelativePath
$sourcePatchDestination = Join-Path $SdkRoot $sourcePatchRelativePath
$recipeDestination = Join-Path $SdkRoot $recipeRelativePath
New-Item -ItemType Directory -Path (Split-Path -Parent $sourceDestination) `
    -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $recipeDestination) `
    -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $sourcePatchDestination) `
    -Force | Out-Null
Copy-Item -LiteralPath $archivePath -Destination $sourceDestination
Copy-Item -LiteralPath $sourcePatchSourcePath -Destination $sourcePatchDestination
Copy-Item -LiteralPath $recipeSourcePath -Destination $recipeDestination

$artifactEvidence = @($artifactPaths | ForEach-Object {
    $absolutePath = Join-Path $SdkRoot $_
    [ordered]@{
        path = $_
        length = (Get-Item -LiteralPath $absolutePath).Length
        sha256 = Get-Sha256 $absolutePath
    }
})
$evidence = [ordered]@{
    schemaVersion = 3
    version = $ffmpegVersion
    tag = $ffmpegTag
    sourceCommit = $sourceCommit
    sourceArchivePath = $sourceArchiveRelativePath.Replace('\', '/')
    sourceArchiveLength = (Get-Item -LiteralPath $sourceDestination).Length
    sourceArchiveSha256 = Get-Sha256 $sourceDestination
    sourcePatchPath = $sourcePatchRelativePath.Replace('\', '/')
    sourcePatchLength = (Get-Item -LiteralPath $sourcePatchDestination).Length
    sourcePatchSha256 = Get-Sha256 $sourcePatchDestination
    sourcePatchUpstreamCommit = $sourcePatchUpstreamCommit
    sourcePatchUpstreamUrl = $sourcePatchUpstreamUrl
    platform = 'windows-x64'
    toolchain = 'msvc'
    msvcCompilerVersion = $actualMsvcVersion
    windowsSdkVersion = $actualWindowsSdkVersion
    linkage = 'shared'
    license = 'LGPL version 2.1 or later'
    gpl = $false
    nonfree = $false
    configureArguments = $configureArguments
    enabledLibraries = @('avcodec', 'avformat', 'avutil', 'swresample')
    enabledEncoders = @('aac', 'h264_mf')
    enabledMuxers = @('mov', 'mp4')
    enabledParsers = @('ac3')
    enabledBitstreamFilters = @('aac_adtstoasc', 'vp9_superframe')
    enabledProtocols = @('file')
    enabledExternalLibraries = @('mediafoundation')
    enabledHardwareAccelerationLibraries = @('d3d11va')
    buildRecipePath = $recipeRelativePath.Replace('\', '/')
    buildRecipeLength = (Get-Item -LiteralPath $recipeDestination).Length
    buildRecipeSha256 = Get-Sha256 $recipeDestination
    artifacts = $artifactEvidence
}
$evidencePath = Join-Path $SdkRoot $evidenceRelativePath
[IO.File]::WriteAllText(
    $evidencePath,
    (($evidence | ConvertTo-Json -Depth 6) + "`n"),
    [Text.UTF8Encoding]::new($false))

if (-not (Test-ExistingSdk $SdkRoot)) {
    throw 'Generated Windows production SDK failed its post-build identity check'
}

Write-Output $SdkRoot
