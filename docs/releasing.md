# Releasing, versioning, and rollback

Vice ships two installable `dotnet tool` packages built from the same repository:

| Package | Tool command | Source project |
|---|---|---|
| `Vice.Cli` | `vice` | `src/Vice.Cli` |
| `Vice.Mux.Cli` | `vice-mux` | `src/Vice.Mux.Cli` |

The framework library `Vice` (and `Vice.Net`, `Vice.Files`, `Vice.Build`, `Vice.Mux`, `Vice.Generators`) are not separately shipped as tools; the libraries are bundled inside the tool packages. `Vice` is published as a framework NuGet package for downstream consumers.

## Versioning policy

The project follows [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html): `MAJOR.MINOR.PATCH`.

- **MAJOR** — incompatible changes to the framework API (`src/Vice` public surface) or to a tool's command grammar that breaks an existing invocation.
- **MINOR** — new commands, new framework API, or new opt-in behavior, all backward compatible.
- **PATCH** — backward-compatible bug fixes and documentation.

The single source of truth for the shipped version is `<Version>` in `Directory.Build.props`; every packable project inherits it, so the two tool packages always release in lockstep at the same version. Bump that one element before cutting a release, and record the change under a new heading in [CHANGELOG.md](../CHANGELOG.md), which is kept in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

Each release corresponds to an annotated git tag `v<version>` (for example `v0.1.0`) on the commit that produced the artifacts. The tag is what ties a published `.nupkg` back to a reproducible source state.

## Cutting a release

1. Update `<Version>` in `Directory.Build.props`.
2. Add a dated section to [CHANGELOG.md](../CHANGELOG.md) describing Added / Changed / Fixed.
3. Commit, then tag the commit: `git tag -a v<version> -m "v<version>"`.
4. Produce the packages: `scripts/release.sh --verify-tag`.

   This reads `<Version>` from `Directory.Build.props`, confirms the matching `v<version>` tag is at `HEAD`, and packs both tool projects into `artifacts/release/<version>/`. Without `--verify-tag` it packs without the tag check (useful for a release dry run).
5. Publish: `NUGET_API_KEY=<key> scripts/release.sh --verify-tag --push`.

   `--push` uploads every `.nupkg` in `artifacts/release/<version>/` to the feed in `$VICE_NUGET_FEED` (default `https://api.nuget.org/v3/index.json`) with `--skip-duplicate`, so re-running an already-published version is a no-op rather than an error.

Builds are deterministic (`Deterministic` and `ContinuousIntegrationBuild` are set in `Directory.Build.props`) and embed untracked sources, so a package built from a given tag is reproducible.

## Rollback

Because both tools install through `dotnet tool`, rolling back is pinning to a prior published version. Every prior version stays available on the feed, so a downgrade never requires republishing.

```bash
scripts/rollback.sh <previous-version>            # rolls back vice
scripts/rollback.sh <previous-version> vice-mux   # rolls back vice-mux
scripts/rollback.sh <previous-version> all        # rolls back both
```

The script wraps `dotnet tool update --global <package> --version <previous-version>`. Set `$VICE_NUGET_FEED` to roll back from a non-default feed. Verify the active version afterward with `vice --version`.

To roll back manually without the script:

```bash
dotnet tool update --global Vice.Cli --version <previous-version>
dotnet tool update --global Vice.Mux.Cli --version <previous-version>
```

## Package sources and source mapping

The repo-root `nuget.config` pins the package feeds and source mapping repo-locally rather than inheriting whatever a developer's user-global config declares. It `<clear />`s inherited sources and declares exactly two:

| Source key | Location | Serves |
|---|---|---|
| `nuget.org` | `https://api.nuget.org/v3/index.json` | third-party dependencies |
| `vice-local` | `artifacts/local-nupkg/` (relative to the repo root) | the locally packed first-party `Vice*` tool packages |

The `<packageSourceMapping>` block binds each package ID prefix to exactly one source: every third-party prefix the repo consumes (`Microsoft.*`, `System.*`, `Grpc.*`, `Google.*`, `xunit`, `xunit.*`, `coverlet.*`, `JunitXml.*`) resolves only from `nuget.org`, and the first-party IDs (`Vice`, `Vice.*`) resolve only from `vice-local`. A first-party ID can therefore never be substituted by a same-named package from a public or attacker-controlled feed, and adding a stray source to a developer's global config does not affect this repo's restore — the mapping is the trust boundary, and it travels with the source.

The solution builds entirely from `ProjectReference`s, so a normal restore never queries `vice-local`; it is only used when `scripts/install-local.sh` installs the freshly packed tools.

## Local install (no feed)

To try the current working tree as installed tools without publishing, use `scripts/install-local.sh`, which packs both tools into `artifacts/local-nupkg/` and installs them globally through the repo-root `nuget.config` so the `Vice.Cli` and `Vice.Mux.Cli` IDs resolve only from the mapped `vice-local` source; `scripts/uninstall-local.sh` removes them and clears the local cache. See [scripts/README.md](../scripts/README.md).
