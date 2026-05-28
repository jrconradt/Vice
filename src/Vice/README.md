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
- Git-style external plugin discovery: any executable named `<app>-<verb>` on PATH dispatches as a verb (the host's executable name supplies `<app>`)
- Hookable framework services: `IViceLogger`, `IKeyring`, telemetry sink
- Global options for pager, clipboard, and locale

## Companion packages

- [`Vice.Parser`](https://www.nuget.org/packages/Vice.Parser) — standalone lexer/resolver
- [`Vice.Generators`](https://www.nuget.org/packages/Vice.Generators) — source generators for command composition
- [`Vice.Net`](https://www.nuget.org/packages/Vice.Net) — reference CLI tool built on this framework

Apache 2.0.
