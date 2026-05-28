# Getting started

## Install

```bash
dotnet tool install --global Vice.Net
```

The installed command is `vice`. Requires .NET 10 on the host.

## Verify

Run `vice` with no arguments. With no args it opens an interactive REPL:

```
$ vice
vice>
```

Type `help` for the command list, `exit` (or `quit`, or Ctrl+D) to leave. From there:

```
vice> version
vice v1.0.0
vice> list commands
```

To run a single command and return, pass it as args instead:

```bash
vice version
vice help
vice help search
```

## Three first commands

**File search** — list every C# file under the current directory:

```bash
vice search files by type csharp
```

**Network probe** — list gRPC services exposed by a local reflection-enabled server:

```bash
vice grpc list services on endpoint localhost:50051
```

**Research** — search arXiv for recent papers on a topic:

```bash
vice search "graph neural networks" on source arxiv
```

## Mode summary

| Mode | How to enter | Behavior |
|---|---|---|
| One-shot | `vice <args>` | Parses args, runs one pipeline, exits with the command's return code. |
| Session | `vice` (no args) | Opens the REPL. Background jobs are managed in-process. Closing the REPL with active jobs detaches them into a daemon. |

## Next

- [Network commands](network-commands.md)
- [Research commands](research-commands.md)
- [Build commands](build-commands.md)
- [File commands](file-commands.md)
