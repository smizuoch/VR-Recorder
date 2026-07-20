[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [Parameter(Mandatory = $true)]
    [string]$PackagingIdentity,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackagedHardwareReport,

    [string]$PackagedHardwareArtifactRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

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

function Get-LatestWindowsSdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $kitsRoot 'bin') `
        -Directory -ErrorAction Stop | ForEach-Object {
            $version = [System.Version]'0.0'
            $path = Join-Path $_.FullName $RelativePath
            if ([System.Version]::TryParse($_.Name, [ref]$version) -and
                [System.IO.File]::Exists($path)) {
                [pscustomobject]@{
                    Path = $path
                    Version = $version
                }
            }
        } | Sort-Object Version -Descending)
    if ($candidates.Count -eq 0) {
        throw "$Name was not found in the installed Windows SDK."
    }

    return $candidates[0]
}

function Wait-ForAutomationElement {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.Condition]$Condition,

        [int]$Seconds = 20
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $Condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 100
    }
    while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Get-AutomationCondition {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.ControlType]$ControlType,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$ProcessId = 0
    )

    $conditions = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType))
    $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name))
    if ($ProcessId -gt 0) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $ProcessId))
    }
    return [System.Windows.Automation.AndCondition]::new($conditions.ToArray())
}

function Wait-ForWindow {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$Seconds = 20
    )

    return Wait-ForAutomationElement `
        -Root ([System.Windows.Automation.AutomationElement]::RootElement) `
        -Condition (Get-AutomationCondition `
            -ControlType ([System.Windows.Automation.ControlType]::Window) `
            -Name $Name `
            -ProcessId $ProcessId) `
        -Seconds $Seconds
}

function Invoke-NamedButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $button = Wait-ForAutomationElement `
        -Root $Root `
        -Condition (Get-AutomationCondition `
            -ControlType ([System.Windows.Automation.ControlType]::Button) `
            -Name $Name)
    $pattern = $null
    if ($null -eq $button -or
        -not $button.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref]$pattern)) {
        throw "UI Automation could not invoke button: $Name"
    }
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Complete-FirstRunDialogs {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId
    )

    $rights = Wait-ForWindow `
        -ProcessId $ProcessId `
        -Name 'Before you record' `
        -Seconds 3
    if ($null -ne $rights) {
        $checkBox = Wait-ForAutomationElement `
            -Root $rights `
            -Condition (Get-AutomationCondition `
                -ControlType ([System.Windows.Automation.ControlType]::CheckBox) `
                -Name 'Acknowledge responsibility for recording rights and participant consent')
        $togglePattern = $null
        if ($null -eq $checkBox -or
            -not $checkBox.TryGetCurrentPattern(
                [System.Windows.Automation.TogglePattern]::Pattern,
                [ref]$togglePattern)) {
            throw 'UI Automation could not acknowledge the recording rights notice.'
        }
        ([System.Windows.Automation.TogglePattern]$togglePattern).Toggle()
        Invoke-NamedButton `
            -Root $rights `
            -Name 'Accept the recording rights notice'
    }

    $setup = Wait-ForWindow `
        -ProcessId $ProcessId `
        -Name 'VR-Recorder first-run setup' `
        -Seconds 5
    if ($null -ne $setup) {
        Invoke-NamedButton -Root $setup -Name 'Close first-run setup'
    }
}

function Test-PackagedUi {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    Complete-FirstRunDialogs -ProcessId $Process.Id
    $main = Wait-ForWindow -ProcessId $Process.Id -Name 'VR-Recorder'
    if ($null -eq $main) {
        throw 'The packaged application main window did not appear.'
    }

    Invoke-NamedButton -Root $main -Name 'Open recording settings'
    $settings = Wait-ForWindow `
        -ProcessId $Process.Id `
        -Name 'Recording settings'
    if ($null -eq $settings) {
        throw 'The packaged settings window did not appear.'
    }
    Invoke-NamedButton -Root $settings -Name 'Save recording settings'

    Invoke-NamedButton -Root $main -Name 'Open diagnostics'
    $diagnostics = Wait-ForWindow `
        -ProcessId $Process.Id `
        -Name 'VR-Recorder diagnostic bundle export'
    if ($null -eq $diagnostics) {
        throw 'The packaged diagnostics window did not appear.'
    }
    Invoke-NamedButton -Root $diagnostics -Name 'Close diagnostics'

    Invoke-NamedButton -Root $main -Name 'About and licenses'
    $legal = Wait-ForWindow `
        -ProcessId $Process.Id `
        -Name 'About and third-party licenses'
    if ($null -eq $legal) {
        throw 'The packaged Legal window did not appear.'
    }
    $legalList = Wait-ForAutomationElement `
        -Root $legal `
        -Condition (Get-AutomationCondition `
            -ControlType ([System.Windows.Automation.ControlType]::List) `
            -Name 'Third-party component list')
    if ($null -eq $legalList -or -not $legalList.Current.IsEnabled) {
        throw 'The packaged Legal catalog was not available.'
    }
    Invoke-NamedButton -Root $legal -Name 'Close About and licenses'
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$packagePath = Resolve-ExistingFile -Path $Package -Name 'Store MSIX package'
$packagingIdentityPath = Resolve-ExistingFile `
    -Path $PackagingIdentity `
    -Name 'Store packaging identity'
$identity = Get-Content -LiteralPath $packagingIdentityPath -Raw |
    ConvertFrom-Json
$publisher = $identity.storeIdentity.publisher
if ($identity.schemaVersion -ne 1 -or
    $identity.candidateKind -cne 'microsoft-store-packaging-candidate' -or
    $identity.publishEligible -ne $false -or
    $identity.packageFileName -cne [System.IO.Path]::GetFileName($packagePath) -or
    $publisher -isnot [string] -or
    [string]::IsNullOrWhiteSpace($publisher)) {
    throw 'The Store packaging identity is invalid.'
}
$packageSha256 = (Get-FileHash `
    -LiteralPath $packagePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageSha256 -cne $identity.packageSha256) {
    throw 'The Store package does not match its packaging identity.'
}
$hasPackagedHardwareReport =
    -not [string]::IsNullOrWhiteSpace($PackagedHardwareReport)
$hasPackagedHardwareArtifacts =
    -not [string]::IsNullOrWhiteSpace($PackagedHardwareArtifactRoot)
if ($hasPackagedHardwareReport -ne $hasPackagedHardwareArtifacts) {
    throw (
        'PackagedHardwareReport and PackagedHardwareArtifactRoot must be ' +
        'provided together.')
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([System.IO.Directory]::Exists($outputRoot) -or
    [System.IO.File]::Exists($outputRoot)) {
    throw "OutputDirectory must not already exist: $outputRoot"
}
$outputParent = [System.IO.Path]::GetDirectoryName($outputRoot)
[System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
$scratchRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "vrrecorder-store-preflight-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($scratchRoot) | Out-Null

$certificate = $null
$trustedCertificate = $null
$installedPackage = $null
$appProcess = $null
try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $signTool = Get-LatestWindowsSdkTool `
        -RelativePath 'x64\signtool.exe' `
        -Name 'SignTool'
    $appCertPath = Join-Path `
        ${env:ProgramFiles(x86)} `
        'Windows Kits\10\App Certification Kit\appcert.exe'
    $appCert = Resolve-ExistingFile `
        -Path $appCertPath `
        -Name 'Windows App Certification Kit'

    $signedPackagePath = Join-Path `
        $scratchRoot `
        ([System.IO.Path]::GetFileName($packagePath))
    [System.IO.File]::Copy($packagePath, $signedPackagePath, $false)
    $passwordText = [Convert]::ToBase64String(
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $password = ConvertTo-SecureString $passwordText -AsPlainText -Force
    $certificate = New-SelfSignedCertificate `
        -Subject $publisher `
        -Type Custom `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension '2.5.29.37={text}1.3.6.1.5.5.7.3.3'
    if ($certificate.Subject -cne $publisher) {
        throw (
            'The generated certificate Subject does not exactly match ' +
            "manifest Publisher: $($certificate.Subject)")
    }

    $pfxPath = Join-Path $scratchRoot 'sideload-test.pfx'
    $cerPath = Join-Path $scratchRoot 'sideload-test.cer'
    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $pfxPath `
        -Password $password | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
    $trustedCertificate = Import-Certificate `
        -FilePath $cerPath `
        -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople'

    & $signTool.Path sign `
        /fd SHA256 `
        /a `
        /f $pfxPath `
        /p $passwordText `
        $signedPackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool sign failed with exit code $LASTEXITCODE."
    }
    & $signTool.Path verify /pa /v $signedPackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool verify failed with exit code $LASTEXITCODE."
    }

    Add-AppxPackage -Path $signedPackagePath -ForceApplicationShutdown
    $installedCandidates = @(Get-AppxPackage `
        -Name $identity.storeIdentity.packageIdentityName |
        Where-Object { $_.Architecture -eq 'X64' })
    if ($installedCandidates.Count -ne 1) {
        throw 'Exactly one signed Store package must be installed.'
    }
    $installedPackage = $installedCandidates[0]

    $writeProbe = Join-Path `
        $installedPackage.InstallLocation `
        "vrrecorder-write-probe-$([Guid]::NewGuid().ToString('N')).tmp"
    $installRootReadOnly = $false
    try {
        $probe = [System.IO.File]::Open(
            $writeProbe,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $probe.Dispose()
        [System.IO.File]::Delete($writeProbe)
    }
    catch [System.UnauthorizedAccessException] {
        $installRootReadOnly = $true
    }
    if (-not $installRootReadOnly) {
        throw 'The package install root was writable by the test user.'
    }

    $workingDirectory = Join-Path $scratchRoot 'working-directory'
    [System.IO.Directory]::CreateDirectory($workingDirectory) | Out-Null
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new(
        (Join-Path $installedPackage.InstallLocation 'app\VRRecorder.App.exe'))
    $startInfo.WorkingDirectory = $workingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.ArgumentList.Add('--ui-locale=en-US')
    $appProcess = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $appProcess -or $appProcess.WaitForExit(1000)) {
        throw 'The packaged application did not stay running.'
    }
    Test-PackagedUi -Process $appProcess

    Stop-Process -Id $appProcess.Id -Force
    $appProcess.WaitForExit(10000) | Out-Null
    $appProcess.Dispose()
    $appProcess = $null
    Remove-AppxPackage `
        -Package $installedPackage.PackageFullName `
        -Confirm:$false
    if (Get-AppxPackage -PackageTypeFilter Main |
        Where-Object {
            $_.PackageFullName -ceq $installedPackage.PackageFullName
        }) {
        throw 'The signed Store package remained installed.'
    }
    $installedPackage = $null

    $stagingOutput = Join-Path $scratchRoot 'evidence'
    [System.IO.Directory]::CreateDirectory($stagingOutput) | Out-Null
    $wackReport = Join-Path $stagingOutput 'wack-report.xml'
    & $appCert reset
    if ($LASTEXITCODE -ne 0) {
        throw "WACK reset failed with exit code $LASTEXITCODE."
    }
    & $appCert test `
        -appxpackagepath $signedPackagePath `
        -reportoutputpath $wackReport
    if ($LASTEXITCODE -ne 0 -or
        -not [System.IO.File]::Exists($wackReport)) {
        throw "WACK test failed with exit code $LASTEXITCODE."
    }

    $capturedAtUtc = [DateTime]::UtcNow.ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $evidence = [ordered]@{
        schemaVersion = 1
        evidenceKind = 'store-sideload-lifecycle-v1'
        packageSha256 = $packageSha256
        manifestPublisher = $publisher
        certificateSubject = $certificate.Subject
        certificateThumbprint = $certificate.Thumbprint.ToLowerInvariant()
        signToolVersion = $signTool.Version.ToString()
        signatureVerified = $true
        installSucceeded = $true
        launchSucceeded = $true
        uninstallSucceeded = $true
        installRootReadOnly = $true
        workingDirectoryIndependent = $true
        settingsPassed = $true
        diagnosticsPassed = $true
        legalDisplayPassed = $true
        capturedAtUtc = $capturedAtUtc
    }
    $sideloadEvidence = Join-Path `
        $stagingOutput `
        'store-sideload-lifecycle.v1.json'
    [System.IO.File]::WriteAllText(
        $sideloadEvidence,
        (($evidence | ConvertTo-Json -Depth 4) + "`n"),
        $utf8WithoutBom)

    $finalScanEvidence = Join-Path `
        $stagingOutput `
        'store-final-scan.v1.json'
    & (Join-Path $PSScriptRoot 'scan-store-msix.ps1') `
        -Package $packagePath `
        -PackagingIdentity $packagingIdentityPath `
        -OutputFile $finalScanEvidence
    if ($LASTEXITCODE -ne 0 -or
        -not [System.IO.File]::Exists($finalScanEvidence)) {
        throw 'The final Store payload scan did not produce evidence.'
    }

    if ($hasPackagedHardwareReport) {
        $hardwareReportPath = Resolve-ExistingFile `
            -Path $PackagedHardwareReport `
            -Name 'Packaged hardware report'
        $hardwareArtifactRoot = [System.IO.Path]::GetFullPath(
            $PackagedHardwareArtifactRoot)
        if (-not [System.IO.Directory]::Exists($hardwareArtifactRoot)) {
            throw (
                'PackagedHardwareArtifactRoot must be an existing ' +
                "directory: $hardwareArtifactRoot")
        }
        $releaseToolProject = Join-Path `
            $repositoryRoot `
            'src\VRRecorder.ReleaseTool\VRRecorder.ReleaseTool.csproj'
        & dotnet run `
            --project $releaseToolProject `
            --configuration Release `
            --no-launch-profile `
            -p:RestoreLockedMode=true `
            -- `
            validate-store-submission-preflight `
            --package $packagePath `
            --packaging-identity $packagingIdentityPath `
            --sideload-evidence $sideloadEvidence `
            --wack-evidence $wackReport `
            --final-scan-evidence $finalScanEvidence `
            --packaged-hardware-report $hardwareReportPath `
            --packaged-hardware-artifacts-root $hardwareArtifactRoot
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Store submission preflight evidence validation failed ' +
                "with exit code $LASTEXITCODE.")
        }
    }

    [System.IO.Directory]::Move($stagingOutput, $outputRoot)
    Write-Output $outputRoot
}
finally {
    if ($null -ne $appProcess) {
        Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue
        $appProcess.Dispose()
    }
    if ($null -ne $installedPackage) {
        Remove-AppxPackage `
            -Package $installedPackage.PackageFullName `
            -Confirm:$false `
            -ErrorAction SilentlyContinue
    }
    if ($null -ne $trustedCertificate) {
        Remove-Item `
            -LiteralPath "Cert:\CurrentUser\TrustedPeople\$($trustedCertificate.Thumbprint)" `
            -Force `
            -ErrorAction SilentlyContinue
    }
    if ($null -ne $certificate) {
        Remove-Item `
            -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" `
            -Force `
            -ErrorAction SilentlyContinue
    }
    if ([System.IO.Directory]::Exists($scratchRoot)) {
        $resolvedScratch = [System.IO.Path]::GetFullPath($scratchRoot)
        $temporaryPrefix = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::GetTempPath()).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        if ($resolvedScratch.StartsWith(
                $temporaryPrefix,
                [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedScratch).StartsWith(
                'vrrecorder-store-preflight-',
                [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
        }
        else {
            throw "Refusing to remove unexpected scratch path: $resolvedScratch"
        }
    }
}
