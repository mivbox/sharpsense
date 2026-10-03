---
name: ef-query-work
description: "Review or change EF Core queries and writes: SQL shape, transactions, tracking and provider-specific behavior."
---

# EF query work

Inspect the affected context registration, database provider, repository/handler boundary and existing tests.
Follow the established tracking and transaction model before changing the query.

## Preserve correctness

- Keep a context local to its unit of work and do not share it across concurrent operations.
- Preserve established transaction ownership. Related writes that form one operation must retain their atomicity;
  publish dependent cache or notification state at the point required by the existing commit contract.
- Preserve keys, relationships, concurrency tokens and delete behavior. Check what depends on an identity before
  replacing a row or rebuilding a collection.
- Use parameterised SQL and pass cancellation. Keep provider-specific query choices deliberate rather than
  transferring assumptions from a different database or EF version.

## Control query cost

- Project the fields the caller needs and apply filtering, ordering and paging before materialisation.
- Use the context's tracking baseline deliberately. Read-only entity queries normally need no tracking; mutation
  queries need the appropriate tracked state or an explicit update strategy.
- Batch repeated lookups and assess collection joins for row multiplication. Use split queries only when the SQL
  shape and consistency requirements justify them.
- Preserve deterministic pagination and its scope/concurrency contract. Check parameter limits and the provider's
  collection translation when passing large identifier sets.
- Measure a suspected bottleneck before adding compiled queries, caching, pooling or broader relationship loading.

Validate the changed invariant with the existing real-provider fixture: query results, update/rollback behavior,
identity preservation, concurrency or pagination. Use representative data for a performance claim and compare the
same operation before and after. Preserve historical migration files during ordinary cleanup; intentional schema
changes need the repository's migration checks.
