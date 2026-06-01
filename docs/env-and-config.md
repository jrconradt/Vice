# Environment and configuration

## Environment variables

| Variable | Default | Effect |
|---|---|---|
| `VICE_LOG_LEVEL` | `warn` | Logger threshold. Accepted values: `trace`, `debug`, `info`, `warn` (or `warning`), `error`. Anything else falls back to `warn`. Set to `debug` to see stack traces on REPL handler exceptions. |
| `VICE_ALLOWED_ROOTS` | (empty) | `:`-separated list of additional directories outside the current working directory where `download` and `archive` may write files. The cwd is always allowed. |
| `NO_COLOR` | unset | Any value (even empty) disables all ANSI color output. Wins over `FORCE_COLOR` and `CLICOLOR_FORCE`. See [no-color.org](https://no-color.org/). |
| `FORCE_COLOR` | unset | Any non-empty value enables color even when stdout is redirected. Overridden by `NO_COLOR`. |
| `CLICOLOR_FORCE` | unset | Any value other than `0` enables color even when stdout is redirected. Overridden by `NO_COLOR`. |

## Pluggable framework services

Abstractions for consumer-specific concerns. Each has a `Null` default that does nothing; consumers wire their own implementations when needed.

| Abstraction | Default | When to plug your own |
|---|---|---|
| `IKeyring` (`Vice.Configuration`) | `NullKeyring` | Secret storage backed by a platform keychain (libsecret, Keychain, Credential Manager) — Vice ships no implementation that touches disk |
| `IViceLogger` (`Vice.Logging`) | `NullLogger` | A custom logging sink |

Wiring these into the host is consumer-side. The framework doesn't auto-instantiate them.

## Outbound-connection allow/deny lists

`SafeOutboundConnection` (the SSRF defense wired into `HttpClient` via `SocketsHttpHandler.ConnectCallback`) refuses connections to private/loopback addresses by default. Override on a per-host or per-IP-range basis via env vars.

### Env vars

| Env var | Format | Effect |
|---|---|---|
| `VICE_SAFE_NET_ALLOW` | comma- / space- / semicolon-separated IPs or CIDRs (`127.0.0.0/8`, `::1`, `192.168.10.5`) | Bypass the `IsPrivateOrLocal` check for matching addresses. |
| `VICE_SAFE_NET_DENY` | same format as `VICE_SAFE_NET_ALLOW` | Refuse connections to any matching address (overrides allow). |
| `VICE_SAFE_NET_ALLOW_HOSTS` | comma-/space-/semicolon-separated hostnames; supports leading-`*.` wildcard (`localhost`, `*.example.com`) | Hostname is whitelisted pre-DNS; resolved IPs are not re-checked. |
| `VICE_SAFE_NET_DENY_HOSTS` | same format as `VICE_SAFE_NET_ALLOW_HOSTS` | Refuse the connection pre-DNS for matching hostnames. |

### Evaluation order

1. `deny_hosts` matches → refuse (no DNS).
2. `allow_hosts` matches → connect, skip IP checks.
3. DNS-resolve the host.
4. For each resolved IP: `deny` matches → refuse; `allow` matches → accept; otherwise the built-in `IsPrivateOrLocal` check applies.

Consumers can also set `SafeOutboundConnection.Policy` directly (for example, in test fixtures) to bypass the env-var / file path entirely.

## Pager wrapping

Commands that emit long output can opt into pager wrapping:

```csharp
await using var pager = Vice.Output.PagerSession.Open(ctx);
pager.Writer.WriteLine("lots of lines...");
```

`PagerSession.Open` returns a session that wraps stdout in `$PAGER` (default `less -R` when available) when:
- `ctx.NoPager` is false (i.e., `--no-pager` not set), AND
- stdout is interactive (not redirected to a file/pipe), AND
- `$PAGER` is set OR `less` is on `PATH`.

Otherwise it returns a disabled session whose `Writer` is `Console.Out`. The `using` pattern flushes and closes the pager process cleanly. Auto-wrapping every command's output is *not* in scope — opt-in only, by design.

## Plugins (trusted-directory discovery)

git-style: when `vice <verb>` is invoked and `<verb>` is not a registered command (and doesn't start with `-` / `--`), Vice looks for an executable named `vice-<verb>` in the trusted directory named by `$VICE_PLUGIN_DIR` and execs it with the remaining arguments. Exit code is forwarded.

`$PATH` is intentionally not consulted. On Unix the plugin file's permission mode must not be group- or world-writable, and the canonical resolved path (after symlink resolution) must remain inside the plugin directory.

```
$ cat > ~/.local/bin/vice-greet <<'EOF'
#!/bin/sh
echo "hello, $1"
EOF
$ chmod +x ~/.local/bin/vice-greet
$ vice greet world
hello, world
```

Built-in commands take priority over plugins of the same name; you can't shadow `vice list` by installing `vice-list`. Plugins inherit the same naming convention for every Vice consumer — for `chain-asm`, the plugin executable is `chain-asm-<verb>`.

Windows: requires `vice-<verb>.exe`. Plugin discovery is implemented but the cross-shell wrapping (e.g., `.cmd` shims) is the consumer's responsibility.

## Output format

`--format <value>` selects the output shape. The framework normalizes the value to lowercase and exposes it via `ctx.OutputFormat`; the conventional values:

| Value | `ctx.WantsJson` | Meaning |
|---|---|---|
| `auto` (default) | false | Render in the format most appropriate for the destination — table for TTY, lines for piped. |
| `text` | false | Plain text, no markup. |
| `json` | true | One JSON document — array of objects or single object as the command sees fit. |
| `jsonl` / `ndjson` | true | Newline-delimited JSON — one object per line, well-suited for streaming. |
| `hex` | false | Bytes as hex pairs. |

Commands opt in by checking `ctx.OutputFormat` or the convenience `ctx.WantsJson`. The framework doesn't auto-convert; each command is responsible for emitting its data in the requested format. Adoption is incremental.

## Behavior flags

Framework-level global options that any command can honor via `CommandContext`:

| Flag | `ctx` property | Convention |
|---|---|---|
| `--verbose` (`-v` reserved) | `ctx.Verbose` | Show extra diagnostic output (method type, request bodies, timing). |
| `--quiet` | `ctx.Quiet` | Suppress non-error output. Errors still print. |
| `--dry-run` | `ctx.DryRun` | Show what would happen without making changes (no file writes, no network mutations). |
| `--non-interactive` | `ctx.NonInteractive` | Refuse to prompt; fail fast if input is missing. CI-friendly. |
| `--no-pager` | `ctx.NoPager` | Suppress PAGER wrapping for long output. Commands that opt into pager behavior should consult this. |
| `--locale <bcp47>` | `ctx.Locale` | Applied to `CurrentCulture` and `CurrentUICulture` for the invocation. Affects all .NET formatting (dates, numbers, strings). Invalid tags log to stderr and continue with system default. |

Commands opt in by checking the property — the framework never auto-suppresses output or short-circuits behavior, so the contract is consistent across consumers. `--verbose` is already honored by every Vice.Net command; the others are framework-side ready and consumer commands can adopt them incrementally.

## Exit codes

Vice follows POSIX conventions so scripts can branch on outcome:

| Code | Constant | Meaning |
|---|---|---|
| `0` | `ViceExitCode.Success` | Command succeeded. |
| `1` | `ViceExitCode.Failure` | Generic runtime error (HTTP failure, socket error, unhandled exception, gRPC failure, etc.). |
| `2` | `ViceExitCode.UsageError` | Usage error — unknown command, bad argument, malformed input, unsupported shell. |
| `130` | `ViceExitCode.Interrupted` | Interrupted by SIGINT (single Ctrl+C). Conventional `128 + signal-number`. |

Handlers can return any int. Command-pack authors who construct a `ViceError` get the correct code automatically via `CommandErrorHandler.Handle` — `BadArgument` resolves to `2`, every other built-in error type resolves to `1`. Subclass `ViceError` and override `ExitCode` for custom codes.

## HTTP user agent

All outbound HTTP requests carry:

```
User-Agent: Vice/1.0 (+https://github.com/vice-cli)
```

This header is set on the research `HttpClient` in `src/Vice.Net/Requests/Research/ResearchHttp.cs` and applies to every research source.

## Daemon coordination

Session state lives in memory for the duration of the process. The IPC pipe name for daemon mode is `vice-session-<UserName>` (cross-process identifier; not a filesystem path).

## PoliteHandler

The HTTP handler wrapping every research-source request enforces:

| Knob | Default |
|---|---|
| Per-host minimum interval | 1 second between successive requests to the same hostname. |
| Retry count on `429 Too Many Requests` or `503 Service Unavailable` | 3 retries (so 4 total attempts). |
| Retry delay | Honors the upstream `Retry-After` header if present; otherwise exponential backoff of `2^(attempt+1)` seconds (2, 4, 8, ...). |

These are wired in `src/Vice.Net/Requests/Research/ResearchHttp.cs` with no environment override; tightening or loosening them requires a code change.
