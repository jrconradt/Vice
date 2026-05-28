# Research sources

Vice ships with five research sources, each backed by an upstream HTTP API. All five share a single `HttpClient` whose handler enforces a per-host minimum interval and retry policy ([env-and-config.md#politehandler](env-and-config.md#politehandler) for defaults).

| Source | Aliases | Query syntax | Rate-limit policy | Best for |
|---|---|---|---|---|
| `arxiv` | — | arXiv API `search_query=all:<query>` — full-text across abstract, title, author | Per upstream policy; PoliteHandler enforces a 1 s/host minimum and 3 retries on 429/503 | Physics, math, CS preprints; the `<id>` is the arXiv identifier (e.g. `2401.00001`) |
| `gutenberg` | `gutenberg.org` | Gutendex `search=<query>` — title/author substring | Per upstream policy; PoliteHandler enforces a 1 s/host minimum and 3 retries on 429/503 | Public-domain literature; the `<id>` is the Gutenberg book number |
| `pubmed` | `pmc` | NCBI E-utilities `esearch` term — MeSH terms and field tags supported (e.g. `cancer[mh]`, `smith[au]`) | Per upstream policy; PoliteHandler enforces a 1 s/host minimum and 3 retries on 429/503 | Biomedical literature; the `<id>` is the PMID |
| `uniprot` | — | UniProt REST `query=<expr>` — supports field syntax (`gene:BRCA1`, `organism_id:9606`) and boolean operators | Per upstream policy; PoliteHandler enforces a 1 s/host minimum and 3 retries on 429/503 | Protein sequences/metadata; the `<id>` is the UniProt accession (e.g. `P04637`) |
| `alphafold` | `af` | **Not searchable.** Fetch and download only. | Per upstream policy; PoliteHandler enforces a 1 s/host minimum and 3 retries on 429/503 | Predicted protein structures; the `<id>` is a UniProt accession |

Per-source notes:

- **arXiv** — `download` retrieves the PDF from `https://arxiv.org/pdf/<id>`. `fetch` returns title/authors/categories/abstract/comment as text.
- **Gutenberg** — `--format epub` or `--format html` change the download target; default is plain text. `fetch` previews the first 2000 chars of the text file.
- **PubMed** — `download` returns the full XML; `fetch` returns title, authors, abstract, journal, date, and MeSH headings. Search may return fewer results than the page size when E-utilities thins them.
- **UniProt** — `--format json` / `--format xml` / `--format gff` change download target; default is FASTA. `fetch` returns function annotation and the FASTA sequence inline.
- **AlphaFold** — `--format cif`, `--format mmcif`, `--format bcif`, or `--format pae` change download target; default is PDB. `fetch` returns model metadata and the available structure URLs.

The query string passed to `search`/`archive` is **forwarded verbatim** to the upstream URL after `WebUtility.UrlEncode`. The syntax it expects is the upstream API's — Vice does not normalise across sources.
