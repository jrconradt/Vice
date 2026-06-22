# Vice

[![CI](https://github.com/jrconradt/Vice/actions/workflows/ci.yml/badge.svg)](https://github.com/jrconradt/Vice/actions/workflows/ci.yml) ![.NET](https://img.shields.io/badge/.NET-10-512BD4) [![License](https://img.shields.io/badge/License-Apache_2.0-blue)](LICENSE)

**A .NET CLI framework with a natural-language command grammar, and the tools built on it.**

Vice parses natural-language command lines:

```bash
vice search "graph neural networks" on source arxiv --limit 20
vice tcp send "PING" to endpoint localhost:6379
vice search files by type csharp in ./src then write to file ./inventory.txt
```

A lexer and parser turn them into typed commands that pipe into one another through backpressured streaming channels, run as detached background processes, and share a session REPL, provided by the framework.

Vice is two things:

- **`vice` — a CLI tool** for network probes, scientific-literature search, filesystem work, and `dotnet` build orchestration. Commands use the natural-language grammar and pipe into each other.
- **Vice — the framework** underneath it. Define your own commands as a composable grammar, and inherit the parser, the pipelines, the REPL, history, plugins, and configuration without implementing them.

Apache-2.0 · targets .NET 10.

---

## Quick start

To try the `vice` tool, build and install it from this repo:

```bash
./scripts/install-local.sh    # packs Vice.Cli and installs it as a global `vice` tool
vice                          # opens the interactive REPL
```

A full walkthrough lives in **[docs/getting-started.md](docs/getting-started.md)**.

Run a single command and exit:

```bash
vice version
vice help
vice help search
```

Or open a session by running `vice` with no arguments:

```
$ vice
vice> list commands
vice> search "transformer" on source arxiv --limit 5
vice> exit
```

In a session, long-running work (downloads) runs as a detached child process — the host binary re-executed as `vice job run <descriptor>`, with the child's pid as its id. The process does its work and exits; the file at its destination is the only result, so `ls` is the status command. It ignores `SIGHUP`, so it survives REPL exit and terminal close; stop a runaway with `kill <pid>`. Killing a download mid-flight leaves its `.partial` file, and re-running the same download resumes from it.

---

## What the `vice` tool can do

Every area below has a dedicated guide under [`docs/`](docs/).

**Network** — raw TCP, UDP, and gRPC. ([docs/network-commands.md](docs/network-commands.md))

```bash
vice grpc list services on endpoint localhost:50051
vice grpc call helloworld.Greeter/SayHello on endpoint localhost:50051 with data '{"name":"World"}'
```

**Research** — search, fetch, download, and bulk-archive across five scientific sources (arXiv, Project Gutenberg, PubMed, UniProt, AlphaFold), with a per-host rate limiter. ([docs/research-commands.md](docs/research-commands.md))

```bash
vice fetch 2401.00001 from source arxiv
vice archive "CRISPR review" from source pubmed to path ./crispr-reviews/ --limit 50
```

**Files** — read (with transparent gzip/brotli/deflate inflation), write, append, stream, extract archives, and search the filesystem by name, path, type, size, or modification time. ([docs/file-commands.md](docs/file-commands.md))

```bash
vice read ./access.log.gz
vice search files by name "*.log" and by size ">10Mi" in ./logs
```

**Build** — thin, deduplicating wrappers over the local `dotnet` CLI. ([docs/build-commands.md](docs/build-commands.md))

```bash
vice build ./MyApp.sln
vice test ./tests/MyLib.Tests/
```

**Pipelines** — any producing command pipes into a consuming one through a streaming channel:

```bash
vice search "graph" on source arxiv --limit 50 then write to file ./graph-papers.txt
vice read ./large.bin.gz then stream to count
```

---

## The `vice-mux` companion tool

`vice-mux` is a separate stream-plumbing tool for inspecting, routing, and tee-ing byte streams — installed locally alongside `vice` via `./scripts/install-local.sh` and invoked as `vice-mux`. It reads stdin and fans bytes out to files, TCP endpoints, child processes, or named pipes; `route` forwards the stream to the destinations whose condition matches the upstream exit code. ([docs/mux-commands.md](docs/mux-commands.md))

```bash
cat ./events.ndjson | vice-mux tee to file:./copy.ndjson,tcp:collector:9000 > ./out.ndjson
some-stage | vice-mux route on 0 to file:./ok.log on 1,2 to exec:notify on '*' to file:./all.log --code 1
```

---

## Build your own CLI on Vice

Reference the framework directly from a checkout of this repo — add a `ProjectReference` to `src/Vice/Vice.csproj`:

```xml
<ProjectReference Include="path/to/vice/src/Vice/Vice.csproj" />
```

Define a group of commands. Each command is a grammar (verb, optional nouns, bound targets) plus a handler. Register them by hand when wiring a single app:

```csharp
using static Vice.Core.Dsl;

internal static class GreetCommands
{
    private static readonly TargetDef Name = new("name");

    internal static void Register(IViceApp app)
    {
        app.Register(
            verb("greet") * Name,
            "Greet someone by name",
            async (ctx, ct) =>
            {
                Vice.Core.Output.Line($"Hello, {ctx["name"]}!");
                return 0;
            });
    }
}
```

Build the app and run it:

```csharp
using Vice.Host;

await using var app = ViceApp.Create("mytool", "0.1.0")
    .WithDescription("An example CLI")
    .Build();

GreetCommands.Register(app);

return await app.RunAsync(args, CancellationToken.None);
```

`mytool greet world` prints `Hello, world!`; `mytool` with no arguments opens a REPL. The parser, pipelines, and REPL are all inherited — only the command grammar and handler above are yours.

The grammar composes with operators: `>` sequences words, `*` binds a target, and `Verbs`/`Nouns`/`Connectors`/`Targets` provide a shared vocabulary:

```csharp
app.Register(
    Verbs.Grpc() > Nouns.Connect() * Targets.Endpoint,
    "Connect to a gRPC endpoint",
    async (ctx, ct) => { /* ... */ });
```

Hand-registration suits a single app. To scale across many packs without hand-wiring each one, mark each pack class with `[ViceCommandPack]`, then build the app as `ComposeFromAttributes(host).Build().RegisterDiscoveredPacks(host)` — passing the host services object — and the **[Vice.Generators](src/Vice.Generators/README.md)** source generator discovers and wires every pack at compile time, with no runtime reflection and no hand calls to each pack's `Register`. That's how the `vice` tool itself is assembled.

What you inherit by building on Vice:

- Natural-language lexer and parser with synonyms and aliases
- Typed, backpressured streaming channels between piped stages
- A session REPL with history and a `daemon` mode for terminal-independent runs
- Git-style external plugins: any executable named `<app>-<verb>` on the trusted plugin path dispatches as a verb
- Pluggable, opt-in framework services (`IViceLogger`) with safe `Null` defaults
- POSIX exit codes, pager wrapping, `--json`/`--format` conventions, and an SSRF-guarded `HttpClient`

The framework API is summarized in [src/Vice/README.md](src/Vice/README.md); the knobs are in [docs/env-and-config.md](docs/env-and-config.md).

---

## Repository layout

| Path | What it is |
|---|---|
| `src/Vice.Foundation` | BCL-only primitives shared across the tree — concurrency helpers, terminal rendering, the DSL node tree, expression composition, and path-gated atomic file writes. A leaf with no inbound `Vice.*` dependency. |
| `src/Vice.Parser` | The command-line lexer and resolver. BCL-only, no dependency on the rest of the framework; `Vice` references it. |
| `src/Vice.Generators` | Roslyn source generators that wire `[ViceCommandPack]` classes into the host at compile time. Consumed in-tree as an analyzer, never shipped — `IsPackable=false`. |
| `src/Vice.Jobs` | The detached job model — spawn a child process that runs to completion and exits. No tracking, no state. References `Vice.Foundation` only. |
| `src/Vice` | The framework. The command DSL, streaming channels, configuration, plugins; references `Vice.Foundation`, `Vice.Jobs`, and `Vice.Parser`. |
| `src/Vice.Host` | The session REPL host that wires the framework into a runnable interactive application. References `Vice`, `Vice.Jobs`, and `Vice.Foundation`. |
| `src/Vice.Net` | Network command library (TCP, UDP, gRPC) for the `vice` tool. A library, not a tool — `IsPackable=false`. |
| `src/Vice.Files` | Filesystem command library — read, write, stream, archive, and search. A library, not a tool. |
| `src/Vice.Build` | The `dotnet`-wrapper build command library. A library, not a tool. |
| `src/Vice.Mux` | The mux library for inspecting, routing, and tee-ing Vice pipeline streams. A library, not a tool. |
| `src/Vice.Cli` | The `vice` reference CLI tool, `ToolCommandName` `vice`. Composes the framework, host, and feature libraries. Install from a local checkout with `./scripts/install-local.sh`. |
| `src/Vice.Mux.Cli` | The `vice-mux` companion CLI tool, `ToolCommandName` `vice-mux`. Install from a local checkout with `./scripts/install-local.sh` ([docs/mux-commands.md](docs/mux-commands.md)). |
| `docs/` | User and configuration guides. |
| `scripts/` | Build, test, and local-install helpers ([scripts/README.md](scripts/README.md)). |
| `tests/` | Unit tests for the framework, generators, parser, network layer, and mux tool. |
| `bench/Vice.Benchmarks` | BenchmarkDotNet harness for the hot paths — lexer, streaming channel throughput, pipeline stage execution, mux routing, and buffered I/O. |

---

## Configuration

Vice is configured through environment variables — log level, allowed write roots for downloads, color control, outbound-connection allow/deny lists, and plugin discovery. The complete reference is in **[docs/env-and-config.md](docs/env-and-config.md)**.

---

## Accessibility

Vice is usable without color, motion, or Unicode, and conveys every outcome textually.

- **Disable motion.** `--no-status` turns off the animated status spinner; progress is still reported in plain text. For a persistent setting, set `VICE_NO_STATUS` or `VICE_REDUCED_MOTION` to a truthy value; Vice also honors the cross-tool `ACCESSIBLE` reduced-motion convention (see [docs/env-and-config.md](docs/env-and-config.md)).
- **Disable color.** `--no-color`, or the standard `NO_COLOR` environment variable, drops all ANSI color. `FORCE_COLOR` and `CLICOLOR_FORCE` opt color back in for redirected output, and `NO_COLOR` always wins (see [docs/env-and-config.md](docs/env-and-config.md)).
- **Unicode / ASCII fallback.** When the terminal does not advertise Unicode support, table borders and decorations fall back to ASCII automatically; no flag is required.
- **No color-only signals.** Status and error semantics are always carried in the text itself — error messages, `hint:` lines, and POSIX exit codes — not by color alone, so screen readers and color-blind users lose no information.

---

## Documentation

- [Architecture](docs/architecture.md) — assembly graph, core internal layering, and extraction boundaries
- [Getting started](docs/getting-started.md) — install, verify, first commands, one-shot vs. session mode
- [Network commands](docs/network-commands.md) — TCP, UDP, gRPC
- [Research commands](docs/research-commands.md) — search, fetch, download, archive
- [File commands](docs/file-commands.md) — read, write, stream, archives, filesystem search
- [Build commands](docs/build-commands.md) — `dotnet` wrappers and build deduplication
- [vice-mux commands](docs/mux-commands.md) — the companion stream inspect/route/tee tool, its exit-code conditions and sinks
- [Environment and configuration](docs/env-and-config.md) — env vars, plugins, services, exit codes
- [Licensing](docs/licensing.md) — Apache-2.0 coverage, SPDX provenance, third-party notices
- [Troubleshooting](docs/troubleshooting.md)

---

## Building from source

```bash
./scripts/build.sh    # restore and build the solution
./scripts/test.sh     # run all tests
./scripts/demo.sh     # build, install, exercise a few commands, then uninstall
./scripts/bench.sh    # run the BenchmarkDotNet hot-path harness
```

Requires the .NET 10 SDK.

`scripts/bench.sh` runs every benchmark by default and writes BenchmarkDotNet JSON, Markdown, and log artifacts under `BenchmarkDotNet.Artifacts/` in the repository root (override the location with `VICE_BENCH_ARTIFACTS`). Pass BenchmarkDotNet arguments through, e.g. `./scripts/bench.sh --filter '*RouteStrategy*'`.

To compare throughput run-over-run automatically, pass `--gate`: the harness diffs each benchmark's mean against the committed `bench/Vice.Benchmarks/baseline.json` and exits non-zero when any benchmark slows past the tolerance (`VICE_BENCH_TOLERANCE`, default `0.05` for 5%, capped at `0.50`); a missing baseline is seeded from the current run. Pass `--update-baseline` to rewrite the baseline from the current run when an intended change shifts the numbers.

---

## Status

Active development; the framework API is still evolving. The `Vice` library package publishes to NuGet from CI on push to `main`.

## License

Apache-2.0. Copyright 2026 Infalligence Labs LLC. The root [LICENSE](LICENSE) governs every file in the tree; the SPDX identifier `Apache-2.0` is carried as package and assembly metadata rather than per-file comment headers. See [docs/licensing.md](docs/licensing.md) for the file-level provenance policy and [THIRD_PARTY_NOTICE](THIRD_PARTY_NOTICE) for redistributed dependencies.
