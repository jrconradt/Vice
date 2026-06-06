# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## What Vice is

Vice is a .NET 10 CLI framework with a natural-language command grammar,
and the `vice` / `vice-mux` tools built on it. A lexer and parser turn
command lines like `vice search "graph neural networks" on source arxiv
--limit 20` into typed commands that pipe into one another through
backpressured streaming channels, run as background jobs, and share a
session REPL.

This repo is Vice only. It is not part of Syzygy/ExSpectra — the
`~/ecosystem/CLAUDE.md` one directory up describes a different project
and none of its rules apply here.

## Build

```bash
./scripts/build.sh    # dotnet build Vice.slnx + dotnet format --verify-no-changes
./scripts/test.sh     # dotnet test Vice.slnx, every test project
```

These local scripts are the developer-loop gates. CI lives in
`.github/workflows/ci.yml`, which builds and packs `Vice.slnx` and
publishes the library packages to NuGet on push to `main`.

When you change help, manpage, completions, or generator output, the
golden baselines under each test project's `Goldens/` directory must be
regenerated:

```bash
UPDATE_GOLDENS=1 ./scripts/test.sh    # rewrite golden baselines
```

Inspect the resulting diff before committing — the regenerated goldens
are the new expected output.

## Project map

Dependency layers, bottom-up — each layer references only layers below
it; the graph is a DAG with no cycles:

- **Leaves:** `Vice.Foundation` (logging, concurrency, persistence,
  exit codes), `Vice.Parser` (lexer/parser), `Vice.Generators` (Roslyn
  source generators)
- **`Vice.Jobs`** — background job model, manager, worker pool
- **`Vice`** — the framework root every consumer references: contracts,
  composition, command registry, options, display, help, manpages,
  completions, streaming, pipeline execution
- **Feature libraries:** `Vice.Host` (IPC, daemon, session REPL),
  `Vice.Net` (gRPC, HTTP, TCP/UDP transport), `Vice.Research`
  (research source catalog and commands over `Vice.Net`), `Vice.Files`,
  `Vice.Mux`, `Vice.Build`
- **Tools:** `Vice.Cli` (`vice`), `Vice.Mux.Cli` (`vice-mux`) — the two
  executable application projects; not published to NuGet

Tests live in `tests/`, one project per subject. Benchmarks in `bench/`.
Docs live in `docs/`; keep them in sync with behavior.

## The programming model

The primary model is `[ViceCommandPack]` static classes with a
`public static void Register(IViceApp app)` body that calls
`app.Register*(...)` with DSL chains — for example
`app.RegisterStreaming<byte[]>(Verbs.Read() * Targets.Path, "...",
ReadAsStreamAsync)`. Most command sources use this form. The secondary
single-class form is a class carrying a `[ViceCommand]` attribute and
implementing `IViceCommand` (`Task<int> Handle(CommandContext,
CancellationToken)`). The Roslyn generators in `Vice.Generators`
discover both and emit the composition and registration wiring at
compile time. `ViceApp.Create(...).ComposeFromAttributes(host).Build()`
is the composition root — see `src/Vice.Cli/Program.cs` and
`src/Vice.Mux.Cli/Program.cs` for the two real instances.

Dependencies flow through constructors and `CommandContext`; there are
no ambient static service locators. Logging goes through the injected
`IViceLogger`.

## Hard rules for this repo

- **No NuGet lock files.** Never set `<RestorePackagesWithLockFile>`,
  never commit a `packages.lock.json`. Central package management
  (`Directory.Packages.props`) pins versions. If lock files appear,
  remove the property and delete the files.
- **CI publishes the libraries.** `.github/workflows/ci.yml` builds and
  packs `Vice.slnx` and pushes the library packages (`Vice`,
  `Vice.Parser`) to NuGet on push to `main` via the `nuget` environment's
  `NUGET_API_KEY` secret. The CLI projects are not packed.
- **Don't hand-edit generator output.** Generated composition wiring
  comes from `Vice.Generators`; change the generator, rebuild.
- **Local packing is scripted.** `scripts/release.sh` and
  `scripts/install-local.sh` define local packing and install; keep them
  authoritative rather than packing ad hoc.
