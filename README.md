# Vice

**A .NET CLI framework whose commands read like English — and the tools built on it.**

Most command-line tools make you memorize flags. Vice lets you (and your users) write what you mean:

```bash
vice search "graph neural networks" on source arxiv --limit 20
vice tcp send "PING" to endpoint localhost:6379
vice search files by type csharp in ./src then write to file ./inventory.txt
```

Those sentences aren't a gimmick. A real lexer and parser turn them into typed commands that pipe into one another through backpressured streaming channels, run as background jobs, and share a session REPL — all provided by the framework, for free.

Vice is two things:

- **`vice` — a ready-to-use CLI tool** for network probes, scientific-literature search, filesystem work, and `dotnet` build orchestration. Commands read like English and pipe into each other.
- **Vice — the framework** underneath it. Define your own commands as a small composable grammar, and inherit the parser, the pipelines, the REPL, jobs, history, plugins, and configuration without writing any of it yourself.

Apache-2.0 · targets .NET 10 · current release `0.1.0` (see [CHANGELOG.md](CHANGELOG.md)).

---

## Quick start

The fastest way to try the `vice` tool is to build and install it from this repo:

```bash
./scripts/install-local.sh    # packs Vice.Net and installs it as a global `vice` tool
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

In a session, long-running work (downloads, server-streaming gRPC calls) runs as a background **job**; `jobs`, `pause`, `resume`, and `cancel` manage them, and closing the REPL with jobs still running detaches them into a daemon.

---

## What the `vice` tool can do

Every area below has a dedicated guide under [`docs/`](docs/).

**Network** — raw TCP, UDP, and gRPC. ([docs/network-commands.md](docs/network-commands.md))

```bash
vice grpc list services on endpoint localhost:50051
vice grpc call helloworld.Greeter/SayHello on endpoint localhost:50051 with data '{"name":"World"}'
```

**Research** — search, fetch, download, and bulk-archive across five scientific sources (arXiv, Project Gutenberg, PubMed, UniProt, AlphaFold), with an on-disk cache and a polite per-host rate limiter. ([docs/research-commands.md](docs/research-commands.md) · [docs/sources.md](docs/sources.md))

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

**Pipelines** — any producing command pipes into a consuming one through a real streaming channel:

```bash
vice search "graph" on source arxiv --limit 50 then write to file ./graph-papers.txt
vice read ./large.bin.gz then stream to count
```

---

## Build your own CLI on Vice

Add the framework package:

```bash
dotnet add package Vice
```

Define a group of commands as a `[ViceCommandPack]`. Each command is a grammar (verb, optional nouns, bound targets) plus a handler:

```csharp
using Vice.Composition;
using static Vice.Dsl;

[ViceCommandPack]
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
    .WithDescription("A friendly little CLI")
    .Build();

GreetCommands.Register(app);

return await app.RunAsync(args, CancellationToken.None);
```

Now `mytool greet world` prints `Hello, world!`, and `mytool` with no arguments opens a REPL — you didn't write either.

The grammar composes with operators: `>` sequences words, `*` binds a target, and `Verbs`/`Nouns`/`Connectors`/`Targets` provide a shared vocabulary so commands read naturally:

```csharp
app.Register(
    Verbs.Grpc() > Nouns.Connect() * Targets.Endpoint,
    "Connect to a gRPC endpoint",
    async (ctx, ct) => { /* ... */ });
```

Mark packs with `[ViceCommandPack]` and call `ComposeFromAttributes()` to have the **[Vice.Generators](src/Vice.Generators/README.md)** source generator discover and wire every pack at compile time — no runtime reflection. That's how the `vice` tool itself is assembled.

What you inherit by building on Vice:

- Natural-language lexer and parser with synonyms and aliases
- Typed, backpressured streaming channels between piped stages
- A session REPL with job management, history, and daemon detachment
- Git-style external plugins: any executable named `<app>-<verb>` on the trusted plugin path dispatches as a verb
- Pluggable, opt-in framework services (`IViceLogger`, `IKeyring`, update checks, telemetry) with safe `Null` defaults
- POSIX exit codes, XDG-compliant state directories, pager wrapping, `--json`/`--format` conventions, and an SSRF-guarded `HttpClient`

The framework API is summarized in [src/Vice/README.md](src/Vice/README.md); the knobs are in [docs/env-and-config.md](docs/env-and-config.md).

---

## Repository layout

| Path | What it is |
|---|---|
| `src/Vice` | The framework. NuGet package `Vice` — lexer/parser, the command DSL, streaming channels, REPL, jobs, plugins, configuration. |
| `src/Vice.Generators` | Roslyn source generators that wire `[ViceCommandPack]` classes into the host at compile time. |
| `src/Vice.Net` | The `vice` reference CLI tool, built entirely on the framework. |
| `src/Vice.Mux` | `vice-mux`, a companion tool for inspecting, splitting, routing, and tee-ing Vice pipeline streams. |
| `docs/` | User and configuration guides. |
| `scripts/` | Build, test, and local-install helpers ([scripts/README.md](scripts/README.md)). |
| `tests/` | Unit tests for the framework, generators, parser, network layer, and mux tool. |

---

## Configuration

Vice is configured through environment variables, XDG-standard directories, and optional settings files — log level, allowed write roots for downloads, cache and state locations, color control, outbound-connection allow/deny lists, and plugin discovery. The complete reference is in **[docs/env-and-config.md](docs/env-and-config.md)**.

---

## Documentation

- [Getting started](docs/getting-started.md) — install, verify, first commands, one-shot vs. session mode
- [Network commands](docs/network-commands.md) — TCP, UDP, gRPC
- [Research commands](docs/research-commands.md) — search, fetch, download, archive
- [Research sources](docs/sources.md) — per-source query syntax, aliases, formats
- [File commands](docs/file-commands.md) — read, write, stream, archives, filesystem search
- [Build commands](docs/build-commands.md) — `dotnet` wrappers and build deduplication
- [Environment and configuration](docs/env-and-config.md) — env vars, XDG paths, plugins, services, exit codes
- [Known issues](docs/known-issues.md) · [Troubleshooting](docs/troubleshooting.md)

---

## Building from source

```bash
./scripts/build.sh    # restore and build the solution
./scripts/test.sh     # run all tests
./scripts/demo.sh     # build, install, exercise a few commands, then uninstall
```

Requires the .NET 10 SDK.

---

## License

Apache-2.0. Copyright 2026 Infalligence Labs LLC. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICE](THIRD_PARTY_NOTICE).
