# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 0.1.0 - 2026-05-25

Initial public release.

### Added

- `ViceError.Hint` virtual property with built-in remediation strings for `TimedOut`, `FileMissing`, `HttpFailure` (401/403/404/429/5xx), `SocketFailure`, and `Unhandled`. Rendered as a `hint:` line after the error message by `CommandErrorHandler`.
- Built-in cache verbs: `vice cache info`, `vice cache clear`, `vice cache clear source <name>` (with `--dry-run`).
- `ICommandRegistry.FindContaining(token)` powers `vice help <token>` fallback when no head verb matches.

### Changed

- `PoliteHandler` now logs `Info` on each 429/503 backoff and `Warn` when the retry budget is exhausted.
- `DotnetBuildQueue.GetOrStart` emits an `Info` log line when a request is deduped against an in-flight build.

### Fixed

- `DownloadJobRunner` now routes source-aware downloads through `ResearchSourceRegistry`; raw-URL downloads still resume via `ResumableHttpStream` (previously source-aware jobs could throw `UriFormatException` on arxiv-style IDs).
