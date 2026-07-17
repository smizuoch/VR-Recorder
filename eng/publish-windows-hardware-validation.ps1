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

$stagingParent = [System.IO.Path]::GetFullPath($StagingOutputParent)
[System.IO.Directory]::CreateDirectory($stagingParent) | Out-Null
$publishDirectory = [System.IO.Path]::GetFullPath($PublishOutput)
if ([System.IO.Directory]::Exists($publishDirectory) -or
    [System.IO.File]::Exists($publishDirectory)) {
    throw "PublishOutput must not already exist: $publishDirectory"
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
    "-p:ApprovedWindowsRuntimeProps=$approvedProps" `
    "-p:LegalBundleDirectory=$legalRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Release publish failed with exit code $LASTEXITCODE."
}

$entryPoint = Join-Path $publishDirectory 'VRRecorder.App.exe'
if (-not [System.IO.File]::Exists($entryPoint)) {
    throw "Release publish did not produce VRRecorder.App.exe: $entryPoint"
}

Write-Output $publishDirectory
