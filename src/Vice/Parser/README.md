# Vice.Parser

Standalone command-line lexer and resolver used by the Vice framework. Tokenizes a command string, extracts global options, and resolves against a registered chain descriptor set.

## Install

```bash
dotnet add package Vice.Parser
```

Requires .NET 10.

## What it is

- `Lexer` / `Token` — tokenization of fluent command strings
- `GlobalOptionExtractor` — separates `--flag` style options from positional tokens
- `CommandChain` / `CommandResolver` — matches a token stream against registered chains
- `ParseResult` / `MatchDiagnostic` — structured parse outcome with diagnostics
- `IChainDescriptor` / `ITargetDescriptor` — descriptor surface for host-defined commands

Usable independently of the Vice host runtime.

Apache 2.0.
