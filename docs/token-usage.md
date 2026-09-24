# Evaluating token usage

SharpSense returns bounded graph context and reduced command output so an agent can request relevant information in smaller steps. Actual token usage depends on the model, repository, task, tool calls, and output limits.

There is currently no controlled benchmark for the version 1 workspace workflow. Earlier ad hoc transcripts mixed cached input, output, different traversal depths, and different model runs; they do not establish a reproducible percentage saving.

## What to measure

Compare the same task, repository revision, selected sources, model, prompt, and success criteria with and without SharpSense. Record:

- Input, cached-input, and output tokens separately, using the model provider's reported usage.
- Tool-call count, response sizes, elapsed time, and whether the answer is correct.
- SharpSense version, workspace source selection, embedding availability, and whether indexing time is included.
- Search limits, trace direction/depth, context limits, and whether memories are included.
- Repeated runs, including failures and tasks where graph navigation does not help.

Keep each run's configuration and anonymized transcript with its measurements. Do not compare a cold-cache baseline against a warm-cache run without reporting that difference.

## A repeatable workflow

1. Create a [workspace](wiki/cli/workspace-command.md) with explicit C#, TypeScript, and/or Markdown sources.
2. Run [analyze](wiki/cli/analyze-command.md), then [doctor](wiki/cli/doctor-command.md) to record graph coverage and indexing diagnostics.
3. Give both runs the same concrete task and acceptance criteria.
4. In the SharpSense run, use [search](wiki/cli/search-command.md) to identify nodes, [context](wiki/cli/context-command.md) for immediate relationships, and [trace](wiki/cli/trace-command.md) only where necessary.
5. Use [windowed execution](wiki/architecture/windowed-execution-pipeline.md) for large build or test output, with queries that expose relevant failures.
6. Compare correctness before comparing token totals.

Reduced output is not proof of complete coverage: TypeScript relationships are syntax-based, traces have bounds, and command reduction returns matching excerpts rather than the full transcript. Inspect source or rerun a command when the result leaves a material question unresolved.
