# Microsoft Store MSIX candidate build recipe

## Purpose

`.github/workflows/store-msix.yml` creates an unsigned x64 Microsoft Store
packaging candidate from the exact `win-x64` payload that already passed the
full hardware validation matrix. It does not publish or rebuild
`VRRecorder.App`.

The resulting MSIX remains `publishEligible: false`. The repository now also
contains fail-closed workflows for packaged hardware evidence, ephemeral local
signing and sideload UI Automation, WACK, final Defender/Legal/SBOM scan, and
Partner Center certification/flight evidence. Those workflows validate the
candidate; they never store a private signing key or silently publish it.

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

## Submission preflight after packaged hardware validation

Run the eight packaged hardware cases against the exact candidate and place
one non-empty evidence JSON per case in an `artifacts/` directory:

- `spout2-wasapi-recording`
- `nvenc-recording`
- `amf-recording`
- `qsv-recording`
- `software-fallback-recording`
- `vrchat-recording`
- `openvr-overlay-controller`
- `wrist-haptics-move-pin-telemetry`

`eng/new-store-packaged-hardware-report.ps1` hashes those files and the exact
MSIX into `store-packaged-hardware-validation.v1.json`. Upload the report and
`artifacts/` together without changing either, then run **Validate Microsoft
Store Release Candidate** with the candidate and hardware artifact run/ID
pairs.

The workflow is deliberately assigned to a self-hosted interactive Windows
x64 runner carrying the `store-release` label. It creates a per-run local test
certificate whose Subject exactly equals manifest Publisher, signs only a
scratch copy, installs and launches from a different working directory, runs
packaged UI Automation, uninstalls, runs WACK, and scans the original unsigned
candidate plus its expanded payload. Its artifact contains only JSON/XML
evidence; the PFX, scratch-signed MSIX, and temporary certificates are removed.
The runner must be interactive, elevated for WACK, isolated from untrusted
jobs, and configured without a persistent package-signing private key.

If WACK genuinely cannot support the package, replace `wack-report.xml` with a
strict waiver created by `eng/new-wack-waiver-evidence.ps1`. A waiver is
accepted only after an independent reviewer and a passed Partner Center flight
for the same package; it is not a skip switch. The public workflow accepts
exactly one `wack-*` evidence file, so a report and waiver cannot coexist.

## Partner Center and public release

Microsoft Store performs production signing. After the exact candidate passes
certification and a private flight, export the certification and flight
reports, create `partner-center-public-release.v1.json` with
`eng/new-partner-center-release-evidence.ps1`, and upload all three as one
immutable artifact. Run **Validate Microsoft Store Public Release** with the
candidate, preflight, packaged-hardware, and Partner Center run/artifact IDs.

That last workflow is the only repository gate that reports
`publishEligible=true`. Certification and flight are external Store operations
and cannot be truthfully completed by source code alone. Configure its
`store-production` GitHub Environment with required independent reviewers;
without that repository setting, the source-level reviewer separation is not
an operational approval boundary.

## Legal approval and frozen unpackaged payload

After an independent reviewer changes every runtime registry entry to
`approved` with a real ticket, distinct requester, and reviewer, generate the
Legal Bundle through `VRRecorder.ReleaseTool generate-legal-bundle`. Pending,
self-approved, incomplete, or candidate-only native entries fail closed.

Use `eng/prepare-windows-runtime-input.ps1` to assemble the exact production
native DLL, factory evidence, FFmpeg/libvpl, ffprobe oracle, OpenVR assets, and
app-local VC runtime into a staging manifest. Pass its `source/` directory and
manifest to `eng/publish-windows-hardware-validation.ps1`; that script requires
a clean revision, publishes self-contained `win-x64`, and emits the sealed
application payload identity consumed by the initial hardware run.
RID-neutral tools/tests use `packages.lock.json`; the `win-x64` application
publish explicitly selects `packages.win-x64.lock.json` for every project in
its restore graph. Both paths remain locked without making Linux coverage use
a Windows RID graph.

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
