# Vice

**A .NET CLI framework with a natural-language command grammar, and the tools built on it.**

Vice parses natural-language command lines:

```bash
vice search "graph neural networks" on source arxiv --limit 20
vice tcp send "PING" to endpoint localhost:6379
vice search files by type csharp in ./src then write to file ./inventory.txt
```

A lexer and parser turn them into typed commands that pipe into one another through backpressured streaming channels, run as background jobs, and share a session REPL, provided by the framework.

Vice is two things:

- **`vice` — a CLI tool** for network probes, scientific-literature search, filesystem work, and `dotnet` build orchestration. Commands use the natural-language grammar and pipe into each other.
- **Vice — the framework** underneath it. Define your own commands as a composable grammar, and inherit the parser, the pipelines, the REPL, jobs, history, plugins, and configuration without implementing them.

Apache-2.0 · targets .NET 10 · current release `0.1.0` (see [CHANGELOG.md](CHANGELOG.md)).

---

## Quick start

To try the `vice` tool, build and install it from this repo:

```bash
./scripts/install-local.sh    # packs Vice.Cli and installs it as a global `vice` tool
vice                          # opens the interactive REPL
```

The published-tool install path and a full walkthrough live in **[docs/getting-started.md](docs/getting-started.md)**.

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

In a session, long-running work (downloads, server-streaming gRPC calls) runs as a background **job**; `jobs`, `pause`, `resume`, and `cancel` manage them. Closing the REPL with jobs still running keeps them running in the same process while the terminal stays open; closing the terminal sends `SIGHUP` and stops them. For terminal-independent persistence, run under `nohup`/`systemd`/`supervisord` or start `vice daemon`.

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

`vice-mux` is a separate stream-plumbing tool for inspecting, routing, and tee-ing byte streams — installed on its own (`dotnet tool install --global Vice.Mux.Cli`) and invoked as `vice-mux`. It reads stdin and fans bytes out to files, TCP endpoints, child processes, or named pipes; `route` forwards the stream to the destinations whose condition matches the upstream exit code. ([docs/mux-commands.md](docs/mux-commands.md))

```bash
cat ./events.ndjson | vice-mux tee to file:./copy.ndjson,tcp:collector:9000 > ./out.ndjson
some-stage | vice-mux route on 0 to file:./ok.log on 1,2 to exec:notify on '*' to file:./all.log --code 1
```

---

## Build your own CLI on Vice

Add the framework package:

```bash
dotnet add package Vice
```

Define a group of commands. Each command is a grammar (verb, optional nouns, bound targets) plus a handler. Register them by hand when wiring a single app:

```csharp
using static Vice.Dsl;

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
                Vice.Output.Line($"Hello, {ctx["name"]}!");
                return 0;
            });
    }
}
```

Build the app and run it:

```csharp
using Vice;

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
- A session REPL with job management, history, and a `daemon` mode for terminal-independent runs
- Git-style external plugins: any executable named `<app>-<verb>` on the trusted plugin path dispatches as a verb
- Pluggable, opt-in framework services (`IViceLogger`, `IKeyring`) with safe `Null` defaults
- POSIX exit codes, pager wrapping, `--json`/`--format` conventions, and an SSRF-guarded `HttpClient`

The framework API is summarized in [src/Vice/README.md](src/Vice/README.md); the knobs are in [docs/env-and-config.md](docs/env-and-config.md).

---

## Repository layout

| Path | What it is |
|---|---|
| `src/Vice.Parser` | The command-line lexer and resolver. NuGet package `Vice.Parser` — BCL-only, no dependency on the rest of the framework; `Vice` references it transitively. |
| `src/Vice` | The framework. NuGet package `Vice` — the command DSL, streaming channels, REPL, jobs, plugins, configuration; references `Vice.Parser` for lexing and resolution. |
| `src/Vice.Generators` | Roslyn source generators that wire `[ViceCommandPack]` classes into the host at compile time. |
| `src/Vice.Net` | Network command library (TCP, UDP, gRPC) for the `vice` tool, built on the framework. A library, not a tool — `IsPackable=false`. |
| `src/Vice.Cli` | The `vice` reference CLI tool. NuGet package `Vice.Cli`, `ToolCommandName` `vice` — `dotnet tool install --global Vice.Cli`. |
| `src/Vice.Mux` | The mux library for inspecting, routing, and tee-ing Vice pipeline streams. A library, not a tool. |
| `src/Vice.Mux.Cli` | The `vice-mux` companion CLI tool. NuGet package `Vice.Mux.Cli`, `ToolCommandName` `vice-mux` — `dotnet tool install --global Vice.Mux.Cli` ([docs/mux-commands.md](docs/mux-commands.md)). |
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
- [Releasing](docs/releasing.md) — versioning policy, tagging, publishing, rollback
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

To compare throughput run-over-run automatically, pass `--gate`: the harness diffs each benchmark's mean against the committed `bench/Vice.Benchmarks/baseline.json` and exits non-zero when any benchmark slows past the tolerance (`VICE_BENCH_TOLERANCE`, default `0.10` for 10%); a missing baseline is seeded from the current run. Pass `--update-baseline` to rewrite the baseline from the current run when an intended change shifts the numbers.

---

## Releasing

Four projects are packable: the two tool projects — `Vice.Cli` (the `vice` tool) and `Vice.Mux.Cli` (the `vice-mux` tool) — and the two framework libraries `Vice` and `Vice.Parser`; `Vice.Net` and the other libraries are not. The project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html); the shipped version is `<Version>` in `Directory.Build.props`, and each release is an annotated git tag `v<version>`.

To cut a release: bump `<Version>`, update [CHANGELOG.md](CHANGELOG.md), tag the commit (`git tag -a v<version> -m "v<version>"`), then pack and publish:

```bash
scripts/release.sh --verify-tag                       # pack all four packages into artifacts/release/<version>/
NUGET_API_KEY=<key> scripts/release.sh --verify-tag --push   # publish to $VICE_NUGET_FEED (default nuget.org)
```

Roll a tool back to a prior published version (every prior version stays on the feed):

```bash
scripts/rollback.sh <previous-version> all            # dotnet tool update --version <previous-version>
```

The full process — versioning policy, tagging, publishing, and rollback — is documented in **[docs/releasing.md](docs/releasing.md)**. To try the tools locally without publishing, run `./scripts/install-local.sh`.

---

## License

Apache-2.0. Copyright 2026 Infalligence Labs LLC. The root [LICENSE](LICENSE) governs every file in the tree; the SPDX identifier `Apache-2.0` is carried as package and assembly metadata rather than per-file comment headers. See [docs/licensing.md](docs/licensing.md) for the file-level provenance policy and [THIRD_PARTY_NOTICE](THIRD_PARTY_NOTICE) for redistributed dependencies.
