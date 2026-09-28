# Markdown extraction

Markdown uses Markdig to create a document root and heading-level chunks. Select files, directories, or globs explicitly in a workspace, for example `--markdown 'docs/**/*.md'`. Named workspaces do not add an implicit documentation selection.

## Nodes and links

Each selected document is read through the filesystem boundary and parsed into stable, repository-relative canonical identities. Heading slugs distinguish chunks; source spans and text support context display and search.

The extractor emits structural `ParentOf` edges for heading hierarchy and `DocumentLink` edges for resolvable local links. Standard Markdown links resolve relative to the source file. Wiki links support `[[page]]`, `[[page#heading]]`, and `[[page|alias]]`; bare targets inside `docs/wiki/` resolve from the wiki root, while `./` and `../` stay relative to the page.

Public documentation uses relative Markdown links so navigation also works on GitHub. Links to unselected documents cannot provide an indexed target node.

## Watching

All selected Markdown inputs are combined into one extraction pass. Relevant watch changes reconcile the selected document graph, including incoming links when a target appears, disappears, or changes headings. Canonical identities that survive a rewrite retain their persisted node handles.

Heading changes or removed selections can remove nodes and their attached memories. See [memory lifecycle](../cli/memory-command.md), [watch reconciliation](../architecture/incremental-watch.md), and [SQLite persistence](../persistence/sqlite-schema.md).
