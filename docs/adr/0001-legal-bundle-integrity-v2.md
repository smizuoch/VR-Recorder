# ADR 0001: Legal Bundle integrity contract v2

- Status: Accepted
- Date: 2026-07-10

## Context

The schema-v1 component catalog requires `manifestSha256`, while the integrity manifest must hash the exact component-catalog bytes. This creates the recursive equation:

    d = SHA256(LEGAL-MANIFEST(... SHA256(THIRD-PARTY-COMPONENTS(d)) ...))

It is a cryptographic fixed point problem. A solution is not guaranteed, searching for one is impractical, and leaving a placeholder would make a false integrity claim.

## Decision

Release bundles use `THIRD-PARTY-COMPONENTS.json` schema v2. Version 2 removes `manifestSha256` and replaces it with an `integrityManifest` reference containing only the path `LEGAL-MANIFEST.sha256` and algorithm `SHA-256`.

The catalog `bundleId` is exactly the SPDX `documentNamespace`. `LEGAL-MANIFEST.sha256` hashes every other exact payload, including the component catalog, and excludes itself. The authoritative digest of the exact manifest bytes is stored out-of-band in authenticated release metadata or a signed application resource.

## Trust boundary and verification order

1. Authenticate the expected out-of-band manifest digest.
2. Hash the exact `LEGAL-MANIFEST.sha256` bytes and compare that digest.
3. Verify every listed payload path and SHA-256 value; reject missing, duplicate, malformed, traversal, and unexpected files.
4. Parse the schema-v2 component catalog and require `bundleId` to equal the SPDX `documentNamespace`.
5. Compare the pair `{bundleId, manifest digest}` across embedded, installed, cache, and recording-output copies.

A checksum stored only beside the payload detects accidental drift but does not prevent coordinated replacement. Authentication of the out-of-band digest is therefore part of the release/runtime trust boundary.

## Compatibility

The schema-v1 file remains unchanged as a design-time legacy contract. Release generation emits v2 only. Runtime and release gates reject schema v1 and unknown schema versions for production bundles; there is no silent reinterpretation or migration of the v1 digest field.

## Consequences

The component catalog can be covered by the same deterministic manifest as every other payload without recursion. The signed package or authenticated release channel must carry one additional manifest digest. Legal UI readers use the catalog reference to locate the manifest but never treat it as proof by itself.

## Rejected alternatives

- Omitting the component catalog from manifest coverage leaves a required runtime input unprotected.
- Zeroing or blanking the v1 digest field hashes bytes different from the distributed catalog.
- Hashing only part of the manifest creates ambiguous integrity semantics.
- Iterative fixed-point search has no practical completion or security guarantee.
- Silently redefining schema v1 breaks existing schema identity and makes old documents ambiguous.
