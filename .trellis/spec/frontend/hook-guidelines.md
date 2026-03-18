# Hook Guidelines

> How hook-like reusable stateful logic is handled in this project.

---

## Overview

This project does not use React or any custom hook system.
There are no `use*` hooks, no client-side data fetching hooks, and no hook naming conventions to follow.

Instead, reusable stateful logic is handled through:

- shared helper methods,
- small internal state objects,
- explicit workflow orchestration methods.

Relevant examples:

- shared status helper methods: `NextBotInstaller/Program.cs:615-628`
- proxy state mutation helper: `NextBotInstaller/Program.cs:832-836`
- shared mutable state type: `NextBotInstaller/Program.cs:1361-1365`

---

## Custom Hook Patterns

There are no custom hooks in the current repository.
If you need reusable logic, prefer one of these existing patterns instead:

1. **Pure helper methods** for deterministic transformations or lookups
   - examples: `GetCurrentOsName`: `NextBotInstaller/Program.cs:631-648`
   - `TryNormalizeProxyBaseUrl`: `NextBotInstaller/Program.cs:795-819`
2. **Workflow helpers with explicit async signatures** for reusable operational steps
   - examples: `ResolveArchivePlanAsync`: `NextBotInstaller/Program.cs:839-862`
   - `DownloadFileAsync`: `NextBotInstaller/Program.cs:968-977`
3. **Small shared state object plus setter helper** when state must be retained across menu actions
   - examples: `GithubProxyState`: `NextBotInstaller/Program.cs:1361-1365`
   - `ApplyProxyState`: `NextBotInstaller/Program.cs:832-836`

---

## Data Fetching

Data fetching is not a frontend concern in this project; it is performed directly inside the CLI workflow using `HttpClient`.

Current patterns:

- use a shared static `HttpClient`: `NextBotInstaller/Program.cs:49-50`, `NextBotInstaller/Program.cs:1347-1355`
- fetch remote release metadata with explicit parsing: `NextBotInstaller/Program.cs:839-847`, `NextBotInstaller/Program.cs:1310-1344`
- probe URLs with helper methods and fallbacks rather than inline networking code: `NextBotInstaller/Program.cs:923-965`

---

## Naming Conventions

Because there are no hooks, do not introduce `use*` names unless the project actually adopts a framework that requires them.

Follow current naming conventions instead:

- verb-based helper names,
- `Try*` prefixes for normalization or parsing that can fail without throwing,
- `Run*Async` for user workflows and operational async helpers.

Examples:

- `TryNormalizeProxyBaseUrl`: `NextBotInstaller/Program.cs:795-819`
- `RunOneClickInstallAsync`: `NextBotInstaller/Program.cs:141-232`
- `RunProcessWithLiveOutputAsync`: `NextBotInstaller/Program.cs:1154-1189`

---

## Common Mistakes

- Writing guidance as if the project already used React hooks.
- Introducing hook terminology when a helper method is the real existing pattern.
- Hiding mutable shared state across many methods instead of keeping it explicit in a small state object.
- Duplicating async workflow logic that should be centralized in a helper.

---

## Examples

- Shared async status wrappers: `NextBotInstaller/Program.cs:615-628`
- Shared proxy state and updater: `NextBotInstaller/Program.cs:832-836`, `NextBotInstaller/Program.cs:1361-1365`
- Reusable network helpers: `NextBotInstaller/Program.cs:839-977`
