# Licensing and file-level provenance

## The root LICENSE governs every file

Every source file in this repository — all `.cs` under `src/`, `tests/`, and `bench/`, every `.csproj`, every script, and every document — is licensed under the **Apache License, Version 2.0**. The authoritative text is the root [LICENSE](../LICENSE) file. There is no exception, dual-license, or per-directory override anywhere in the tree.

Copyright 2026 Infalligence Labs LLC.

No source file carries an inline license or `SPDX-License-Identifier` comment header. That is deliberate, not an omission: license coverage is established once, for the whole tree, by the root LICENSE file and by the package- and assembly-level metadata described below. A reader establishing the license of any individual file should consult this document and the root LICENSE rather than expecting a per-file banner.

## Where the SPDX identifier lives

The SPDX license expression is `Apache-2.0`. Rather than repeating it as a comment in thousands of files, it is carried as machine-readable metadata that ships with every build:

- **Package metadata.** `Directory.Build.props` sets `PackageLicenseExpression` to `Apache-2.0`, so every produced NuGet package (`Vice.Cli`, `Vice.Mux.Cli`) declares its SPDX license expression in its `.nuspec`. Package consumers and SBOM tooling read it directly.
- **Assembly metadata.** `Directory.Build.props` also emits an `AssemblyMetadata` item with key `SPDX-License-Identifier` and value `Apache-2.0`, baked into every compiled assembly in the solution. The value is queryable at runtime via `Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()` and is visible to license scanners that inspect compiled output.
- **Copyright.** `Directory.Build.props` sets `Copyright` to `Copyright 2026 Infalligence Labs LLC · Apache-2.0`, surfaced in the assembly's `AssemblyCopyrightAttribute`.

The result is the same provenance an `SPDX-License-Identifier` comment would provide — a single, machine-readable license identifier attached to every shipped artifact — without an inline comment in any source file.

## Third-party components

Redistributed third-party software and its licenses are enumerated in [THIRD_PARTY_NOTICE](../THIRD_PARTY_NOTICE). That file is the NOTICE required by Apache-2.0 §4(d) for the bundled dependencies and is packed alongside the tools.

## Adding new files

New source files inherit Apache-2.0 from this policy automatically; nothing needs to be added to the file itself. Do not introduce per-file license comment headers — provenance is established by the root LICENSE, this document, and the assembly/package metadata above.
