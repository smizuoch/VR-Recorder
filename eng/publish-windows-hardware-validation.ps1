[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeManifest,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeSourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$LegalBundleDirectory,

    [Parameter(Mandatory = $true)]
    [string]$StagingOutputParent,

    [Parameter(Mandatory = $true)]
    [string]$PublishOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must not be a reparse point: $fullPath"
    }

    return $fullPath
}

$repositoryRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $PSScriptRoot '..') `
    -Name 'Repository root'
$sourceRoot = Resolve-ExistingDirectory `
    -Path $RuntimeSourceRoot `
    -Name 'Runtime source root'
$legalRoot = Resolve-ExistingDirectory `
    -Path $LegalBundleDirectory `
    -Name 'Legal Bundle directory'
$manifestPath = [System.IO.Path]::GetFullPath($RuntimeManifest)
if (-not [System.IO.File]::Exists($manifestPath)) {
    throw "RuntimeManifest must be an existing file: $manifestPath"
}

$sourceRevisionOutput = @(
    & git -C $repositoryRoot rev-parse --verify HEAD
)
if ($LASTEXITCODE -ne 0 -or
    $sourceRevisionOutput.Count -ne 1 -or
    $sourceRevisionOutput[0] -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
    throw 'Repository HEAD must resolve to one canonical source revision.'
}
$sourceRevision = $sourceRevisionOutput[0]
$repositoryStatus = @(
    & git -C $repositoryRoot status --porcelain=v1 --untracked-files=all
)
if ($LASTEXITCODE -ne 0 -or $repositoryStatus.Count -ne 0) {
    throw 'Release publish requires a clean repository working tree.'
}

$stagingParent = [System.IO.Path]::GetFullPath($StagingOutputParent)
[System.IO.Directory]::CreateDirectory($stagingParent) | Out-Null
$publishDirectory = [System.IO.Path]::GetFullPath($PublishOutput)
$identityOutput = [System.IO.Path]::GetFullPath(
    "$publishDirectory.application-payload-identity.v1.json")
if ([System.IO.Directory]::Exists($publishDirectory) -or
    [System.IO.File]::Exists($publishDirectory)) {
    throw "PublishOutput must not already exist: $publishDirectory"
}
if ([System.IO.Directory]::Exists($identityOutput) -or
    [System.IO.File]::Exists($identityOutput)) {
    throw "Payload identity output must not already exist: $identityOutput"
}

$releaseToolProject = Join-Path `
    $repositoryRoot `
    'src/VRRecorder.ReleaseTool/VRRecorder.ReleaseTool.csproj'
$stageOutput = @(
    & dotnet run `
        --project $releaseToolProject `
        --configuration Release `
        --no-launch-profile `
        -p:RestoreLockedMode=true `
        -- `
        stage-windows-runtime `
        --repository-root $repositoryRoot `
        --manifest $manifestPath `
        --source-root $sourceRoot `
        --output-parent $stagingParent
)
if ($LASTEXITCODE -ne 0) {
    throw "Windows runtime staging failed with exit code $LASTEXITCODE."
}

$propsCandidates = @($stageOutput | Where-Object {
    $_ -is [string] -and
    [System.IO.Path]::GetFileName($_) -eq 'ApprovedWindowsRuntime.props' -and
    [System.IO.File]::Exists($_)
})
if ($propsCandidates.Count -ne 1) {
    throw 'The staging CLI did not return exactly one approved props path.'
}

$approvedProps = [System.IO.Path]::GetFullPath($propsCandidates[0])
$digestDirectory = [System.IO.Path]::GetFileName(
    [System.IO.Path]::GetDirectoryName($approvedProps))
if ($digestDirectory -cnotmatch '^windows-runtime-[0-9a-f]{64}$') {
    throw "The approved props path is not in an immutable digest directory: $approvedProps"
}

$appProject = Join-Path `
    $repositoryRoot `
    'src/VRRecorder.App/VRRecorder.App.csproj'
& dotnet publish `
    $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:RestoreLockedMode=true `
    -p:VRRecorderUseWinX64LockGraph=true `
    "-p:SourceRevisionId=$sourceRevision" `
    "-p:ApprovedWindowsRuntimeProps=$approvedProps" `
    "-p:LegalBundleDirectory=$legalRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Release publish failed with exit code $LASTEXITCODE."
}

$entryPoint = Join-Path $publishDirectory 'VRRecorder.App.exe'
if (-not [System.IO.File]::Exists($entryPoint)) {
    throw "Release publish did not produce VRRecorder.App.exe: $entryPoint"
}

$postPublishRevision = @(
    & git -C $repositoryRoot rev-parse --verify HEAD
)
if ($LASTEXITCODE -ne 0 -or
    $postPublishRevision.Count -ne 1 -or
    $postPublishRevision[0] -cne $sourceRevision) {
    throw 'Repository HEAD changed during Release publish.'
}
$postPublishStatus = @(
    & git -C $repositoryRoot status --porcelain=v1 --untracked-files=all
)
if ($LASTEXITCODE -ne 0 -or $postPublishStatus.Count -ne 0) {
    throw 'Repository working tree changed during Release publish.'
}

$sealOutput = @(
    & dotnet run `
        --project $releaseToolProject `
        --configuration Release `
        --no-launch-profile `
        -p:RestoreLockedMode=true `
        -- `
        seal-windows-payload `
        --publish-root $publishDirectory `
        --approved-props $approvedProps `
        --identity-output $identityOutput
)
if ($LASTEXITCODE -ne 0) {
    throw "Windows payload sealing failed with exit code $LASTEXITCODE."
}

$identityCandidates = @($sealOutput | Where-Object {
    $_ -is [string] -and
    [System.IO.Path]::GetFullPath($_) -ceq $identityOutput -and
    [System.IO.File]::Exists($_)
})
if ($identityCandidates.Count -ne 1) {
    throw 'The sealing CLI did not return exactly one payload identity path.'
}

Write-Output $identityOutput
