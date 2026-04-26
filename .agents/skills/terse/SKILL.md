---
name: terse
description: Enforces highly concise, pragmatic, and fluff-free communication for code reviews.
user-invocable: false
---

# Output Formatting: Terse Mode

Communicate like a highly efficient, pragmatic engineer. Deliver actionable feedback using the minimum required tokens.

## Core Rules

* **Zero Fluff:** Strip all pleasantries, filler words, apologies, and conversational hedging (e.g., omit "It looks
  like", "I think", "Please consider").
* **Fragments over Sentences:** Use short bullet points, arrows (`→`), and sentence fragments.
* **Technical Exactness:** Retain 100% of technical precision. Enforce code spans (`code`) for all variables, methods,
  and classes.
* **Severity Tiers:** Prefix every issue with its impact tier: `[BLOCKER]`, `[ADVISORY]`, or `[NIT]` to enable rapid
  scannability.
* **Cite and Summarize:** State the violation, the required fix, and a strict citation. Include a 3–5 word technical
  rationale to prevent dead-ends if the referenced document is offline or out of context.

## Standardised Pattern

`[SEVERITY] [Location]: [Issue] → [Risk/Rationale]. Fix: [Action]. [Ref: Document Name]`

### Examples

**Bad (Standard LLM Output):**
> "It appears that on line 42 of the CustomerRepository, you are querying the database without using `.AsNoTracking()`.
> According to our Runtime Excellence standards, this could cause unnecessary memory overhead because the entities are
> being tracked. You should append `.AsNoTracking()` to the query to resolve this."

**Good (Terse Output):**

> `[BLOCKER] CustomerRepository:L42: Missing .AsNoTracking() → EF Core memory bloat/GC pressure. Fix: Append .AsNoTracking(). [Ref: Runtime Excellence Guide]`

**Bad:**
> "I noticed that the CreateUserCommand has public setters. In CQRS, commands should be immutable. Please make them
> private or use init-only properties."

**Good:**
> `[ADVISORY] CreateUserCommand: Public setters → Violates CQRS immutability. Fix: Use init properties. [Ref: ADR-015 Command Structures]`
