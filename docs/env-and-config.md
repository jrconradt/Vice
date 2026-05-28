# Environment and configuration

## Environment variables

| Variable | Default | Effect |
|---|---|---|
| `VICE_LOG_LEVEL` | `warn` | Logger threshold. Accepted values: `trace`, `debug`, `info`, `warn` (or `warning`), `error`. Anything else falls back to `warn`. Set to `debug` to see stack traces on REPL handler exceptions. |
| `VICE_ALLOWED_ROOTS` | (empty) | `:`-separated list of additional directories outside the current working directory where `download` and `archive` may write files. The cwd is always allowed. |
| `VICE_CONFIG_HOME` | XDG default | Override the directory holding `config.json`. Takes precedence over `XDG_CONFIG_HOME`. |
| `VICE_DATA_HOME` | XDG default | Override the directory holding `jobs.json` and any other persistent data. Takes precedence over `XDG_DATA_HOME`. |
| `VICE_CACHE_HOME` | XDG default | Override the directory holding the research cache. Takes precedence over `XDG_CACHE_HOME`. |
| `VICE_STATE_HOME` | XDG default | Override the directory holding `history` and other session state. Takes precedence over `XDG_STATE_HOME`. |
| `VICE_RUNTIME_DIR` | `XDG_RUNTIME_DIR` if set | Override the directory used for non-persistent runtime files (daemon coordination). Takes precedence over `XDG_RUNTIME_DIR`. |
| `NO_COLOR` | unset | Any value (even empty) disables all ANSI color output. Wins over `FORCE_COLOR` and `CLICOLOR_FORCE`. See [no-color.org](https://no-color.org/). |
| `FORCE_COLOR` | unset | Any non-empty value enables color even when stdout is redirected. Overridden by `NO_COLOR`. |
| `CLICOLOR_FORCE` | unset | Any value other than `0` enables color even when stdout is redirected. Overridden by `NO_COLOR`. |

## Pluggable framework services

Three abstractions for consumer-specific concerns. Each has a `Null` default that does nothing; consumers wire their own implementations when needed.

| Abstraction | Default | Provided alternative | When to plug your own |
|---|---|---|---|
| `IKeyring` (`Vice.Configuration`) | `NullKeyring` | `FileKeyring` — plaintext JSON at `$XDG_DATA_HOME/<app>/keyring.json`; dev/local-only, refuses to construct unless `VICE_ALLOW_PLAINTEXT_KEYRING=1` | Production secret storage needs platform keychain (libsecret, Keychain, Credential Manager) — out of framework scope |
| `IUpdateChecker` (`Vice.Configuration`) | `NullUpdateChecker` | (none) | Wire your own to poll NuGet, GitHub releases, or a custom feed; framework intentionally doesn't pick a feed |
| Telemetry sink (`Vice.Logging`) | `NullTelemetrySink` | `FileTelemetrySink` — JSONL at `$XDG_STATE_HOME/<app>/telemetry.jsonl`; no-ops unless `VICE_TELEMETRY_CONSENT=1` is set | Real analytics backend (PostHog, Sentry, custom). MUST surface user consent before enabling. |

Wiring these into the host is consumer-side. The framework doesn't auto-instantiate them.

### Opt-in env vars (production safety)

| Env var | Default | Effect |
|---|---|---|
| `VICE_ALLOW_PLAINTEXT_KEYRING` | unset | `FileKeyring` constructor throws unless this is `1` / `true` / `yes` / `on`. Forces the consumer to acknowledge the plaintext-on-disk model before using it. |
| `VICE_TELEMETRY_CONSENT` | unset | `FileTelemetrySink.Track`/`TrackException` silently no-op unless this is `1` / `true` / `yes` / `on`. No data is written to disk without explicit consent. |

## Outbound-connection allow/deny lists

`SafeOutboundConnection` (the SSRF defense wired into `HttpClient` via `SocketsHttpHandler.ConnectCallback`) refuses connections to private/loopback addresses by default. Override on a per-host or per-IP-range basis via env vars **or** a settings file. Both sources are combined; entries in either source contribute.

### Env vars

| Env var | Format | Effect |
|---|---|---|
| `VICE_SAFE_NET_ALLOW` | comma- / space- / semicolon-separated IPs or CIDRs (`127.0.0.0/8`, `::1`, `192.168.10.5`) | Bypass the `IsPrivateOrLocal` check for matching addresses. |
| `VICE_SAFE_NET_DENY` | same format as `VICE_SAFE_NET_ALLOW` | Refuse connections to any matching address (overrides allow). |
| `VICE_SAFE_NET_ALLOW_HOSTS` | comma-/space-/semicolon-separated hostnames; supports leading-`*.` wildcard (`localhost`, `*.example.com`) | Hostname is whitelisted pre-DNS; resolved IPs are not re-checked. |
| `VICE_SAFE_NET_DENY_HOSTS` | same format as `VICE_SAFE_NET_ALLOW_HOSTS` | Refuse the connection pre-DNS for matching hostnames. |

### Settings file

`$VICE_CONFIG_HOME/vice/safenet.json` (falls back to `$XDG_CONFIG_HOME/vice/safenet.json` or `~/.config/vice/safenet.json`):

```json
{
  "allow": ["127.0.0.0/8", "::1"],
  "deny": ["10.42.0.0/16"],
  "allow_hosts": ["localhost", "*.internal.example.com"],
  "deny_hosts": ["169.254.169.254"]
}
```

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

git-style: when `vice <verb>` is invoked and `<verb>` is not a registered command (and doesn't start with `-` / `--`), Vice looks for an executable named `vice-<verb>` in a single trusted directory and execs it with the remaining arguments. Exit code is forwarded.

Discovery order:
1. `$VICE_PLUGIN_DIR` if set.
2. Otherwise `$XDG_DATA_HOME/vice/plugins/` (or `~/.local/share/vice/plugins/` when `XDG_DATA_HOME` is unset).

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
| `--force` | `ctx.Force` | Skip confirmation prompts and overwrite checks. |
| `--non-interactive` | `ctx.NonInteractive` | Refuse to prompt; fail fast if input is missing. CI-friendly. |
| `--no-pager` | `ctx.NoPager` | Suppress PAGER wrapping for long output. Commands that opt into pager behavior should consult this. |
| `--clipboard` | `ctx.Clipboard` | Copy primary command output to the system clipboard. Commands opt in by emitting through `ctx.Clipboard ? clipboard : console`. |
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

This header is set on the shared `HttpClient` and applies to every research source.

## State directories (XDG-compliant)

Vice files are split across the standard XDG directories. Each kind can be overridden independently. Path-resolution precedence for any kind: ctor override → `VICE_<KIND>_HOME` (or `VICE_RUNTIME_DIR` for the runtime kind) → `XDG_<KIND>_HOME` (or `XDG_RUNTIME_DIR`) → platform default.

| File | Default location | XDG kind |
|---|---|---|
| `history` | `$XDG_STATE_HOME/vice/history` (defaults to `~/.local/state/vice/history`) | state |
| `jobs.json` | `$XDG_DATA_HOME/vice/jobs.json` (defaults to `~/.local/share/vice/jobs.json`) | data |
| `config.json` | `$XDG_CONFIG_HOME/vice/config.json` (defaults to `~/.config/vice/config.json`) | config |
| research cache | `$XDG_CACHE_HOME/vice/research/` (defaults to `~/.cache/vice/research/`) | cache |
| daemon coordination | `$XDG_RUNTIME_DIR/vice/` if set, otherwise named pipe only | runtime |

`history` is capped at 1000 lines; oldest entries evicted on rewrite. `jobs.json` is reloaded across sessions and daemon restarts. `config.json` is written by `vice> set <key> to <value>` in session mode.

The IPC pipe name for daemon mode is `vice-session-<UserName>` (cross-process identifier; not a filesystem path).

### Backward compatibility with `~/.vice/`

If a legacy `~/.vice/<file>` exists and the corresponding modern path does not, Vice reads the legacy location automatically. Writes always go to the modern XDG path. To complete a migration, move the legacy files into their new homes manually:

```bash
mkdir -p ~/.local/state/vice ~/.local/share/vice ~/.config/vice
mv ~/.vice/history     ~/.local/state/vice/history
mv ~/.vice/jobs.json   ~/.local/share/vice/jobs.json
mv ~/.vice/config.json ~/.config/vice/config.json
rmdir ~/.vice
```

## Research cache

Per-source on-disk cache for search results and downloaded content lives under the XDG cache home (see table above): `$XDG_CACHE_HOME/vice/research/`, defaulting to `~/.cache/vice/research/` on Linux/macOS and `%LOCALAPPDATA%\vice\research\` on Windows. Override with `VICE_CACHE_HOME`.

Layout under the cache root:

```
<source>/search/<sha256-of-query+limit+offset+format>.json    # 1 hour TTL
<source>/content/<sanitized-id>.<ext>                          # no TTL
```

Search-result caching has a 1 hour TTL; downloaded content has no expiry and is reused on subsequent `download`/`archive` calls for the same `(source, id, format)`. Pass `--no-cache` on any research verb to bypass; there is no built-in invalidation command — delete the cache directory by hand.

## PoliteHandler

The HTTP handler wrapping every research-source request enforces:

| Knob | Default |
|---|---|
| Per-host minimum interval | 1 second between successive requests to the same hostname. |
| Retry count on `429 Too Many Requests` or `503 Service Unavailable` | 3 retries (so 4 total attempts). |
| Retry delay | Honors the upstream `Retry-After` header if present; otherwise exponential backoff of `2^(attempt+1)` seconds (2, 4, 8, ...). |

These are wired in `src/Vice.Net/Program.cs` with no environment override; tightening or loosening them requires a code change.
