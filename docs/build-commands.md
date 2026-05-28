# Build commands

Thin wrappers over the local `dotnet` CLI. Every verb takes an optional `{path}` target (project file, solution file, or directory); when omitted, the current working directory is used.

## Verbs

| Verb | dotnet equivalent |
|---|---|
| `build` | `dotnet build <path>` |
| `test` | `dotnet test <path>` |
| `restore` | `dotnet restore <path>` |
| `clean` | `dotnet clean <path>` |

### Synopsis

```
vice build [<path>]
vice test [<path>]
vice restore [<path>]
vice clean [<path>]
```

### Examples

```bash
vice build
vice test ./tests/MyLib.Tests/
vice restore ./MyApp.sln
vice clean
```

## Dedup by canonical path

Each verb keys its dispatch by `<verb>::<Path.GetFullPath(<path>)>`. If a second call arrives for the same canonical path while the first is still in flight, the second caller awaits the first call's `Task<int>` instead of spawning a parallel `dotnet` process. Both callers receive the same exit code; both see the same stdout.

Why this matters:

- In session mode, two REPL users (or two pipelines that both depend on a build) can't race to build the same project, corrupting `obj/` lockfiles.
- A pipeline like `build ./Foo.csproj then test ./Foo.csproj` doesn't double-build; the `test` step awaits the in-flight `build` if it's still running for the same path.
- Different paths run in parallel — the dedup is path-scoped, not global.

The trade-off: a concurrent caller looks like a hang until the in-flight call finishes, with no visible signal. See [troubleshooting.md](troubleshooting.md#silent-dedup-waits).
