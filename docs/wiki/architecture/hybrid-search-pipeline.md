# Hybrid search

`HybridSearcher` combines lexical and semantic ranking inside SQLite. The application `HybridSearchQuery` carries the query text, result limit, and supported filters; CLI, MCP, and UI adapters expose their respective options.

## Query flow

1. Validate the query and resolve an optional project filter.
2. Ask `IKeywordCandidateProvider` for a safe FTS match expression.
3. Generate an embedding for the query text.
4. Rank keyword candidates with FTS5 BM25 and vector candidates with sqlite-vec cosine distance.
5. Combine ranks with Reciprocal Rank Fusion and hydrate only the selected result nodes.

The implementation bounds candidate pools and uses an RRF constant of 60. Keyword and vector ranking are composed in SQL.

Code-node `SearchText` incorporates symbol and documentation context. `VectorEmbedding` stores reusable vectors generated during analysis. Nodes without vectors can still participate in the lexical side, but search itself still requires the query-embedding runtime.

## Memories and isolation

Memory relevance is opt-in. When enabled, matching memory content, tags, and vectors can promote their owning node; supported filters are applied during ranking. Memory results retain the owning workspace's node identity.

The search service and database factory are scoped to the selected workspace. Each UI workspace also has an isolated client/query cache, preventing equal numeric IDs in different databases from sharing result state.

See [search usage](../cli/search-command.md), [memories](../cli/memory-command.md), and [SQLite schema](../persistence/sqlite-schema.md).
