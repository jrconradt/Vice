# Architecture and internal layering

This document records the intended layering of the Vice assemblies and the
internal subsystem layers inside the core `Vice` assembly. It exists so that
feature work does not deepen coupling to incidental core subsystems, and so the
extraction boundaries that already hold (and the ones that intentionally do not)
are explicit.

## Assembly graph

The project graph is acyclic. Features depend only on shared lower assemblies;
no shared assembly depends on a feature.

```
Vice.Parser        (BCL-only leaf: lexer + resolver, no inbound Vice dependency)
   ^
   |
Vice               (core host runtime; references Vice.Parser)
   ^         ^        ^        ^
   |         |        |        |
Vice.Net  Vice.Files Vice.Build Vice.Mux   (feature modules; each references Vice only)
   ^                                ^
   |                                |
Vice.Cli                        Vice.Mux.Cli   (entry-point CLIs; compose features)
```

`Vice.Generators` is a Roslyn source generator consumed as an analyzer by `Vice`
and the CLIs; it produces no runtime assembly reference.

## Vice.Parser is a standalone package

`Vice.Parser` is the command-line lexer and resolver: tokenization
(`Lexer` / `Token`), global-option extraction (`GlobalOptionExtractor`),
chain resolution (`CommandChain` / `CommandResolver`), the structured parse
outcome (`ParseResult` / `MatchDiagnostic`), and the host-facing descriptor
surface (`IChainDescriptor` / `ITargetDescriptor`).

It depends only on the .NET base class library — no type in `Vice.Parser`
references any other `Vice.*` namespace — so it ships as its own package. The
`Vice` host references it, so framework consumers receive it transitively, while
a tool that needs only lexing and resolution can reference `Vice.Parser` alone
and pull in nothing else. `Vice.Parser.Tests` exercises it directly.

## Core internal layers

Inside the `Vice` assembly the folders form layers. Lower layers know nothing of
higher layers; a higher layer may use any lower layer. Listed lowest first.

| Layer | Folders | Role |
| --- | --- | --- |
| Foundation | `Concurrency`, `Display`, `Nodes`, `Composition` | Primitives: wait-free helpers, terminal rendering, DSL node tree, expression composition. |
| Parsing surface | `Lexicon`, `Options`, `Core` | Connector vocabulary, typed option registries, the public `Dsl` / `TargetDef` builder surface — all thin layers over `Vice.Parser`. |
| Logging and configuration | `Logging`, `Configuration`, `Sinks` | Structured logging, keyring/config, log sinks. |
| Execution | `Commands`, `Execution`, `Help`, `Completions`, `Manpages` | Command registry, pipeline execution, help rendering, shell-completion and man-page generation. |
| Runtime services | `Ipc`, `Jobs`, `Session`, `Streaming`, `Output`, `Plugins` | Pipe-server IPC, job/daemon management, the session REPL, typed streaming, output sinks, external-plugin dispatch. |
| Host | `ViceApp`, `ViceAppBuilder`, `Signals` | Wires the layers into a runnable application. |

## Subsystems that intentionally stay in core

`Completions` and `Manpages` are self-contained in purpose but
are not extractable to their own assemblies without introducing a project cycle.
They stay in `Vice`; the layering table above is the contract that keeps their
coupling from spreading.

- `Completions` consumes `Commands`, `Nodes`, and `Options`, and is itself
  consumed only by `Commands`. Extracting it would put `Commands` on both sides
  of the boundary.
- `Manpages` consumes `Commands`, `Help`, and `Options`, and is consumed only by
  `Commands`. Same cycle as `Completions`.

If one of these is to be extracted later, the cycle has to be broken first — for
example by inverting the `Commands` dependency through an interface
owned by the lower layer — not by moving files across an assembly boundary that
the type graph still spans.

## Rule for new feature work

A new feature module references `Vice` and nothing below it directly; it reaches
parsing or execution only through the public surface those layers
expose. When a feature needs a capability that today lives deep in core, surface
it on the owning layer rather than reaching across layers — that keeps the
acyclic assembly graph and the internal layering both intact.
