[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [Parameter(Mandatory = $true)]
    [string]$SubmissionId,

    [Parameter(Mandatory = $true)]
    [string]$CertificationReport,

    [Parameter(Mandatory = $true)]
    [string]$FlightReport,

    [Parameter(Mandatory = $true)]
    [string]$ApprovedBy,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

function Resolve-NonEmptyFile {
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
    if ($item.Length -le 0 -or
        ($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Name must be a non-empty regular file: $fullPath"
    }
    return $fullPath
}

foreach ($value in @($SubmissionId, $ApprovedBy)) {
    if ([string]::IsNullOrWhiteSpace($value) -or
        $value.Length -gt 512 -or
        $value.ToCharArray().Where({ $_ -lt ' ' -or $_ -gt '~' }).Count -ne 0) {
        throw 'Partner Center evidence identity fields must be concise ASCII.'
    }
}

$packagePath = Resolve-NonEmptyFile -Path $Package -Name 'Store package'
$certificationReportPath = Resolve-NonEmptyFile `
    -Path $CertificationReport `
    -Name 'Partner Center certification report'
$flightReportPath = Resolve-NonEmptyFile `
    -Path $FlightReport `
    -Name 'Partner Center flight report'
$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
if ([System.IO.File]::Exists($outputPath) -or
    [System.IO.Directory]::Exists($outputPath)) {
    throw "OutputFile must not already exist: $outputPath"
}

$evidence = [ordered]@{
    schemaVersion = 1
    evidenceKind = 'partner-center-public-release-v1'
    packageSha256 = (Get-FileHash `
        -LiteralPath $packagePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    submissionId = $SubmissionId
    certificationStatus = 'passed'
    certificationReportSha256 = (Get-FileHash `
        -LiteralPath $certificationReportPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    flightStatus = 'passed'
    flightReportSha256 = (Get-FileHash `
        -LiteralPath $flightReportPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    approvedBy = $ApprovedBy
    capturedAtUtc = [DateTime]::UtcNow.ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
}
[System.IO.Directory]::CreateDirectory(
    [System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[System.IO.File]::WriteAllText(
    $outputPath,
    (($evidence | ConvertTo-Json -Depth 4) + "`n"),
    $utf8WithoutBom)
Write-Output $outputPath
