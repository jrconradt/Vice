# Research commands

Four verbs over a registry of five sources (arXiv, Project Gutenberg, PubMed, UniProt, AlphaFold). All four take a `source` target identifying which backend to use. See [sources.md](sources.md) for per-source query syntax, aliases, and supported formats.

The default per-call timeout is 30 s. Results are cached on disk; see [env-and-config.md](env-and-config.md) for cache location and [--no-cache](#global-options) to bypass.

## search / find

Search a source for results matching a free-text query. In a piped pipeline, search emits results as a byte stream (one record per chunk); standalone, it formats them as a table.

### Synopsis

```
vice search "<query>" on source <name>
vice find "<query>" on source <name>
```

### Example

```bash
vice search "transformer" on source arxiv --limit 20
```

AlphaFold does not support search (no free-text index); use `fetch` or `download` with a UniProt accession instead.

## fetch / get

Retrieve one result by ID and print its title, metadata, and a text preview/abstract.

### Synopsis

```
vice fetch <id> from source <name>
vice get <id> from source <name>
```

### Example

```bash
vice fetch 2401.00001 from source arxiv
vice fetch P04637 from source uniprot
```

## download / dl

Download a result's content to a file. If `to path` is omitted, the file is written to the current directory using the source's default extension (PDF for arXiv, TXT for Gutenberg, XML for PubMed, FASTA for UniProt, PDB for AlphaFold). In session mode, downloads are queued as background jobs (`jobs` to list, `cancel <id>` to stop).

The destination must be inside the current working directory unless `VICE_ALLOWED_ROOTS` widens the allowed set.

### Synopsis

```
vice download <id> from source <name> to path <path>
vice dl <id> from source <name> to path <path>
vice download <id> from source <name>           # writes to ./<id>.<ext>
```

### Example

```bash
vice download 2401.00001 from source arxiv to path ./papers/
vice dl P04637 from source uniprot to path ./tp53.fasta
```

## archive

Run a search, then download every result to a directory. In session mode each download becomes its own background job. Out of session, downloads run sequentially with a progress display.

### Synopsis

```
vice archive "<query>" from source <name> to path <dir>
vice archive "<query>" from source <name>      # writes to ./<source>-archive/
```

### Example

```bash
vice archive "CRISPR review" from source pubmed to path ./crispr-reviews/ --limit 50
```

Requires the source to support both `search` and `download`. AlphaFold therefore cannot be archived (no search).

## Global options

| Option | Default | Applies to | Effect |
|---|---|---|---|
| `--limit <n>` | 10 | search, archive | Page size, clamped to 100. |
| `--offset <n>` | 0 | search, archive | Skip the first n results. |
| `--page <n>` | (none) | search, archive | Convenience for `--offset (n-1)*limit`. Ignored if `--offset` is also set. |
| `--format <fmt>` | source-specific | fetch, download, archive | Per-source format override (e.g. `epub` for Gutenberg, `cif` for AlphaFold). |
| `--timeout <ms>` | 30000 | all | Per-call timeout. |
| `--no-cache` | off | all | Bypass the on-disk research cache for this call. |
