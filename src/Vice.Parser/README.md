# Vice.Parser

The `Vice.Parser` assembly — the command-line lexer and resolver used by the Vice framework. Tokenizes a command string, extracts global options, and resolves against a registered chain descriptor set.

It ships as a standalone `Vice.Parser` package. The `Vice` host package references it, so framework consumers get it transitively; tools that only need lexing and resolution can reference `Vice.Parser` directly and pull in nothing else.

Requires .NET 10.

## What it is

- `Lexer` / `Token` — tokenization of fluent command strings
- `GlobalOptionExtractor` — separates `--flag` style options from positional tokens
- `CommandChain` / `CommandResolver` — matches a token stream against registered chains
- `ParseResult` / `MatchDiagnostic` — structured parse outcome with diagnostics
- `IChainDescriptor` / `ITargetDescriptor` — descriptor surface for host-defined commands

The parser types depend only on the .NET base class library, so they can be consumed without the rest of the Vice host runtime.

Apache 2.0.
