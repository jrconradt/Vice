# Vice.Generators

Roslyn source generators for the Vice framework. Discovers `[ViceCommandPack]`-decorated classes at compile time and emits the composition glue that wires commands, DI, and chain descriptors into the host.

## Install

```bash
dotnet add package Vice.Generators
```

Targets `netstandard2.0` as a Roslyn analyzer component. Consume from a project that already references [`Vice`](https://www.nuget.org/packages/Vice).

## What it does

- Scans for types annotated with `[ViceCommandPack]`
- Emits composition code so the host can resolve commands and their dependencies without runtime reflection
- Ships as an analyzer (`IsRoslynComponent`); no runtime assembly added to the consuming project

Apache 2.0.
