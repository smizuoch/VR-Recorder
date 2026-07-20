[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IdentityFile,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ValidatedInputRoot,

    [Parameter(Mandatory = $true)]
    [string]$ValidatedRunId,

    [Parameter(Mandatory = $true)]
    [string]$ValidatedArtifactId,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackagingRevision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$foundationNamespace =
    'http://schemas.microsoft.com/appx/manifest/foundation/windows10'
$uapNamespace =
    'http://schemas.microsoft.com/appx/manifest/uap/windows10'
$uap10Namespace =
    'http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
$restrictedCapabilitiesNamespace =
    'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities'
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

    return $fullPath
}

function Read-StoreIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $identity = Get-Content -LiteralPath $Path -Raw |
        ConvertFrom-Json
    $expectedProperties = @(
        'PackageIdentityName',
        'Publisher',
        'PublisherDisplayName'
    )
    $actualProperties = @($identity.PSObject.Properties.Name)
    if ($actualProperties.Count -ne $expectedProperties.Count -or
        @($expectedProperties | Where-Object {
            $_ -cnotin $actualProperties
        }).Count -ne 0) {
        throw 'The Store identity file has an invalid schema.'
    }

    foreach ($property in $expectedProperties) {
        if ($identity.$property -isnot [string] -or
            [string]::IsNullOrWhiteSpace($identity.$property)) {
            throw "The Store identity value is missing: $property"
        }
    }

    return $identity
}

function Assert-MsixVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -cnotmatch
        '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.0$') {
        throw 'Version must use canonical A.B.C.0 format.'
    }

    $components = @($Value.Split('.') | ForEach-Object {
        [int]::Parse($_, [System.Globalization.CultureInfo]::InvariantCulture)
    })
    if (@($components | Where-Object { $_ -gt 65535 }).Count -ne 0) {
        throw 'Each MSIX version component must be between 0 and 65535.'
    }
}

function Get-MakeAppxPath {
    $windowsKitsRoot = Join-Path `
        ${env:ProgramFiles(x86)} `
        'Windows Kits\10\bin'
    if (-not [System.IO.Directory]::Exists($windowsKitsRoot)) {
        throw "Windows SDK bin directory was not found: $windowsKitsRoot"
    }

    $candidates = @(Get-ChildItem -LiteralPath $windowsKitsRoot `
        -Directory -ErrorAction Stop | ForEach-Object {
            $candidate = Join-Path $_.FullName 'x64\makeappx.exe'
            $sdkVersion = [System.Version]'0.0'
            if ([System.Version]::TryParse(
                    $_.Name,
                    [ref]$sdkVersion) -and
                [System.IO.File]::Exists($candidate)) {
                [pscustomobject]@{
                    Path = $candidate
                    Version = $sdkVersion
                }
            }
        } | Sort-Object Version -Descending)
    if ($candidates.Count -eq 0) {
        throw 'The x64 MakeAppx.exe tool was not found in the Windows SDK.'
    }

    return $candidates[0].Path
}

function Copy-PayloadFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [Parameter(Mandatory = $true)]
        [string]$DestinationRoot
    )

    [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
    foreach ($sourcePath in [System.IO.Directory]::EnumerateFiles(
            $SourceRoot,
            '*',
            [System.IO.SearchOption]::AllDirectories)) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $SourceRoot,
            $sourcePath)
        $destinationPath = Join-Path $DestinationRoot $relativePath
        [System.IO.Directory]::CreateDirectory(
            [System.IO.Path]::GetDirectoryName($destinationPath)) |
            Out-Null
        [System.IO.File]::Copy(
            $sourcePath,
            $destinationPath,
            $false)
    }
}

function New-StoreLogo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(32, 33, 36))
            $margin = [Math]::Max(2, [int]($Size * 0.16))
            $diameter = $Size - (2 * $margin)
            $brush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(138, 180, 248))
            try {
                $graphics.FillEllipse(
                    $brush,
                    $margin,
                    $margin,
                    $diameter,
                    $diameter)
            }
            finally {
                $brush.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-ManifestNamespaceManager {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document
    )

    $manager = [System.Xml.XmlNamespaceManager]::new($Document.NameTable)
    $manager.AddNamespace('f', $foundationNamespace)
    $manager.AddNamespace('uap', $uapNamespace)
    $manager.AddNamespace('uap10', $uap10Namespace)
    $manager.AddNamespace('rescap', $restrictedCapabilitiesNamespace)
    Write-Output -NoEnumerate $manager
}

function Get-RequiredManifestNode {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document,

        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNamespaceManager]$NamespaceManager,

        [Parameter(Mandatory = $true)]
        [string]$XPath
    )

    $nodes = @($Document.SelectNodes($XPath, $NamespaceManager))
    if ($nodes.Count -ne 1) {
        throw "The package manifest must contain exactly one node: $XPath"
    }

    return $nodes[0]
}

function Write-PackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplatePath,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [psobject]$StoreIdentity,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion
    )

    $document = [System.Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $false
    $document.XmlResolver = $null
    $document.Load($TemplatePath)
    $namespaces = Get-ManifestNamespaceManager -Document $document
    $identity = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Identity'
    $identity.SetAttribute('Name', $StoreIdentity.PackageIdentityName)
    $identity.SetAttribute('Publisher', $StoreIdentity.Publisher)
    $identity.SetAttribute('Version', $PackageVersion)

    $publisherDisplayName = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Properties/f:PublisherDisplayName'
    $publisherDisplayName.InnerText = $StoreIdentity.PublisherDisplayName

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = $utf8WithoutBom
    $settings.Indent = $true
    $settings.NewLineChars = "`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $writer = [System.Xml.XmlWriter]::Create($OutputPath, $settings)
    try {
        $document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Assert-PackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [psobject]$StoreIdentity,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion
    )

    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.Load($Path)
    $namespaces = Get-ManifestNamespaceManager -Document $document
    $identity = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Identity'
    if ($identity.GetAttribute('Name') -cne
            $StoreIdentity.PackageIdentityName -or
        $identity.GetAttribute('Publisher') -cne
            $StoreIdentity.Publisher -or
        $identity.GetAttribute('Version') -cne $PackageVersion -or
        $identity.GetAttribute('ProcessorArchitecture') -cne 'x64') {
        throw 'The generated package Identity does not match the inputs.'
    }

    $publisherDisplayName = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Properties/f:PublisherDisplayName'
    if ($publisherDisplayName.InnerText -cne
        $StoreIdentity.PublisherDisplayName) {
        throw 'The generated PublisherDisplayName does not match the input.'
    }

    $deviceFamily = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Dependencies/f:TargetDeviceFamily'
    if ($deviceFamily.GetAttribute('Name') -cne 'Windows.Desktop' -or
        $deviceFamily.GetAttribute('MinVersion') -cne '10.0.19041.0' -or
        $deviceFamily.GetAttribute('MaxVersionTested') -cne
            '10.0.26100.0') {
        throw 'The package must target the approved Windows.Desktop range.'
    }

    $application = Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath '/f:Package/f:Applications/f:Application'
    if ($application.GetAttribute('Executable') -cne
            'app\VRRecorder.App.exe' -or
        $application.GetAttribute(
            'RuntimeBehavior',
            $uap10Namespace) -cne 'packagedClassicApp' -or
        $application.GetAttribute(
            'TrustLevel',
            $uap10Namespace) -cne 'mediumIL') {
        throw 'The package must activate the validated mediumIL desktop app.'
    }

    Get-RequiredManifestNode `
        -Document $document `
        -NamespaceManager $namespaces `
        -XPath (
            '/f:Package/f:Capabilities/' +
            'rescap:Capability[@Name="runFullTrust"]') |
        Out-Null
}

function Invoke-StoreInputValidation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTool,

        [Parameter(Mandatory = $true)]
        [string]$PayloadRoot,

        [Parameter(Mandatory = $true)]
        [string]$PayloadIdentity,

        [Parameter(Mandatory = $true)]
        [string]$HardwareReport,

        [Parameter(Mandatory = $true)]
        [string]$HardwareArtifacts,

        [Parameter(Mandatory = $true)]
        [string]$CandidateOutput,

        [Parameter(Mandatory = $true)]
        [psobject]$StoreIdentity
    )

    & dotnet $ReleaseTool `
        validate-store-packaging-input `
        --payload-root $PayloadRoot `
        --payload-identity $PayloadIdentity `
        --hardware-report $HardwareReport `
        --hardware-artifacts-root $HardwareArtifacts `
        --candidate-output $CandidateOutput `
        --store-name $StoreIdentity.PackageIdentityName `
        --store-publisher $StoreIdentity.Publisher `
        --store-publisher-display-name $StoreIdentity.PublisherDisplayName
    if ($LASTEXITCODE -ne 0) {
        throw "Store packaging input validation failed with exit code $LASTEXITCODE."
    }
}

Assert-MsixVersion -Value $Version
if ($ValidatedRunId -cnotmatch '^[1-9][0-9]*$') {
    throw 'ValidatedRunId must be a positive GitHub Actions run ID.'
}
if ($ValidatedArtifactId -cnotmatch '^[1-9][0-9]*$') {
    throw 'ValidatedArtifactId must be a positive GitHub artifact ID.'
}

$repositoryRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $PSScriptRoot '..') `
    -Name 'Repository root'
$validatedRoot = Resolve-ExistingDirectory `
    -Path $ValidatedInputRoot `
    -Name 'Validated input root'
$payloadRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $validatedRoot 'payload') `
    -Name 'Validated payload root'
$evidenceRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $validatedRoot 'evidence') `
    -Name 'Validation evidence root'
$payloadIdentityPath = Resolve-ExistingFile `
    -Path (Join-Path `
        $evidenceRoot `
        'application-payload-identity.v1.json') `
    -Name 'Payload identity'
$hardwareReportPath = Resolve-ExistingFile `
    -Path (Join-Path `
        $evidenceRoot `
        'hardware-validation-report.v1.json') `
    -Name 'Hardware validation report'
$hardwareArtifactsRoot = Resolve-ExistingDirectory `
    -Path (Join-Path $evidenceRoot 'artifacts') `
    -Name 'Hardware validation artifacts root'
$storeIdentityPath = Resolve-ExistingFile `
    -Path $IdentityFile `
    -Name 'Store identity file'
$storeIdentity = Read-StoreIdentity -Path $storeIdentityPath

$payloadIdentity = Get-Content -LiteralPath $payloadIdentityPath -Raw |
    ConvertFrom-Json
$expectedStoreVersion = "$($payloadIdentity.productVersion).0"
if ($Version -cne $expectedStoreVersion) {
    throw (
        "MSIX Version must match the validated payload product version: " +
        $expectedStoreVersion)
}

if ([string]::IsNullOrWhiteSpace($PackagingRevision)) {
    $revisionOutput = @(& git -C $repositoryRoot rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or $revisionOutput.Count -ne 1) {
        throw 'The packaging revision could not be resolved from Git.'
    }
    $PackagingRevision = $revisionOutput[0]
}
if ($PackagingRevision -cnotmatch '^[0-9a-f]{40}([0-9a-f]{24})?$') {
    throw 'PackagingRevision must be a canonical lowercase Git revision.'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([System.IO.Directory]::Exists($outputRoot) -or
    [System.IO.File]::Exists($outputRoot)) {
    throw "OutputDirectory must not already exist: $outputRoot"
}

$releaseToolProject = Join-Path `
    $repositoryRoot `
    'src\VRRecorder.ReleaseTool\VRRecorder.ReleaseTool.csproj'
& dotnet build `
    $releaseToolProject `
    --configuration Release `
    --runtime win-x64 `
    --no-self-contained `
    -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) {
    throw "Release tool build failed with exit code $LASTEXITCODE."
}
$releaseTool = Resolve-ExistingFile `
    -Path (Join-Path `
        $repositoryRoot `
        'src\VRRecorder.ReleaseTool\bin\Release\net10.0\win-x64\VRRecorder.ReleaseTool.dll') `
    -Name 'Release tool'

$temporaryParent = if (
    -not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
}
else {
    [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
}
$scratchRoot = Join-Path `
    $temporaryParent `
    "vrrecorder-store-msix-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($scratchRoot) | Out-Null

try {
    $packageFileName = "VRRecorder_$($Version)_x64.msix"
    $scratchPackagePath = Join-Path $scratchRoot $packageFileName
    Invoke-StoreInputValidation `
        -ReleaseTool $releaseTool `
        -PayloadRoot $payloadRoot `
        -PayloadIdentity $payloadIdentityPath `
        -HardwareReport $hardwareReportPath `
        -HardwareArtifacts $hardwareArtifactsRoot `
        -CandidateOutput $scratchPackagePath `
        -StoreIdentity $storeIdentity

    $layoutRoot = Join-Path $scratchRoot 'layout'
    $layoutApplicationRoot = Join-Path $layoutRoot 'app'
    [System.IO.Directory]::CreateDirectory($layoutRoot) | Out-Null
    Copy-PayloadFiles `
        -SourceRoot $payloadRoot `
        -DestinationRoot $layoutApplicationRoot
    Invoke-StoreInputValidation `
        -ReleaseTool $releaseTool `
        -PayloadRoot $layoutApplicationRoot `
        -PayloadIdentity $payloadIdentityPath `
        -HardwareReport $hardwareReportPath `
        -HardwareArtifacts $hardwareArtifactsRoot `
        -CandidateOutput $scratchPackagePath `
        -StoreIdentity $storeIdentity

    $assetsRoot = Join-Path $layoutRoot 'Assets'
    [System.IO.Directory]::CreateDirectory($assetsRoot) | Out-Null
    Add-Type -AssemblyName System.Drawing.Common
    New-StoreLogo `
        -Path (Join-Path $assetsRoot 'StoreLogo.png') `
        -Size 50
    New-StoreLogo `
        -Path (Join-Path $assetsRoot 'Square150x150Logo.png') `
        -Size 150
    New-StoreLogo `
        -Path (Join-Path $assetsRoot 'Square44x44Logo.png') `
        -Size 44

    $manifestTemplate = Resolve-ExistingFile `
        -Path (Join-Path `
            $repositoryRoot `
            'src\VRRecorder.StorePackaging\Package.appxmanifest') `
        -Name 'Package manifest template'
    $layoutManifest = Join-Path $layoutRoot 'AppxManifest.xml'
    Write-PackageManifest `
        -TemplatePath $manifestTemplate `
        -OutputPath $layoutManifest `
        -StoreIdentity $storeIdentity `
        -PackageVersion $Version
    Assert-PackageManifest `
        -Path $layoutManifest `
        -StoreIdentity $storeIdentity `
        -PackageVersion $Version

    $makeAppx = Get-MakeAppxPath
    & $makeAppx pack `
        /d $layoutRoot `
        /p $scratchPackagePath `
        /o `
        /v `
        /h SHA256
    if ($LASTEXITCODE -ne 0 -or
        -not [System.IO.File]::Exists($scratchPackagePath)) {
        throw "MakeAppx pack failed with exit code $LASTEXITCODE."
    }

    $unpackedRoot = Join-Path $scratchRoot 'unpacked'
    & $makeAppx unpack `
        /p $scratchPackagePath `
        /d $unpackedRoot `
        /o `
        /v
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx unpack failed with exit code $LASTEXITCODE."
    }
    Assert-PackageManifest `
        -Path (Join-Path $unpackedRoot 'AppxManifest.xml') `
        -StoreIdentity $storeIdentity `
        -PackageVersion $Version
    Invoke-StoreInputValidation `
        -ReleaseTool $releaseTool `
        -PayloadRoot (Join-Path $unpackedRoot 'app') `
        -PayloadIdentity $payloadIdentityPath `
        -HardwareReport $hardwareReportPath `
        -HardwareArtifacts $hardwareArtifactsRoot `
        -CandidateOutput $scratchPackagePath `
        -StoreIdentity $storeIdentity

    $packageSha256 = (Get-FileHash `
        -LiteralPath $scratchPackagePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestSha256 = (Get-FileHash `
        -LiteralPath $layoutManifest `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $payloadIdentitySha256 = (Get-FileHash `
        -LiteralPath $payloadIdentityPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $hardwareReportSha256 = (Get-FileHash `
        -LiteralPath $hardwareReportPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $packagingIdentityFileName =
        "VRRecorder_$($Version)_x64.store-packaging-identity.v1.json"
    $scratchPackagingIdentityPath = Join-Path `
        $scratchRoot `
        $packagingIdentityFileName
    $packagingIdentity = [ordered]@{
        schemaVersion = 1
        candidateKind = 'microsoft-store-packaging-candidate'
        packageFileName = $packageFileName
        packageSha256 = $packageSha256
        manifestSha256 = $manifestSha256
        packagingRevision = $PackagingRevision
        validatedArtifact = [ordered]@{
            repository = $env:GITHUB_REPOSITORY
            runId = $ValidatedRunId
            artifactId = $ValidatedArtifactId
        }
        validatedPayload = [ordered]@{
            productVersion = $payloadIdentity.productVersion
            sourceRevision = $payloadIdentity.sourceRevision
            runtimeIdentifier = $payloadIdentity.runtimeIdentifier
            applicationExecutableSha256 =
                $payloadIdentity.applicationExecutableSha256
            payloadInventorySha256 =
                $payloadIdentity.payloadInventorySha256
            legalBundleId = $payloadIdentity.legalBundleId
            legalManifestSha256 = $payloadIdentity.legalManifestSha256
            identityDocumentSha256 = $payloadIdentitySha256
        }
        hardwareValidationReportSha256 = $hardwareReportSha256
        storeIdentity = [ordered]@{
            packageIdentityName = $storeIdentity.PackageIdentityName
            publisher = $storeIdentity.Publisher
            publisherDisplayName = $storeIdentity.PublisherDisplayName
            version = $Version
            processorArchitecture = 'x64'
        }
        publishEligible = $false
    }
    [System.IO.File]::WriteAllText(
        $scratchPackagingIdentityPath,
        (($packagingIdentity | ConvertTo-Json -Depth 8) + "`n"),
        $utf8WithoutBom)
    $packagingIdentitySha256 = (Get-FileHash `
        -LiteralPath $scratchPackagingIdentityPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()

    [System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    [System.IO.File]::Copy(
        $scratchPackagePath,
        (Join-Path $outputRoot $packageFileName),
        $false)
    [System.IO.File]::Copy(
        $scratchPackagingIdentityPath,
        (Join-Path $outputRoot $packagingIdentityFileName),
        $false)
    $checksums =
        "$packageSha256  $packageFileName`n" +
        "$packagingIdentitySha256  $packagingIdentityFileName`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $outputRoot 'SHA256SUMS.txt'),
        $checksums,
        $utf8WithoutBom)

    Write-Output (Join-Path $outputRoot $packageFileName)
}
finally {
    $resolvedScratch = [System.IO.Path]::GetFullPath($scratchRoot)
    $requiredPrefix = $temporaryParent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedScratch.StartsWith(
            $requiredPrefix,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedScratch).StartsWith(
            'vrrecorder-store-msix-',
            [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
    else {
        throw "Refusing to remove unexpected scratch path: $resolvedScratch"
    }
}
