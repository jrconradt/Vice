# vice-mux commands

`vice-mux` is the companion tool for inspecting, routing, and tee-ing byte streams on a Vice pipeline. It reads stdin and writes to stdout and to one or more **sinks** (files, TCP endpoints, child processes, named pipes). It is a separate tool from `vice`, with its own package and command name.

## Install

```bash
dotnet tool install --global Vice.Mux.Cli
```

The installed command is `vice-mux`. Requires .NET 10 on the host. To install from a local checkout instead of nuget.org, run `./scripts/install-local.sh`, which packs and installs both `vice` and `vice-mux`.

Run `vice-mux` with no arguments to open the REPL, or pass a verb to run one command and exit:

```bash
vice-mux help
```

## How it works

Every verb consumes stdin as a stream of byte chunks (default 65536 bytes per chunk) and forwards each chunk to its destinations. `inspect` is a metering passthrough; `tee` broadcasts to every sink; `route` forwards stdin to the destinations whose **condition** matches the upstream exit code. Sinks are addressed by a `scheme:rest` **sink spec** (see [Sinks](#sinks)).

## inspect

Passthrough meter. Copies stdin to stdout unchanged and writes chunk, byte, and format metadata to stderr — use it to see what is flowing through a pipe without altering it. With `--peek <n>`, the first `<n>` bytes of each chunk are also dumped as hex to stderr.

### Synopsis

```
vice-mux inspect
```

### Example

```bash
cat ./payload.bin | vice-mux inspect --peek 16 > /dev/null
```

stderr carries lines like `[mux:inspect] chunk=1 bytes=65536 peek=89504E470D0A1A0A...` and a closing `[mux:inspect] done chunks=… bytes=… format=… elapsed=…ms`. The guessed `format` is one of `Empty`, `Binary`, `Text`, or `JsonLines`.

## tee

Broadcast. Reads stdin and writes every chunk to every named sink **and** to stdout. At least one sink is required.

### Synopsis

```
vice-mux tee to <sink>[,<sink>...]
```

### Example

```bash
cat ./events.ndjson | vice-mux tee to file:./copy.ndjson,tcp:collector:9000 > ./passthrough.ndjson
```

## route

Read stdin and forward it to the destinations whose **condition** matches the upstream exit code. A command is one or more `on <condition> to <destination>` clauses. Every clause whose condition matches receives the full stream; a stream that matches no clause is dropped.

The exit code to match against comes from `--code` (default `0`).

### Condition syntax

| Condition | Matches |
|---|---|
| `0` | Exactly that exit code. |
| `1,2,130` | Any code in the comma-separated list. |
| `*` or `all` | Every exit code. |

### Synopsis

```
vice-mux route on <condition> to <sink> [on <condition> to <sink>]...
```

### Example

```bash
some-stage | vice-mux route \
  on 0   to file:./ok.log \
  on 1,2 to exec:notify-failure \
  on *   to file:./all.log \
  --code 1
```

With `--code 1`, the `1,2` clause and the `*` clause both fire; the `0` clause does not.

## Sinks

A sink spec is `scheme:rest`. A spec with no `scheme:` prefix is treated as a `file:` path. Multiple sinks are comma-separated in one `to` clause.

| Scheme | Form | Destination |
|---|---|---|
| `file` | `file:<path>` | Truncate-or-create the file; parent directories are created. |
| `append` | `append:<path>` | Append to the file (`FileShare.Read` while writing). |
| `tcp` | `tcp:<host>:<port>` | Connect a TCP socket (5 s connect timeout, `NoDelay`). |
| `exec` | `exec:<command line>` | Start a child process and write to its stdin; quoted segments are kept intact. |
| `pipe` | `pipe:<path>` | Open an existing named pipe / FIFO for writing. |
| `null` | `null:` | Discard everything (`/dev/null`-equivalent). |

## Global options

| Option | Default | Applies to | Effect |
|---|---|---|---|
| `--chunk-size <n>` | 65536 | all verbs | Byte-stream chunk size in bytes. |
| `--peek <n>` | 0 | inspect | Emit the first `<n>` bytes of each chunk as hex on stderr. |
| `--code <n>` | 0 | route | Upstream exit code that conditions are matched against. |

## Environment

| Variable | Effect |
|---|---|
| `VICE_LOG_LEVEL` | Log verbosity for `vice-mux` diagnostics: `trace`, `debug`, `info`, `warn` (default), `error`. |
