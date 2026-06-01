# Troubleshooting

## Rate-limit symptoms

Research-source verbs route through `PoliteHandler`, which:

- Forces a 1 s minimum gap between requests to the same hostname.
- Retries on `429 Too Many Requests` and `503 Service Unavailable` up to 3 times.
- Waits the upstream `Retry-After` if it's present, otherwise a full-jitter backoff: a random delay in `[0, min(120, 2^(attempt+1)))` seconds, capped at 2 minutes.

There is **no user-visible signal** when this throttle engages. A `search` or `archive` against a saturated source can appear to hang for tens of seconds while the handler waits between retries. Set `VICE_LOG_LEVEL=debug` to see the underlying timing; otherwise the only sign is a long pause before the response.

If the response is still `429`/`503` after the third retry, the original response is returned and you'll see an `HTTP error: ...` in stderr.

## Silent dedup waits

`build`, `test`, `restore`, and `clean` dedup by canonical path: two concurrent calls for the same `Path.GetFullPath(<path>)` share a single `dotnet` process. The second caller awaits the first call's `Task<int>` with no progress output of its own. From the second caller's perspective, the tool sits silent until the first call completes.

If a `build` call appears to hang, run `VICE_LOG_LEVEL=debug vice build <path>` — the build queue logs `build queue: starting 'build::<path>' (in-flight=N)` whenever it starts a new task, so you can see whether you're waiting on an in-flight one.

## Ctrl+C semantics

`vice` installs a `Console.CancelKeyPress` handler with two-stage behavior:

| Press | Effect |
|---|---|
| First Ctrl+C | Cancels the global `CancellationToken`. In-flight commands see cancellation and shut down gracefully. Vice prints `Shutting down — press Ctrl+C again to force exit.` to stderr. |
| Second Ctrl+C | Default runtime behavior takes over and the process exits immediately; in-memory job state is dropped. |

In session mode, an `OperationCanceledException` raised during a REPL command is caught and the REPL keeps running for the next prompt; the second Ctrl+C is the only way to force-quit.

## Session mode vs one-shot mode

| | Session mode | One-shot mode |
|---|---|---|
| Triggered by | `vice` with no args | `vice <args>` |
| Job runners | Active. Long-running operations (downloads, server-streaming gRPC calls) are submitted as background jobs and reported via `jobs`. | Not started. Operations run synchronously to completion. |
| `jobs` / `pause` / `resume` / `cancel` / `history` / `clear` | Built-in, handled by the session loop. | Not available. |
| Daemon detach | If you exit the REPL while jobs are active, the process detaches and the jobs continue in a background daemon. Reconnect by running `vice` again. You can also start a daemon explicitly with `vice daemon` or `vice --daemon <command>` (useful under systemd/supervisord), and inspect a running daemon with `vice status`. | `vice daemon`, `vice --daemon`, and `vice status` all apply. |

A side-effect to be aware of: a `download` issued from a one-shot invocation runs to completion in-line and blocks the caller; the same command in session mode returns immediately with `Queued download as job #N`.

## "Destination is outside the allowed roots"

`download` and `archive` only write into the current working directory by default. To allow writes elsewhere, set `VICE_ALLOWED_ROOTS` to a `:`-separated list of allowed parent directories before invoking Vice. `unarchive` is independently restricted to the standard write roots — the user home directory, the system temp directory, and the current working directory.

## "vice help <verb>" unknown

`vice help <verb>` is wired and lives in framework `BuiltinCommands` — but the lookup goes through `registry.FindByVerb(commandName)`, and matching is by the head verb token of each registration. Commands registered with multiple synonyms (`tcp, tcpcat`; `grpc, grpcurl`; `search, find`; `fetch, get`; `download, dl`) match on every synonym in their head token, so `vice help tcpcat` and `vice help dl` both resolve. If you see `Unknown command`, you've typed a non-head token (e.g. `vice help send`, where `send` is a body word, not a verb).

## Logging output is empty

The default log level is `warn`. `Info` and `debug` lines are filtered out. To see the structured "build queue starting" lines or retry warnings, set `VICE_LOG_LEVEL=debug` (or `info` for less noise). Logs go to stderr, never stdout.
