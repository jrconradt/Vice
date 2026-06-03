# Vice

A fluent CLI framework for .NET with a natural-language command DSL. Commands compose as expressions (`verb("tcp") > Nouns.Send() * Targets.Data > Connectors.To() > Nouns.Endpoint() * Targets.Endpoint`), pipe through typed streaming channels, and inherit jobs, history, and a session REPL automatically.

## Install

```bash
dotnet add package Vice
```

Requires .NET 10.

## What it is

- DSL for defining commands as fluent expressions resolved by a lexer and parser
- Typed streaming channels with backpressure between piped stages
- Built-in session REPL with job management (`jobs`, `pause`, `resume`, `cancel`, `history`) and background-daemon detachment on exit
- `[ViceCommandPack]` attribute for in-process command extensions with full host-service access
- Git-style external plugin discovery: an executable named `<app>-<verb>` in the trusted plugin directory (`$VICE_PLUGIN_DIR`) dispatches as a verb (the host's executable name supplies `<app>`); `$PATH` is intentionally not consulted
- Hookable framework services: `IViceLogger`
- Global options for pager and locale

## What ships in this package

The `Vice` package wires the framework together:

- Lexing and resolution come from the standalone [`Vice.Parser`](https://www.nuget.org/packages/Vice.Parser) package (the `Vice.Parser` namespace — lexer, `CommandResolver`, `ParseResult`), referenced as a dependency so installing `Vice` pulls it in automatically.
- The command-composition source generator ships embedded under `analyzers/` in this package and runs automatically; it is not a separately installable `Vice.Generators` package.

The reference CLI tool is published separately as [`Vice.Cli`](https://www.nuget.org/packages/Vice.Cli) (`dotnet tool install --global Vice.Cli`), produced from `Vice.Cli`; its network commands live in the non-packable `Vice.Net` library.

Apache 2.0.
