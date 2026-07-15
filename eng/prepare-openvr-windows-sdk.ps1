[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SdkRoot,

    [string]$SourceArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Version = '2.15.6'
$Tag = 'v2.15.6'
$SourceCommit = '0924064316de3effbcd1acf1e309182a2deb1c05'
$TagObject = '41bc3825fd35b04047610c86fee26fb33b017b29'
$ArchiveUrl = 'https://codeload.github.com/ValveSoftware/openvr/tar.gz/refs/tags/v2.15.6'
$ArchiveLength = 154998016L
$ArchiveSha256 = 'e184cb625010fab7043a9d5e1e000fdeb3067a152bb3169ef53f64dfac37164c'
$RecipeLength = 2249L
$RecipeSha256 = '4f1fcbffe5f352d5f8c5252861dc2c9fca670f227d86d84a099fc22af6da61ca'
$LicenseLength = 1488L
$LicenseSha256 = 'f56ff606104d4ef18e617921a75c73ad73b5a1a1d70c69590c29de16919e04ad'

$ArtifactIdentities = [ordered]@{
    'include/openvr.h' = [pscustomobject]@{
        Length = 296217L
        Sha256 = '1e6ed57199896cc1f7c5484e50fa18955e97be15be690beb28d998c877ead7fd'
    }
    'lib/openvr_api.lib' = [pscustomobject]@{
        Length = 5500L
        Sha256 = 'a0bf57c5920f569e8d21ab3e5bc95bac4b73e2016217f8b5b93495a2a7197bbb'
    }
    'bin/openvr_api.dll' = [pscustomobject]@{
        Length = 837272L
        Sha256 = 'bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a'
    }
    'bin/openvr_api.dll.sig' = [pscustomobject]@{
        Length = 1450L
        Sha256 = '6a47bb6e5e3d6850aef60abf4fb6b6f1799bee65f2af3bbdc89dac00b843bc5b'
    }
}

$SourceArchiveRelativePath = 'share/vrrecorder/sources/OpenVR-v2.15.6.tar.gz'
$LicenseRelativePath = 'share/vrrecorder/licenses/OpenVR-LICENSE.txt'
$RecipeRelativePath = 'share/vrrecorder/build-recipes/openvr-windows-x64-sdk.md'
$EvidenceRelativePath = 'share/vrrecorder/openvr-sdk-evidence.json'

function Get-NormalizedFullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Assert-FileIdentity(
    [string]$Path,
    [long]$ExpectedLength,
    [string]$ExpectedSha256,
    [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a reparse point: $Path"
    }
    if ($item.Length -ne $ExpectedLength) {
        throw "$Description length mismatch: $($item.Length)"
    }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $ExpectedSha256) {
        throw "$Description SHA-256 mismatch: $actualHash"
    }
}

function Assert-ExactSdk([string]$Root) {
    $expected = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($relativePath in $ArtifactIdentities.Keys) {
        $identity = $ArtifactIdentities[$relativePath]
        Assert-FileIdentity `
            (Join-Path $Root $relativePath) `
            $identity.Length `
            $identity.Sha256 `
            "OpenVR artifact $relativePath"
        [void]$expected.Add($relativePath)
    }
    Assert-FileIdentity `
        (Join-Path $Root $SourceArchiveRelativePath) `
        $ArchiveLength `
        $ArchiveSha256 `
        'OpenVR source archive'
    Assert-FileIdentity `
        (Join-Path $Root $LicenseRelativePath) `
        $LicenseLength `
        $LicenseSha256 `
        'OpenVR license'
    Assert-FileIdentity `
        (Join-Path $Root $RecipeRelativePath) `
        $RecipeLength `
        $RecipeSha256 `
        'OpenVR preparation recipe'
    foreach ($relativePath in @(
        $SourceArchiveRelativePath,
        $LicenseRelativePath,
        $RecipeRelativePath,
        $EvidenceRelativePath)) {
        [void]$expected.Add($relativePath)
    }

    $evidencePath = Join-Path $Root $EvidenceRelativePath
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "OpenVR SDK evidence is missing: $evidencePath"
    }
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.component -cne 'openvr' -or
        $evidence.version -cne $Version -or
        $evidence.tag -cne $Tag -or
        $evidence.sourceCommit -cne $SourceCommit -or
        $evidence.tagObject -cne $TagObject -or
        $evidence.architecture -cne 'x86_64' -or
        $evidence.deployment -cne 'dynamic') {
        throw 'OpenVR SDK evidence identity is invalid.'
    }

    $actual = Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
    }
    if ($actual.Count -ne $expected.Count) {
        throw 'OpenVR SDK inventory count is not exact.'
    }
    foreach ($relativePath in $actual) {
        if (-not $expected.Contains($relativePath)) {
            throw "Unexpected OpenVR SDK file: $relativePath"
        }
    }
}

$resolvedSdkRoot = Get-NormalizedFullPath $SdkRoot
if (-not [System.IO.Path]::IsPathFullyQualified($resolvedSdkRoot) -or
    [System.IO.Path]::GetPathRoot($resolvedSdkRoot) -eq $resolvedSdkRoot) {
    throw 'SdkRoot must be a non-root absolute path.'
}
if (Test-Path -LiteralPath $resolvedSdkRoot) {
    Assert-ExactSdk $resolvedSdkRoot
    Write-Host "Validated existing OpenVR SDK: $resolvedSdkRoot"
    exit 0
}

$repositoryRoot = Get-NormalizedFullPath (Join-Path $PSScriptRoot '..')
$recipeSource = Join-Path $repositoryRoot 'eng/openvr-windows-x64-sdk-recipe.md'
$licenseSource = Join-Path $repositoryRoot 'third-party/licenses/openvr/LICENSE.txt'
Assert-FileIdentity $recipeSource $RecipeLength $RecipeSha256 'repository OpenVR recipe'
Assert-FileIdentity $licenseSource $LicenseLength $LicenseSha256 'repository OpenVR license'

$parent = Split-Path -Parent $resolvedSdkRoot
$leaf = Split-Path -Leaf $resolvedSdkRoot
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$workRoot = Join-Path $parent (".$leaf.prepare-" + [guid]::NewGuid().ToString('N'))
$payloadRoot = Join-Path $workRoot 'payload'
$extractRoot = Join-Path $workRoot 'source'
$downloadPath = Join-Path $workRoot 'OpenVR-v2.15.6.tar.gz'

try {
    New-Item -ItemType Directory -Path $workRoot, $payloadRoot, $extractRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace($SourceArchivePath)) {
        & curl.exe --fail --location --retry 3 --connect-timeout 15 --max-time 300 `
            --output $downloadPath $ArchiveUrl
        if ($LASTEXITCODE -ne 0) {
            throw "OpenVR archive download failed with exit code $LASTEXITCODE."
        }
    }
    else {
        $providedArchive = Get-NormalizedFullPath $SourceArchivePath
        Copy-Item -LiteralPath $providedArchive -Destination $downloadPath
    }
    Assert-FileIdentity $downloadPath $ArchiveLength $ArchiveSha256 'downloaded OpenVR archive'

    & tar.exe -xzf $downloadPath -C $extractRoot
    if ($LASTEXITCODE -ne 0) {
        throw "OpenVR archive extraction failed with exit code $LASTEXITCODE."
    }
    $sourceRoot = @(Get-ChildItem -LiteralPath $extractRoot -Directory)
    if ($sourceRoot.Count -ne 1) {
        throw 'OpenVR archive must contain exactly one root directory.'
    }
    $sourceRoot = $sourceRoot[0].FullName

    foreach ($directory in @(
        'include',
        'lib',
        'bin',
        'share/vrrecorder/sources',
        'share/vrrecorder/licenses',
        'share/vrrecorder/build-recipes')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $payloadRoot $directory) | Out-Null
    }
    $copies = [ordered]@{
        'headers/openvr.h' = 'include/openvr.h'
        'lib/win64/openvr_api.lib' = 'lib/openvr_api.lib'
        'bin/win64/openvr_api.dll' = 'bin/openvr_api.dll'
        'bin/win64/openvr_api.dll.sig' = 'bin/openvr_api.dll.sig'
    }
    foreach ($sourceRelativePath in $copies.Keys) {
        Copy-Item `
            -LiteralPath (Join-Path $sourceRoot $sourceRelativePath) `
            -Destination (Join-Path $payloadRoot $copies[$sourceRelativePath])
    }
    Copy-Item -LiteralPath $downloadPath -Destination (Join-Path $payloadRoot $SourceArchiveRelativePath)
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $payloadRoot $LicenseRelativePath)
    Copy-Item -LiteralPath $recipeSource -Destination (Join-Path $payloadRoot $RecipeRelativePath)

    $artifactEvidence = foreach ($relativePath in $ArtifactIdentities.Keys) {
        $identity = $ArtifactIdentities[$relativePath]
        [ordered]@{
            path = $relativePath
            length = $identity.Length
            sha256 = $identity.Sha256
        }
    }
    $evidence = [ordered]@{
        schemaVersion = 1
        component = 'openvr'
        version = $Version
        tag = $Tag
        sourceCommit = $SourceCommit
        tagObject = $TagObject
        architecture = 'x86_64'
        deployment = 'dynamic'
        sourceArchivePath = $SourceArchiveRelativePath
        sourceArchiveLength = $ArchiveLength
        sourceArchiveSha256 = $ArchiveSha256
        licensePath = $LicenseRelativePath
        licenseLength = $LicenseLength
        licenseSha256 = $LicenseSha256
        buildRecipePath = $RecipeRelativePath
        buildRecipeLength = $RecipeLength
        buildRecipeSha256 = $RecipeSha256
        artifacts = @($artifactEvidence)
    }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $payloadRoot $EvidenceRelativePath) `
        -Encoding utf8NoBOM

    Assert-ExactSdk $payloadRoot
    Move-Item -LiteralPath $payloadRoot -Destination $resolvedSdkRoot
    Assert-ExactSdk $resolvedSdkRoot
    Write-Host "Prepared OpenVR SDK: $resolvedSdkRoot"
}
finally {
    $resolvedWorkRoot = Get-NormalizedFullPath $workRoot
    $parentPrefix = $parent + [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedWorkRoot.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedWorkRoot).StartsWith(".$leaf.prepare-", [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
