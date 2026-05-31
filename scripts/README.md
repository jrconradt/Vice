# Vice scripts

- `build.sh` — restore and build `Vice.slnx`.
- `test.sh` — run all tests in `Vice.slnx`.
- `bench.sh` — run the `Vice.Benchmarks` BenchmarkDotNet harness over the hot paths; runs every benchmark by default, or forwards BenchmarkDotNet arguments (e.g. `--filter '*RouteStrategy*'`).
- `install-local.sh` — pack `Vice.Cli` and `Vice.Mux.Cli` into `artifacts/local-nupkg/` and install them as the global `vice` and `vice-mux` tools through the repo-root `nuget.config`, whose `<packageSourceMapping>` binds the `Vice*` IDs to that local source.
- `uninstall-local.sh` — uninstall the global `Vice.Cli` and `Vice.Mux.Cli` tools and remove the local nupkg cache.
- `release.sh` — pack `Vice.Cli` and `Vice.Mux.Cli` at the `Directory.Build.props` `<Version>` into `artifacts/release/<version>/`. Dry-run by default; `--push` publishes the `.nupkg` files to `$VICE_NUGET_FEED` (default nuget.org) using `$NUGET_API_KEY`; `--verify-tag` requires an annotated git tag `v<version>` at HEAD before packing.
- `rollback.sh <version> [vice|vice-mux|all]` — pin an installed tool to a prior published version via `dotnet tool update --global <pkg> --version <version>`. Roll back from a non-default feed by setting `$VICE_NUGET_FEED`.
- `demo.sh` — build, install, exercise a few `vice` commands, then uninstall. Pass `--with-net` to include network-dependent samples.
