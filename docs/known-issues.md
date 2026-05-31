# Known issues

UX gaps where Vice exposes raw upstream behavior or leaves a behavior unsurfaced. Each is a candidate for a follow-up fix.

## Per-source query syntax leaks through

The query string passed to `search`/`archive` is forwarded verbatim to the upstream API after URL-encoding. arXiv accepts `search_query=all:<query>` semantics, PubMed accepts E-utilities `term` syntax with field tags (`smith[au]`, `cancer[mh]`), UniProt accepts its own field+boolean DSL (`gene:BRCA1 AND organism_id:9606`), and Gutendex accepts substring matches. Vice does not document these per-source dialects beyond a one-line note in [sources.md](sources.md) and does not validate the query before sending. The user has to know each upstream API to compose effective queries.
