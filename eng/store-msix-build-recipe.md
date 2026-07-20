# Microsoft Store MSIX candidate build recipe

## Purpose

`.github/workflows/store-msix.yml` creates an unsigned x64 Microsoft Store
packaging candidate from the exact `win-x64` payload that already passed the
full hardware validation matrix. It does not publish or rebuild
`VRRecorder.App`.

The resulting MSIX remains `publishEligible: false`. Packaged-app regression,
local sideload signing, WACK, Partner Center flight/certification, and final
Legal approval are later gates.

## Repository variables

Create these GitHub repository variables using the values shown in Partner
Center under the product identity page:

- `STORE_PACKAGE_IDENTITY_NAME`
- `STORE_PUBLISHER`
- `STORE_PUBLISHER_DISPLAY_NAME`

Placeholder values are rejected. The workflow never reads a signing key or
certificate. Microsoft Store submission signing is left to the Store.

## Validated artifact contract

The workflow downloads one immutable artifact from an earlier workflow run by
both run ID and artifact ID. Its extracted layout must be:

```text
payload/
  VRRecorder.App.exe
  ...the complete sealed win-x64 publish directory...
evidence/
  application-payload-identity.v1.json
  hardware-validation-report.v1.json
  artifacts/
    ...every file referenced by the hardware report...
```

The payload identity must describe every file under `payload/`. The report
must pass `full-production-hardware-validation-v1`, bind to that identity
document hash, and describe every file under `evidence/artifacts/` with no
missing, changed, skipped, failed, or extra evidence.

An artifact that only contains a build output, a caller-supplied `passed`
flag, or an EXE without its complete runtime closure cannot pass this gate.

## Run the workflow

Open **Actions > Build Microsoft Store MSIX Candidate > Run workflow** and
provide:

- `version`: canonical `A.B.C.0`; `A.B.C` must equal the sealed payload's
  `productVersion`
- `validated_run_id`: the earlier GitHub Actions run ID
- `validated_artifact_id`: the immutable artifact ID produced by
  `actions/upload-artifact@v4` or later

Only x64 is built because the validated production payload is `win-x64`.
Adding an arm64 bundle requires a separately sealed and hardware-validated
arm64 application payload; relabeling x64 bytes is forbidden.

## Build and verification sequence

`eng/build-store-msix.ps1` performs these operations fail-closed:

1. Parse the Store identity and require an MSIX version matching the payload.
2. Validate the sealed payload directory, strict payload identity, complete
   hardware report/artifacts, Legal anchor, and Partner Center identity.
3. Copy the payload into `app/` without building the application, then verify
   the copy again.
4. Materialize the packaging-only manifest with x64,
   `Windows.Desktop`, `packagedClassicApp`, `mediumIL`, and `runFullTrust`.
5. Run the Windows SDK `MakeAppx.exe pack` with semantic validation enabled.
6. Run `MakeAppx.exe unpack`, validate the expanded manifest, and re-check the
   expanded `app/` inventory against the original sealed identity.
7. Emit the unsigned `.msix`, `SHA256SUMS.txt`, and a separate outer
   `store-packaging-identity.v1.json` recording package/manifest hashes,
   packaging revision, source run/artifact IDs, validated inner identity, and
   `publishEligible: false`.

The uploaded Actions artifact is named
`VRRecorder-Store-Candidate-A.B.C.0` and is retained for 30 days.

## Local invocation

The same artifact layout can be checked locally on Windows with the Windows
SDK installed:

```powershell
.\eng\build-store-msix.ps1 `
  -IdentityFile C:\secure-input\store-identity.json `
  -Version 1.2.3.0 `
  -ValidatedInputRoot C:\validated\artifact `
  -ValidatedRunId 123456789 `
  -ValidatedArtifactId 987654321 `
  -OutputDirectory .\artifacts\store `
  -PackagingRevision (git rev-parse HEAD)
```

The identity file has this exact shape:

```json
{
  "PackageIdentityName": "PartnerCenter.Identity.Name",
  "Publisher": "CN=Partner Center publisher value",
  "PublisherDisplayName": "Publisher display name"
}
```

## Primary references

- Microsoft, [Generating MSIX package components](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
- Microsoft, [Create an app package with MakeAppx.exe](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- Microsoft, [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- GitHub, [Download artifacts from other workflow runs](https://github.com/actions/download-artifact#download-artifacts-from-other-workflow-runs-or-repositories)
