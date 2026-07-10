# ADR 0002: Authenticated Legal Catalog content contract v3

- Status: Accepted
- Date: 2026-07-10

## Context

ADR 0001 removed the recursive manifest digest from schema v2, but schema v2 exposes only one `licenseText` path per component. It cannot represent component-specific notices, copyright files, attributions, or the offline Material Symbols asset manifest through one authenticated reader contract.

Adding fields to schema v2 in place would make the same schema identity mean two different documents. It would also leave UI-provided paths without an exact authenticated component-and-kind binding.

## Decision

Production bundles use `THIRD-PARTY-COMPONENTS.json` schema v3. Schema v3 preserves the cycle-free `integrityManifest` reference from schema v2 and adds required component-specific `copyrightNotice` and `legalDocuments` fields.

Each legal-document reference has exactly `kind` and `path`. The canonical kinds are `license`, `notice`, `copyright`, `attribution`, and `asset-manifest`. A component has exactly one license reference. Multiple notices, copyright documents, or attributions are allowed when their paths differ. Duplicate references and path collisions are rejected. The canonical asset-manifest path is `MATERIAL-SYMBOLS-MANIFEST.json`.

Every referenced path must be package-safe and listed by the authenticated `LEGAL-MANIFEST.sha256`. The manifest digest is never copied into the catalog. Runtime constructs `ManifestSha256` only from the verifier's authenticated out-of-band `LegalBundleIdentity`.

## Runtime trust boundary

The generic legal-document reader authenticates and parses a fresh catalog for every read. It accepts a document only when the requested component ID, kind, and path exactly match a reference in that fresh catalog. A forged UI path, a reference copied from another component, a kind mismatch, a symlink, invalid UTF-8, or any payload drift must fail closed and return no stale text.

## Compatibility

Schema v3 is a new contract, not an implicit extension of schema v2. Production v3 generation, verification, and reading reject schema v1, schema v2, and unknown versions. The v1 and v2 schema and example files remain unchanged as explicit legacy design references; no production fallback or silent migration is provided.

## Consequences

Desktop and wrist projections can share one authenticated generic document API in a later slice. Material Symbols registry semantics and release approval remain separate work; this ADR only reserves the authenticated `asset-manifest` reference contract.
