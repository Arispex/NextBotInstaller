# Type Safety

> Type safety patterns in this project.

---

## Overview

This project is written in C# rather than TypeScript, so type safety comes from .NET language features and project configuration.

Current foundations:

- target framework: `.NET 9`
- implicit usings enabled
- nullable reference types enabled

Source references:

- `NextBotInstaller/NextBotInstaller.csproj:3-12`
- `NextBotInstaller/Program.cs:1310-1365`

---

## Type Organization

Types are currently kept close to the code that uses them.
There is no separate `types/` or `models/` folder.

Current patterns:

- small immutable data carriers are defined as `record` types near the bottom of `Program.cs`
- small mutable shared runtime state is defined as a sealed class near related helpers
- most helper methods return concrete built-in types or nullable results

Examples:

- immutable records: `NextBotInstaller/Program.cs:1357-1359`
- mutable runtime state class: `NextBotInstaller/Program.cs:1361-1365`
- nullable parse result: `NextBotInstaller/Program.cs:1310-1344`

Keep types local to the current file until reuse across multiple files becomes real.

---

## Validation

There is no separate validation library in the current codebase.
Validation is handled through:

- guard clauses,
- `Try*` methods that return `bool`,
- null checks after parsing,
- platform checks before taking OS-specific actions.

Examples:

- proxy URL normalization with `TryNormalizeProxyBaseUrl`: `NextBotInstaller/Program.cs:795-819`
- JSON field presence and kind checks in `ParseLatestReleaseMetadata`: `NextBotInstaller/Program.cs:1314-1339`
- OS checks in archive candidate selection: `NextBotInstaller/Program.cs:868-918`

---

## Common Patterns

- **Nullable-aware parsing**
  - parse methods may return `null` and callers must handle it.
  - example: `ParseLatestReleaseMetadata`: `NextBotInstaller/Program.cs:1310-1344`
- **Small immutable records for result bundles**
  - examples: `ArchivePlan`, `LatestReleaseMetadata`: `NextBotInstaller/Program.cs:1357-1359`
- **Case-insensitive collections for file and directory names**
  - examples: `NextBotUpdateProtectedDirectories`, `NextBotUpdateProtectedFiles`: `NextBotInstaller/Program.cs:33-46`
- **Explicit async method signatures**
  - examples: `RunWithStatusAsync`, `ResolveArchivePlanAsync`, `RunProcessAsync`: `NextBotInstaller/Program.cs:615-628`, `NextBotInstaller/Program.cs:839-862`, `NextBotInstaller/Program.cs:1116-1152`

---

## Forbidden Patterns

- Disabling nullable reference types.
- Using weakly typed containers where a record or explicit return type would be clearer.
- Ignoring `null` paths after parsing external data.
- Introducing generic abstractions that obscure the straightforward CLI workflow.

---

## Examples

- Nullable enabled in project config: `NextBotInstaller/NextBotInstaller.csproj:5-7`
- Nullable parse result handling: `NextBotInstaller/Program.cs:844-847`, `NextBotInstaller/Program.cs:1310-1344`
- Immutable records: `NextBotInstaller/Program.cs:1357-1359`
- Explicit typed helper methods: `NextBotInstaller/Program.cs:615-628`, `NextBotInstaller/Program.cs:839-862`
