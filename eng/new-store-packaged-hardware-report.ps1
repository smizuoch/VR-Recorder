[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,

    [Parameter(Mandatory = $true)]
    [string]$OperatingSystem,

    [Parameter(Mandatory = $true)]
    [string]$Gpu,

    [Parameter(Mandatory = $true)]
    [string]$Driver,

    [Parameter(Mandatory = $true)]
    [string]$SteamVr,

    [Parameter(Mandatory = $true)]
    [string]$Hmd,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
$requiredCases = @(
    'spout2-wasapi-recording',
    'nvenc-recording',
    'amf-recording',
    'qsv-recording',
    'software-fallback-recording',
    'vrchat-recording',
    'openvr-overlay-controller',
    'wrist-haptics-move-pin-telemetry'
)

$packagePath = [System.IO.Path]::GetFullPath($Package)
$artifactRootPath = [System.IO.Path]::GetFullPath($ArtifactRoot)
$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
if (-not [System.IO.File]::Exists($packagePath)) {
    throw "Package must be an existing file: $packagePath"
}
if (-not [System.IO.Directory]::Exists($artifactRootPath)) {
    throw "ArtifactRoot must be an existing directory: $artifactRootPath"
}
if (((Get-Item -LiteralPath $artifactRootPath -Force).Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "ArtifactRoot must not be a reparse point: $artifactRootPath"
}
if ([System.IO.File]::Exists($outputPath) -or
    [System.IO.Directory]::Exists($outputPath)) {
    throw "OutputFile must not already exist: $outputPath"
}
foreach ($value in @($OperatingSystem, $Gpu, $Driver, $SteamVr, $Hmd)) {
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 512) {
        throw 'Every hardware environment value must be present and concise.'
    }
}

$cases = foreach ($caseId in $requiredCases) {
    $relativePath = "$caseId.json"
    $path = Join-Path $artifactRootPath $relativePath
    if (-not [System.IO.File]::Exists($path)) {
        throw "Required packaged hardware evidence is missing: $relativePath"
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -le 0) {
        throw "Packaged hardware evidence is invalid: $relativePath"
    }
    [ordered]@{
        caseId = $caseId
        status = 'passed'
        artifacts = @(
            [ordered]@{
                relativePath = $relativePath
                sha256 = (Get-FileHash `
                    -LiteralPath $path `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
    }
}

$actualFiles = @(Get-ChildItem -LiteralPath $artifactRootPath -File -Recurse)
if ($actualFiles.Count -ne $requiredCases.Count) {
    throw 'ArtifactRoot must contain exactly one artifact per required case.'
}
foreach ($directory in @(Get-ChildItem `
        -LiteralPath $artifactRootPath `
        -Directory `
        -Recurse `
        -Force)) {
    if (($directory.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "ArtifactRoot must not contain reparse points: $($directory.FullName)"
    }
}

$report = [ordered]@{
    schemaVersion = 1
    matrixProfile = 'store-packaged-hardware-validation-v1'
    packageSha256 = (Get-FileHash `
        -LiteralPath $packagePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    runs = @(
        [ordered]@{
            runId = [Guid]::NewGuid().ToString('D')
            capturedAtUtc = [DateTime]::UtcNow.ToString(
                'yyyy-MM-ddTHH:mm:ssZ',
                [System.Globalization.CultureInfo]::InvariantCulture)
            environment = [ordered]@{
                os = $OperatingSystem
                gpu = $Gpu
                driver = $Driver
                steamVr = $SteamVr
                hmd = $Hmd
            }
            cases = @($cases)
        }
    )
}
[System.IO.Directory]::CreateDirectory(
    [System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[System.IO.File]::WriteAllText(
    $outputPath,
    (($report | ConvertTo-Json -Depth 8) + "`n"),
    $utf8WithoutBom)
Write-Output $outputPath
