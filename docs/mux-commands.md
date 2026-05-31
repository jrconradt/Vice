# vice-mux commands

`vice-mux` is the companion tool for inspecting, splitting, routing, and tee-ing byte streams on a Vice pipeline. It reads stdin and writes to stdout and to one or more **sinks** (files, TCP endpoints, child processes, named pipes). It is a separate tool from `vice`, with its own package and command name.

## Install

```bash
dotnet tool install --global Vice.Mux.Cli
```

The installed command is `vice-mux`. Requires .NET 10 on the host. To install from a local checkout instead of nuget.org, run `./scripts/install-local.sh`, which packs and installs both `vice` and `vice-mux`.

Run `vice-mux` with no arguments to open the REPL, or pass a verb to run one command and exit:

```bash
vice-mux strategies
vice-mux help
```

## How it works

Every verb consumes stdin as a stream of byte chunks (default 65536 bytes per chunk) and forwards each chunk to its destinations. `inspect` is a metering passthrough; `tee` broadcasts; `route` and `split` send each chunk to one sink chosen by a routing **strategy**. Sinks are addressed by a `scheme:rest` **sink spec** (see [Sinks](#sinks)).

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

Read stdin and send each chunk to exactly one of the named sinks, chosen per chunk by `<strategy>`. At least one sink is required.

### Synopsis

```
vice-mux route by <strategy> to <sink>[,<sink>...]
```

### Example

```bash
cat ./requests.log | vice-mux route by hash to file:./shard-a.log,file:./shard-b.log
```

## split

Read stdin and route each chunk to one of `<n>` sinks chosen by `<strategy>`. If you provide explicit sinks, their count must equal `<n>`; if you omit them, `<n>` files named `./mux-0.out` … `./mux-{n-1}.out` are created.

### Synopsis

```
vice-mux split into <n> by <strategy> to <sink>[,<sink>...]
vice-mux split into <n> by <strategy>
```

### Examples

```bash
cat ./stream.bin | vice-mux split into 4 by roundrobin
cat ./stream.bin | vice-mux split into 2 by sticky-key to file:./a.bin,file:./b.bin --key-offset 0 --key-length 8
```

## strategies

List the registered routing strategies, one per line as `name kind description`.

### Synopsis

```
vice-mux strategies
```

## Strategies

`route` and `split` select a sink per chunk with one of these built-in strategies:

| Name | Kind | Selection |
|---|---|---|
| `roundrobin` | unicast | Cycle sinks in order, one chunk per sink. |
| `hash` | unicast | FNV-1a 64 of the chunk, modulo sink count. Seeded by `--seed`. |
| `random` | unicast | Uniform random sink per chunk (xorshift seeded by `--seed`). |
| `broadcast` | broadcast | Write every chunk to every sink. |
| `sticky-key` | unicast | FNV-1a over a fixed byte slice (`--key-offset` / `--key-length`) modulo sink count, so chunks sharing that key land on the same sink. |
| `weighted` | unicast | Deterministic interleave by integer weights from `--strategy-arg` (colon-separated, e.g. `3:1`); falls back to round-robin when no weights are given. |

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
| `--seed <uint64>` | 0 | hash, random, sticky-key | Hash / random seed. |
| `--key-offset <n>` | 0 | sticky-key | Byte offset into each chunk for the key slice. |
| `--key-length <n>` | 4 | sticky-key | Byte length of the key slice. |
| `--strategy-arg <s>` | (none) | weighted | Strategy-specific configuration; for `weighted`, colon-separated integer weights such as `3:1`. |

## Environment

| Variable | Effect |
|---|---|
| `VICE_LOG_LEVEL` | Log verbosity for `vice-mux` diagnostics: `trace`, `debug`, `info`, `warn` (default), `error`. |
