# Research commands

Four verbs over a registry of five sources (arXiv, Project Gutenberg, PubMed, UniProt, AlphaFold). All four take a `source` target identifying which backend to use.

The default per-call timeout is 30 s.

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

## Data licensing and attribution

Content retrieved by `fetch`, `download`, and `archive` is owned by the upstream source, not by Vice. Apache-2.0 covers the Vice tool only; it grants you no rights over downloaded data. Each source sets its own terms, and reuse or redistribution carries obligations that travel with the bytes you write to disk.

| Source | Terms | Your obligation on reuse / redistribution |
|---|---|---|
| arXiv | Per-submission license, set by each paper's author (arXiv non-exclusive, CC-BY, CC-BY-SA, CC-BY-NC, CC0, or "arXiv perpetual" with no redistribution right). | Check the license recorded on each paper's abstract page. Many submissions are **not** freely redistributable; do not assume you may republish a downloaded PDF. |
| Project Gutenberg | Ebook texts are public domain in the United States. The name "Project Gutenberg" is a registered trademark. | The text is free to reuse in the US; verify your own jurisdiction. If you redistribute with the Project Gutenberg name or header attached, you accept the trademark terms in the Project Gutenberg License — strip the trademark and header to redistribute without those conditions. |
| PubMed | NLM/NCBI terms. Records and abstracts are generally usable, but some abstracts carry the publisher's copyright. | Follow the NLM data usage policy. Do not assume an abstract is public domain; attribution to NLM and the originating publisher is expected. |
| UniProt | CC-BY 4.0. | Attribution required. Credit UniProt and link the license (https://creativecommons.org/licenses/by/4.0/) wherever you redistribute or build on the data. |
| AlphaFold | CC-BY 4.0 (DeepMind / EMBL-EBI). | Attribution required. Credit AlphaFold DB and link the CC-BY 4.0 license wherever you redistribute or build on the structures. |

License URLs: arXiv https://arxiv.org/help/license, Project Gutenberg https://www.gutenberg.org/policy/license.html, PubMed/NLM https://www.nlm.nih.gov/web_policies.html, UniProt https://www.uniprot.org/help/license, AlphaFold https://alphafold.ebi.ac.uk/faq.

These obligations are yours to discharge; Vice does not clear rights on your behalf and does not strip or rewrite upstream license metadata in the files it saves.
