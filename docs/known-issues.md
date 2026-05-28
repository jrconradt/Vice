# Known issues

UX gaps where Vice exposes raw upstream behavior or leaves a behavior unsurfaced. Each is a candidate for a follow-up fix.

## Per-source query syntax leaks through

The query string passed to `search`/`archive` is forwarded verbatim to the upstream API after URL-encoding. arXiv accepts `search_query=all:<query>` semantics, PubMed accepts E-utilities `term` syntax with field tags (`smith[au]`, `cancer[mh]`), UniProt accepts its own field+boolean DSL (`gene:BRCA1 AND organism_id:9606`), and Gutendex accepts substring matches. Vice does not document these per-source dialects beyond a one-line note in [sources.md](sources.md) and does not validate the query before sending. The user has to know each upstream API to compose effective queries.

## Per-source cache listing and per-item invalidation

`vice cache info` reports per-source file counts and sizes; `vice cache clear` and `vice cache clear source <name>` purge in bulk. Listing individual cached IDs and invalidating a single item are not yet implemented.

## Resumable downloads only on raw-URL paths

`DownloadJobRunner` routes source-aware downloads (e.g., `download arxiv:2401.00001`) through `ResearchSourceRegistry`, which calls each source's `DownloadAsync` directly. That path is functionally correct but non-resumable. Raw-URL downloads (`download https://example.com/file.bin`) still go through `ResumableHttpStream` and resume via HEAD/Range probe. Closing the gap requires extending `IResearchSource` with a resume-offset parameter.
