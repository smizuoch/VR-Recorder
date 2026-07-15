#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter()]
    [string] $SdkRoot = (Join-Path $env:LOCALAPPDATA `
        'VRRecorder\Dependencies\spout2-2.007.017-windows-msvc-x64-static'),

    [Parameter()]
    [string] $DownloadRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = '2.007.017'
$tag = '2.007.017'
$sourceCommit = 'f49e2f469f8cb25f559a6eaa61a3f5b8173fc100'
$binaryArchiveName = 'Spout-SDK-binaries_2-007-017_1.zip'
$binaryArchiveLength = 3472666L
$binaryArchiveSha256 = `
    '695f20e3505fa0da51b2eb959af359f5d9e2c914bb9676e9118d19f6a5424bf4'
$binaryArchiveUrl = `
    'https://github.com/leadedge/Spout2/releases/download/2.007.017/Spout-SDK-binaries_2-007-017_1.zip'
$sourceArchiveName = "Spout2-$sourceCommit.tar.gz"
$sourceArchiveLength = 4920448L
$sourceArchiveSha256 = `
    '9d93cadc7fea63d3e8b26384da8f8f23982a06a07adb0363d75630a99ab1f8f1'
$sourceArchiveUrl = "https://github.com/leadedge/Spout2/archive/$sourceCommit.tar.gz"
$licenseLength = 1326L
$licenseSha256 = `
    '7b602b5c652a76ced1c6ff5f3f4c15c37a733230eeb5b8d075f1282b446b10be'
$recipeLength = 2300L
$recipeSha256 = `
    'cc14b99a8797658139b04b215ff32d4f9a800ec8dcc8f769006fd07385772535'
$ownershipToken = 'vr-recorder Spout2 Windows static SDK work directory v1'
$binaryArchiveRelativePath = "share/vrrecorder/sources/$binaryArchiveName"
$sourceArchiveRelativePath = "share/vrrecorder/sources/$sourceArchiveName"
$licenseRelativePath = 'share/vrrecorder/licenses/Spout2-LICENSE.txt'
$recipeRelativePath = `
    'share/vrrecorder/build-recipes/spout2-windows-x64-static.md'
$evidenceRelativePath = 'share/vrrecorder/spout2-sdk-evidence.json'
$recipeSourcePath = Join-Path $PSScriptRoot `
    'spout2-windows-x64-static-build-recipe.md'
$binarySdkPrefix = `
    'Spout-SDK-binaries\Libs_2-007-017'
$sourceRootName = "Spout2-$sourceCommit"
$artifactContracts = @(
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutCommon.h"
        Path = 'include/SpoutDX/SpoutCommon.h'
        Length = 3422L
        Sha256 = '9dbe0846831f9b396578b1b3288b684f7653c7f35718f429499097a9bf9bf063'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutCopy.h"
        Path = 'include/SpoutDX/SpoutCopy.h'
        Length = 9704L
        Sha256 = '0ca6e26cb5c3e280ca85854b36187dfc996437f11702410f4e028cbc7e4b6503'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutDirectX.h"
        Path = 'include/SpoutDX/SpoutDirectX.h'
        Length = 7445L
        Sha256 = '809f678d270b4a22ea1854a2a83daf4c534535f20a18b2eab8fa5038086bc397'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutDX.h"
        Path = 'include/SpoutDX/SpoutDX.h'
        Length = 12426L
        Sha256 = 'b181be79f2cc3d1830a6c4cf38d8b63dda25fdc4f3558d606b2d1d23b3816446'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutFrameCount.h"
        Path = 'include/SpoutDX/SpoutFrameCount.h'
        Length = 5943L
        Sha256 = '5e09d1aa49005f3cfb8dd1ed5328b78d0223d08630c06eee66213cbcdaf3b4f9'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutSenderNames.h"
        Path = 'include/SpoutDX/SpoutSenderNames.h'
        Length = 8545L
        Sha256 = 'd9ce7069c9403378eac4cb4d3082ce2657504269cd92bb4b8bb3461be2ec4e83'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutSharedMemory.h"
        Path = 'include/SpoutDX/SpoutSharedMemory.h'
        Length = 2772L
        Sha256 = '5567da567945d0feed074c73ffcf76c5917c10506f86d167fc765da5aa62271d'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\include\SpoutDX\SpoutUtils.h"
        Path = 'include/SpoutDX/SpoutUtils.h'
        Length = 15025L
        Sha256 = '22346227a4815855ada400b4980dd9a50a026dbf6d398fefbef4d34c0f4349eb'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\MD\lib\SpoutDX_static.lib"
        Path = 'lib/SpoutDX_static.lib'
        Length = 1081676L
        Sha256 = '1e9aa2d17d05108af2f8eebb405a8d3b81355cef4633c110efab3886b7867afb'
    },
    [pscustomobject]@{
        Source = "$binarySdkPrefix\MD\lib\Spout_static.lib"
        Path = 'lib/Spout_static.lib'
        Length = 1441554L
        Sha256 = 'ce3fdd36584d0e722f73f7eb26b66335c5948c25933304ba206af6ad32d7edbb'
    }
)

function Get-NormalizedAbsolutePath {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "Path must be absolute: $Path"
    }
    return [IO.Path]::TrimEndingDirectorySeparator(
        [IO.Path]::GetFullPath($Path))
}

function Get-Sha256 {
    param([Parameter(Mandatory)] [string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-FileIdentity {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [long] $Length,
        [Parameter(Mandatory)] [string] $Sha256,
        [Parameter(Mandatory)] [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing $Description`: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a reparse point: $Path"
    }
    if ($item.Length -ne $Length) {
        throw "$Description length mismatch. Expected $Length, actual $($item.Length): $Path"
    }
    $actualSha256 = Get-Sha256 $Path
    if ($actualSha256 -ne $Sha256) {
        throw "$Description SHA-256 mismatch. Expected $Sha256, actual ${actualSha256}: $Path"
    }
}

function Get-ExactInventory {
    param([Parameter(Mandatory)] [string] $Root)

    return @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
        ForEach-Object {
            [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        } |
        Sort-Object)
}

function Assert-SdkRoot {
    param([Parameter(Mandatory)] [string] $Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Spout2 SDK root is missing: $Root"
    }
    $rootItem = Get-Item -LiteralPath $Root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Spout2 SDK root must not be a reparse point: $Root"
    }

    Assert-FileIdentity `
        (Join-Path $Root $binaryArchiveRelativePath) `
        $binaryArchiveLength $binaryArchiveSha256 'binary archive'
    Assert-FileIdentity `
        (Join-Path $Root $sourceArchiveRelativePath) `
        $sourceArchiveLength $sourceArchiveSha256 'source archive'
    Assert-FileIdentity `
        (Join-Path $Root $licenseRelativePath) `
        $licenseLength $licenseSha256 'license'
    Assert-FileIdentity `
        (Join-Path $Root $recipeRelativePath) `
        $recipeLength $recipeSha256 'build recipe'
    foreach ($artifact in $artifactContracts) {
        Assert-FileIdentity `
            (Join-Path $Root $artifact.Path) `
            $artifact.Length $artifact.Sha256 "artifact $($artifact.Path)"
    }

    $evidencePath = Join-Path $Root $evidenceRelativePath
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "Spout2 SDK evidence is missing: $evidencePath"
    }
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.component -ne 'spout2' -or
        $evidence.version -ne $version -or
        $evidence.tag -ne $tag -or
        $evidence.sourceCommit -ne $sourceCommit -or
        $evidence.architecture -ne 'x86_64' -or
        $evidence.runtimeLibrary -ne 'MD' -or
        $evidence.deployment -ne 'static' -or
        $evidence.binaryArchivePath -ne $binaryArchiveRelativePath -or
        $evidence.binaryArchiveLength -ne $binaryArchiveLength -or
        $evidence.binaryArchiveSha256 -ne $binaryArchiveSha256 -or
        $evidence.sourceArchivePath -ne $sourceArchiveRelativePath -or
        $evidence.sourceArchiveLength -ne $sourceArchiveLength -or
        $evidence.sourceArchiveSha256 -ne $sourceArchiveSha256 -or
        $evidence.licensePath -ne $licenseRelativePath -or
        $evidence.licenseLength -ne $licenseLength -or
        $evidence.licenseSha256 -ne $licenseSha256 -or
        $evidence.buildRecipePath -ne $recipeRelativePath -or
        $evidence.buildRecipeLength -ne $recipeLength -or
        $evidence.buildRecipeSha256 -ne $recipeSha256) {
        throw 'Spout2 SDK evidence metadata does not match the exact contract'
    }
    if (@($evidence.artifacts).Count -ne $artifactContracts.Count) {
        throw 'Spout2 SDK evidence artifact count does not match'
    }
    for ($index = 0; $index -lt $artifactContracts.Count; $index++) {
        $expected = $artifactContracts[$index]
        $actual = $evidence.artifacts[$index]
        if ($actual.path -ne $expected.Path -or
            $actual.length -ne $expected.Length -or
            $actual.sha256 -ne $expected.Sha256) {
            throw "Spout2 SDK evidence artifact $index does not match"
        }
    }

    $expectedInventory = @(
        $binaryArchiveRelativePath
        $sourceArchiveRelativePath
        $licenseRelativePath
        $recipeRelativePath
        $evidenceRelativePath
    ) + @($artifactContracts | ForEach-Object { $_.Path })
    $expectedInventory = @($expectedInventory | Sort-Object)
    $actualInventory = Get-ExactInventory $Root
    if (($actualInventory -join "`n") -ne ($expectedInventory -join "`n")) {
        throw "Spout2 SDK inventory is not exact. Expected [$($expectedInventory -join ', ')], actual [$($actualInventory -join ', ')]"
    }
}

function Get-OrDownloadArchive {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [long] $Length,
        [Parameter(Mandatory)] [string] $Sha256,
        [Parameter(Mandatory)] [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Invoke-WebRequest `
            -Headers @{ 'User-Agent' = 'VR-Recorder-Spout2-SDK-preparer' } `
            -Uri $Url `
            -OutFile $Path
    }
    Assert-FileIdentity $Path $Length $Sha256 $Description
}

$SdkRoot = Get-NormalizedAbsolutePath $SdkRoot
$sdkParent = [IO.Path]::GetDirectoryName($SdkRoot)
if ([string]::IsNullOrWhiteSpace($DownloadRoot)) {
    $DownloadRoot = Join-Path $sdkParent '.vrrecorder-downloads'
}
$DownloadRoot = Get-NormalizedAbsolutePath $DownloadRoot

Assert-FileIdentity $recipeSourcePath $recipeLength $recipeSha256 'repository build recipe'

if (Test-Path -LiteralPath $SdkRoot) {
    Assert-SdkRoot $SdkRoot
    Write-Output "Validated existing Spout2 SDK: $SdkRoot"
    exit 0
}

New-Item -ItemType Directory -Path $sdkParent -Force | Out-Null
New-Item -ItemType Directory -Path $DownloadRoot -Force | Out-Null
$binaryArchivePath = Join-Path $DownloadRoot $binaryArchiveName
$sourceArchivePath = Join-Path $DownloadRoot $sourceArchiveName
Get-OrDownloadArchive `
    $binaryArchivePath $binaryArchiveUrl `
    $binaryArchiveLength $binaryArchiveSha256 'binary archive'
Get-OrDownloadArchive `
    $sourceArchivePath $sourceArchiveUrl `
    $sourceArchiveLength $sourceArchiveSha256 'source archive'

$workRoot = Join-Path $sdkParent `
    "$([IO.Path]::GetFileName($SdkRoot)).work-$([Guid]::NewGuid().ToString('N'))"
$workRoot = Get-NormalizedAbsolutePath $workRoot
$workMarkerPath = Join-Path $workRoot '.vrrecorder-owned-work.txt'
$stageRoot = Join-Path $workRoot 'sdk'
$binaryExtractRoot = Join-Path $workRoot 'binary'
$sourceExtractRoot = Join-Path $workRoot 'source'

try {
    New-Item -ItemType Directory -Path $workRoot | Out-Null
    [IO.File]::WriteAllText(
        $workMarkerPath,
        "$ownershipToken`n",
        [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    New-Item -ItemType Directory -Path $binaryExtractRoot | Out-Null
    New-Item -ItemType Directory -Path $sourceExtractRoot | Out-Null

    Expand-Archive `
        -LiteralPath $binaryArchivePath `
        -DestinationPath $binaryExtractRoot
    $tar = (Get-Command tar.exe -ErrorAction Stop).Source
    & $tar -xf $sourceArchivePath -C $sourceExtractRoot
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed to extract the source archive ($LASTEXITCODE)"
    }

    foreach ($artifact in $artifactContracts) {
        $source = Join-Path $binaryExtractRoot $artifact.Source
        Assert-FileIdentity `
            $source $artifact.Length $artifact.Sha256 `
            "extracted artifact $($artifact.Path)"
        $destination = Join-Path $stageRoot $artifact.Path
        New-Item `
            -ItemType Directory `
            -Path ([IO.Path]::GetDirectoryName($destination)) `
            -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }

    $sourceLicensePath = Join-Path $sourceExtractRoot "$sourceRootName\LICENSE"
    Assert-FileIdentity `
        $sourceLicensePath $licenseLength $licenseSha256 `
        'source archive license'

    foreach ($copy in @(
            [pscustomobject]@{
                Source = $binaryArchivePath
                Destination = $binaryArchiveRelativePath
            },
            [pscustomobject]@{
                Source = $sourceArchivePath
                Destination = $sourceArchiveRelativePath
            },
            [pscustomobject]@{
                Source = $sourceLicensePath
                Destination = $licenseRelativePath
            },
            [pscustomobject]@{
                Source = $recipeSourcePath
                Destination = $recipeRelativePath
            })) {
        $destination = Join-Path $stageRoot $copy.Destination
        New-Item `
            -ItemType Directory `
            -Path ([IO.Path]::GetDirectoryName($destination)) `
            -Force | Out-Null
        Copy-Item -LiteralPath $copy.Source -Destination $destination
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        component = 'spout2'
        version = $version
        tag = $tag
        sourceCommit = $sourceCommit
        architecture = 'x86_64'
        runtimeLibrary = 'MD'
        deployment = 'static'
        binaryArchivePath = $binaryArchiveRelativePath
        binaryArchiveLength = $binaryArchiveLength
        binaryArchiveSha256 = $binaryArchiveSha256
        sourceArchivePath = $sourceArchiveRelativePath
        sourceArchiveLength = $sourceArchiveLength
        sourceArchiveSha256 = $sourceArchiveSha256
        licensePath = $licenseRelativePath
        licenseLength = $licenseLength
        licenseSha256 = $licenseSha256
        buildRecipePath = $recipeRelativePath
        buildRecipeLength = $recipeLength
        buildRecipeSha256 = $recipeSha256
        artifacts = @($artifactContracts | ForEach-Object {
            [ordered]@{
                path = $_.Path
                length = $_.Length
                sha256 = $_.Sha256
            }
        })
    }
    $evidencePath = Join-Path $stageRoot $evidenceRelativePath
    New-Item `
        -ItemType Directory `
        -Path ([IO.Path]::GetDirectoryName($evidencePath)) `
        -Force | Out-Null
    $evidenceJson = $evidence | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        $evidencePath,
        "$evidenceJson`n",
        [Text.UTF8Encoding]::new($false))

    Assert-SdkRoot $stageRoot
    Move-Item -LiteralPath $stageRoot -Destination $SdkRoot
    Assert-SdkRoot $SdkRoot
    Write-Output "Prepared pinned Spout2 SDK: $SdkRoot"
}
catch {
    throw
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        $normalizedParent = Get-NormalizedAbsolutePath $sdkParent
        $relativeWork = [IO.Path]::GetRelativePath($normalizedParent, $workRoot)
        if ([IO.Path]::IsPathFullyQualified($relativeWork) -or
            $relativeWork -eq '..' -or
            $relativeWork.StartsWith("..$([IO.Path]::DirectorySeparatorChar)")) {
            throw "Refusing to remove work directory outside the SDK parent: $workRoot"
        }
        if (-not (Test-Path -LiteralPath $workMarkerPath -PathType Leaf) -or
            (Get-Content -LiteralPath $workMarkerPath -Raw).Trim() -ne $ownershipToken) {
            throw "Refusing to remove unowned work directory: $workRoot"
        }
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
