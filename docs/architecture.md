# Architecture and internal layering

This document records the intended layering of the Vice assemblies and the
internal subsystem folders inside each runtime assembly. It exists so that
feature work does not deepen coupling to incidental subsystems, and so the
extraction boundaries that already hold (and the ones that intentionally do not)
are explicit.

## Assembly graph

The project graph is acyclic. Features depend only on shared lower assemblies;
no shared assembly depends on a feature.

Three leaves carry no inbound `Vice.*` dependency: `Vice.Foundation` (BCL-only
shared primitives), `Vice.Parser` (BCL-only lexer + resolver), and
`Vice.Generators` (the Roslyn source generator, consumed as an analyzer and
producing no runtime assembly reference). `Vice.Jobs` sits just above
`Vice.Foundation`. The framework root `Vice` references `Vice.Foundation`,
`Vice.Jobs`, and `Vice.Parser`. `Vice.Host` adds the session REPL on top of
`Vice` and `Vice.Jobs`. Feature libraries (`Vice.Net`, `Vice.Research`,
`Vice.Files`, `Vice.Build`, `Vice.Mux`) build on `Vice` and `Vice.Foundation` —
`Vice.Net` also reaches `Vice.Jobs` for background work, and `Vice.Research`
builds on `Vice.Net`, reaching `Vice`, `Vice.Foundation`, and `Vice.Jobs`
transitively. The two entry-point CLIs compose the libraries they need.

Each project and the `ProjectReference` edges it declares (the `Vice.Generators`
analyzer reference is noted separately below, not as a runtime edge):

```
Vice.Foundation  -> (none; BCL-only leaf)
Vice.Parser      -> (none; BCL-only leaf)
Vice.Generators  -> (none; analyzer leaf)
Vice.Jobs        -> Vice.Foundation
Vice             -> Vice.Foundation, Vice.Jobs, Vice.Parser
Vice.Host        -> Vice, Vice.Jobs, Vice.Foundation
Vice.Net         -> Vice, Vice.Jobs, Vice.Foundation
Vice.Research    -> Vice.Net
Vice.Files       -> Vice, Vice.Foundation
Vice.Build       -> Vice, Vice.Foundation
Vice.Mux         -> Vice, Vice.Foundation
Vice.Cli         -> Vice, Vice.Host, Vice.Net, Vice.Research, Vice.Files, Vice.Build, Vice.Mux
Vice.Mux.Cli     -> Vice, Vice.Host, Vice.Mux
```

`Vice.Generators` is a Roslyn source generator consumed as an analyzer reference
by `Vice`, `Vice.Build`, `Vice.Cli`, and `Vice.Mux.Cli`; it produces no runtime
assembly reference.

## Vice.Parser is a standalone leaf

`Vice.Parser` is the command-line lexer and resolver: tokenization
(`Lexer` / `Token`), global-option extraction (`GlobalOptionExtractor`),
chain resolution (`CommandChain` / `CommandResolver`), the structured parse
outcome (`ParseResult` / `MatchDiagnostic`), and the host-facing descriptor
surface (`IChainDescriptor` / `ITargetDescriptor`).

It depends only on the .NET base class library — no type in `Vice.Parser`
references any other `Vice.*` namespace. The `Vice` framework references it
directly, so framework consumers receive it transitively, while a tool that
needs only lexing and resolution can reference `Vice.Parser` alone and pull in
nothing else. `Vice.Parser.Tests` exercises it directly.

## Vice.Generators analyzer-diagnostic suppressions

`Vice.Generators` sets `IsPackable=false`; it is consumed in-tree as an analyzer
reference and never shipped as a NuGet package. Its `NoWarn` set is deliberate
and queryable here rather than inline. `RS1041` is suppressed unconditionally;
`RS2000;RS2001;RS2002;RS2008` are suppressed only under the
`Condition="'$(IsPackable)' != 'true'"` property group:

| Diagnostic | Rule | Why suppressed |
| --- | --- | --- |
| RS1041 | Compiler-extension targets multiple frameworks | The generator targets `net10.0` only and is referenced inside this solution, not redistributed, so the multi-targeting guidance does not apply. |
| RS2000 | Add a public type to the analyzer release tracking file | The analyzer release ledger (`AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md`) was removed; release tracking is unnecessary for a non-packaged in-tree generator. |
| RS2001 | Update the analyzer release tracking file | Same as RS2000: no ledger is maintained. |
| RS2002 | Diagnostic missing from the analyzer release tracking file | Same as RS2000: no ledger is maintained. |
| RS2008 | Enable analyzer release tracking | Release tracking is intentionally off for this non-packaged generator. |

If `Vice.Generators` is ever packaged for external consumption by setting
`IsPackable=true`, the conditional `NoWarn` group drops out automatically and the
release-tracking analyzers (`RS2000;RS2001;RS2002;RS2008`) fire again — the build
then requires restoring the `AnalyzerReleases.Shipped.md` /
`AnalyzerReleases.Unshipped.md` ledger files. The restore obligation is enforced
by the build rather than by re-reading this section.

## Internal layers across the runtime assemblies

The runtime functionality is split across four assemblies — `Vice.Foundation`,
`Vice.Jobs`, `Vice`, and `Vice.Host` — each carrying its own folders. Lower
assemblies know nothing of higher ones; within an assembly, a higher folder may
use any lower folder. Listed lowest first.

| Assembly | Folders | Role |
| --- | --- | --- |
| `Vice.Foundation` | `Concurrency`, `Execution`, `Logging`, `Persistence`, `Sinks` | BCL-only primitives: wait-free helpers, low-level execution scaffolding, structured logging and log sinks, and path-gating + atomic-file write primitives (`SafeWriteRoots`, `AtomicFile`, `FileAccessControl`) consumed by every assembly above. |
| `Vice.Jobs` | `Jobs` | The background-job model — each job is a detached child process re-executing the host binary as `job run <descriptor>`; the job id is the child pid. `JobLedger` reads and writes per-job JSON records under the state directory, `JobSpawner` (`IJobSubmitter`) launches children, and `JobHarness` runs inside the child, dispatching to the registered `IJobRunner` and serializing record writes through a `SerialQueue`. References `Vice.Foundation` only. |
| `Vice` | `Lexicon`, `Options`, `Core`, `Nodes`, `Composition`, `Display`, `Commands`, `Execution`, `Streaming`, `Help`, `Completions`, `Manpages`, `Contracts`, `Sinks` | The framework. The parsing surface (`Lexicon`, `Options`, `Core`'s `Dsl` / `TargetDef`) over `Vice.Parser`; the DSL node tree and expression composition (`Nodes`, `Composition`); terminal rendering (`Display`); the command registry and pipeline execution (`Commands`, `Execution`, `Streaming`); and help, shell-completion, and man-page generation (`Help`, `Completions`, `Manpages`). |
| `Vice.Host` | `Core`, `Ipc`, `Plugins`, `Session`, `ViceApp` / `ViceAppBuilder` | Daemon liveness and message handling (`Core`), pipe-server IPC, external-plugin dispatch, the session REPL, and the `ViceApp` / `ViceAppBuilder` surface that wires everything into a runnable interactive application. |

## Subsystems that intentionally stay in `Vice`

`Completions` and `Manpages` are self-contained in purpose but are not
extractable to their own assemblies without introducing a project cycle. They
stay in `Vice`; the layering table above is the contract that keeps their
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

A feature module (`Vice.Net`, `Vice.Research`, `Vice.Files`, `Vice.Build`,
`Vice.Mux`) references `Vice` and `Vice.Foundation`, reaching parsing or
execution only through the public surface those layers expose; it does not reach
into `Vice.Host`. `Vice.Research` reaches `Vice` and `Vice.Foundation` through
`Vice.Net` rather than referencing them directly. When a
feature needs a capability that today lives deep in a lower assembly, surface it
on the owning layer rather than reaching across layers — that keeps the acyclic
assembly graph and the internal layering both intact.
