# Vice.Generators

Roslyn source generators for the Vice framework. Discovers `[ViceCommandPack]`-decorated classes at compile time and emits the composition glue that wires commands, DI, and chain descriptors into the host.

## Install

Not separately installable. The generator targets `net10.0` and is `IsPackable=false`; it ships embedded under `analyzers/` inside the [`Vice`](https://www.nuget.org/packages/Vice) package and runs automatically. Reference `Vice` and the generator is wired up for you.

## What it does

- Scans for types annotated with `[ViceCommandPack]`
- Emits composition code so the host can resolve commands and their dependencies without runtime reflection
- Ships as an analyzer (`IsRoslynComponent`); no runtime assembly added to the consuming project

Apache 2.0.
