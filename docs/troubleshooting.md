# Troubleshooting

## Rate-limit symptoms

Research-source verbs route through `PoliteHandler`, which:

- Forces a 1 s minimum gap between requests to the same hostname, or 3 s for `export.arxiv.org` to honor the arXiv API Terms of Use.
- Retries on `429 Too Many Requests` and `503 Service Unavailable` up to 3 times.
- Waits the upstream `Retry-After` if it's present, otherwise a full-jitter backoff: a random delay in `[0, min(120, 2^(attempt+1)))` seconds, capped at 2 minutes.

When an upstream host returns `429`/`503` and the handler backs off before retrying, it emits a `Warn`-level line to stderr (`polite: <host> returned <code>; backing off <n>s then retrying ...`), so an upstream rate limit is visible at the default log level. The proactive minimum-gap spacing between successive requests is not announced; a `search` or `archive` against a saturated source can still appear to pause for a few seconds between requests. Set `VICE_LOG_LEVEL=debug` to see the per-request timing.

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
| Background jobs | Downloads are spawned as detached `vice job run` child processes and reported via `jobs`. Each job writes its own record under the state directory (`$XDG_STATE_HOME/vice/<app>-jobs/`, falling back to the local application data directory); the record's id is the job's pid. Jobs ignore `SIGHUP`, so they survive both REPL exit and terminal close. | Operations run synchronously to completion in the foreground. |
| `jobs` / `cancel` / `history` / `clear` | Built-in. `jobs` and `cancel` read the job ledger and work in every mode, including against jobs started from other sessions. | `jobs` and `cancel` work identically; `history` is empty outside a REPL. |
| Daemon | `vice daemon` (or `vice --daemon <command>` under systemd/supervisord) serves the IPC control channel; inspect it with `vice status`. Jobs are independent of the daemon: each is its own process, and a daemon crash loses nothing. A killed job process leaves its `.partial` download intact; re-running the same download resumes from the recorded byte offset. | `vice daemon`, `vice --daemon`, and `vice status` all apply. |

A side-effect to be aware of: a `download` issued from a one-shot invocation runs to completion in-line and blocks the caller; the same command in session mode returns immediately with `Queued download as job #N`, where `N` is the spawned process's pid.

## "Command output exceeds the 16 MiB daemon IPC frame limit"

A command run over the daemon control channel returns its output in a single length-prefixed IPC frame, hard-capped at 16 MiB. The full textual output is buffered and serialized whole; there is no chunked or streaming response over the control channel. When a command's output exceeds that ceiling, the daemon fails the command with `Command output of <N> bytes exceeds the <limit>-byte daemon IPC frame limit. Re-run the command in a non-daemon session or redirect its output to a file.` For known large-output verbs (`read`, `search`, gRPC reflection), run them in a non-daemon session or redirect their output to a file.

## "Destination is outside the allowed roots"

`download` and `archive` only write into the current working directory by default. To allow writes elsewhere, set `VICE_ALLOWED_ROOTS` to a `:`-separated list of allowed parent directories before invoking Vice. `unarchive` is independently restricted to the standard write roots — the user home directory, the system temp directory, and the current working directory.

## "vice help <verb>" unknown

`vice help <verb>` is wired and lives in framework `BuiltinCommands` — but the lookup goes through `registry.FindByVerb(commandName)`, and matching is by the head verb token of each registration. Commands registered with multiple synonyms (`tcp, tcpcat`; `grpc, grpcurl`; `search, find`; `fetch, get`; `download, dl`) match on every synonym in their head token, so `vice help tcpcat` and `vice help dl` both resolve. If you see `Unknown command`, you've typed a non-head token (e.g. `vice help send`, where `send` is a body word, not a verb).

## Logging output is empty

The default log level is `warn`. `Info` and `debug` lines are filtered out. To see the structured "build queue starting" lines or retry warnings, set `VICE_LOG_LEVEL=debug` (or `info` for less noise). Logs go to stderr, never stdout.
