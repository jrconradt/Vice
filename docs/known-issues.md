# Known issues

UX gaps where Vice exposes raw upstream behavior or leaves a behavior unsurfaced. Each is a candidate for a follow-up fix.

## Per-source query syntax leaks through

The query string passed to `search`/`archive` is forwarded verbatim to the upstream API after URL-encoding. arXiv accepts `search_query=all:<query>` semantics, PubMed accepts E-utilities `term` syntax with field tags (`smith[au]`, `cancer[mh]`), UniProt accepts its own field+boolean DSL (`gene:BRCA1 AND organism_id:9606`), and Gutendex accepts substring matches. Vice does not document these per-source dialects beyond a one-line note in [sources.md](sources.md) and does not validate the query before sending. The user has to know each upstream API to compose effective queries.

## Per-source cache listing and per-item invalidation

`vice cache info` reports per-source file counts and sizes; `vice cache clear` and `vice cache clear source <name>` purge in bulk. Listing individual cached IDs and invalidating a single item are not yet implemented.

## Resumable downloads only on raw-URL paths

`ResearchDownloadJobRunner` routes source-aware downloads (e.g., `download arxiv:2401.00001`) through `ResearchSourceRegistry`: it calls `source.ResolveDownloadAsync(...)` to obtain a target URI and then routes through `ResearchDownloader.DownloadToFileAsync`. That helper uses `ResumableHttpStream`, but it begins at `startOffset: 0` unless a sibling `.partial` file already exists, so a fresh source-aware download is functionally correct but non-resumable. Raw-URL downloads (`download https://example.com/file.bin`) reach the same `ResumableHttpStream` and resume via HEAD/Range probe. Closing the gap requires threading a resume offset from the resolved source target into `ResearchDownloader.DownloadToFileAsync`.
