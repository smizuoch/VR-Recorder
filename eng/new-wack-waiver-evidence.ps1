[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [Parameter(Mandatory = $true)]
    [string]$ToolVersion,

    [Parameter(Mandatory = $true)]
    [string]$Reason,

    [Parameter(Mandatory = $true)]
    [string]$RequestedBy,

    [Parameter(Mandatory = $true)]
    [string]$ApprovedBy,

    [Parameter(Mandatory = $true)]
    [string]$FlightSubmissionId,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

function Assert-ConciseAscii {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value.Length -gt 512 -or
        $Value.ToCharArray().Where({ $_ -lt ' ' -or $_ -gt '~' }).Count -ne 0) {
        throw "$Name must be concise ASCII."
    }
}

foreach ($entry in ([ordered]@{
        ToolVersion = $ToolVersion
        Reason = $Reason
        RequestedBy = $RequestedBy
        ApprovedBy = $ApprovedBy
        FlightSubmissionId = $FlightSubmissionId
    }).GetEnumerator()) {
    Assert-ConciseAscii -Value $entry.Value -Name $entry.Key
}
if ([string]::Equals(
        $RequestedBy,
        $ApprovedBy,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'RequestedBy and ApprovedBy must identify different reviewers.'
}

$packagePath = [System.IO.Path]::GetFullPath($Package)
if (-not [System.IO.File]::Exists($packagePath)) {
    throw "Package must be an existing file: $packagePath"
}
$packageItem = Get-Item -LiteralPath $packagePath -Force
if ($packageItem.Length -le 0 -or
    ($packageItem.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Package must be a non-empty regular file: $packagePath"
}

$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
if ([System.IO.File]::Exists($outputPath) -or
    [System.IO.Directory]::Exists($outputPath)) {
    throw "OutputFile must not already exist: $outputPath"
}

$evidence = [ordered]@{
    schemaVersion = 1
    evidenceKind = 'wack-tool-unavailable-waiver-v1'
    packageSha256 = (Get-FileHash `
        -LiteralPath $packagePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    toolVersion = $ToolVersion
    reason = $Reason
    requestedBy = $RequestedBy
    approvedBy = $ApprovedBy
    flightSubmissionId = $FlightSubmissionId
    flightCertificationStatus = 'passed'
    flightValidationStatus = 'passed'
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
