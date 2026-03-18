# Error Handling

> How errors are handled in this project.

---

## Overview

This project uses a simple CLI-oriented error handling model:

- Helper methods throw built-in exceptions when they cannot continue.
- Top-level user actions catch `Exception` and show a user-facing failure message with the original reason.
- There is no custom exception hierarchy today.

This is visible in the main menu dispatch:

- Install flow: `NextBotInstaller/Program.cs:70-78`
- Update flow: `NextBotInstaller/Program.cs:81-89`
- NapCat flow: `NextBotInstaller/Program.cs:92-100`

When a failure reaches the menu boundary, the console message keeps the raw `ex.Message` so the user sees the actual reason.

---

## Error Types

The current codebase relies on standard .NET exception types:

- `InvalidOperationException` for invalid runtime state or failed commands
  - missing Python environment: `NextBotInstaller/Program.cs:245-247`
  - failed release metadata parse: `NextBotInstaller/Program.cs:844-847`
  - failed process execution: `NextBotInstaller/Program.cs:1146-1150`
- `FileNotFoundException` when expected extracted files are missing
  - Python executable not found: `NextBotInstaller/Program.cs:1077-1080`
- `NotSupportedException` for unsupported formats or modes
  - unsupported archive: `NextBotInstaller/Program.cs:1009-1010`
  - unsupported NapCat install mode: `NextBotInstaller/Program.cs:1257-1260`, `NextBotInstaller/Program.cs:1280-1283`

Do not introduce custom exception classes unless the project develops repeated error semantics that built-in types can no longer express clearly.

---

## Error Handling Patterns

### 1. Throw near the failing operation

Helper methods fail fast when a required condition is not met.
Examples:

- Missing `python` directory before update: `NextBotInstaller/Program.cs:245-247`
- Invalid metadata from remote release JSON: `NextBotInstaller/Program.cs:844-847`
- Unsupported archive format: `NextBotInstaller/Program.cs:1009-1010`

### 2. Catch at the user-action boundary

The menu action handlers catch exceptions once and convert them into terminal output.
Examples:

- `安装失败：{ex.Message}`: `NextBotInstaller/Program.cs:75-78`
- `更新失败：{ex.Message}`: `NextBotInstaller/Program.cs:86-89`
- `NapCat 安装失败：{ex.Message}`: `NextBotInstaller/Program.cs:97-100`

### 3. Preserve actionable failure details

When running external processes, the command preview, exit code, and buffered output are included in the thrown error.
Example:

- `RunProcessAsync`: `NextBotInstaller/Program.cs:1146-1150`
- `RunProcessWithLiveOutputAsync`: `NextBotInstaller/Program.cs:1183-1187`

### 4. Use non-exception early returns for user cancellations

User cancellation is not treated as an exceptional failure.
Examples:

- Cancel install overwrite: `NextBotInstaller/Program.cs:167-173`
- Disable proxy and continue: `NextBotInstaller/Program.cs:774-776`

---

## API Error Responses

This repository has no HTTP API layer, so there is no API error response schema to follow.

Equivalent runtime patterns are:

- return `bool` when failure is expected and recoverable during probing,
- return `null` when parsing fails and the caller can decide what to do,
- throw exceptions when the workflow cannot continue.

Examples:

- URL reachability probe returns `bool`: `NextBotInstaller/Program.cs:934-965`
- release metadata parse returns nullable result: `NextBotInstaller/Program.cs:1310-1344`
- workflow helpers throw when continuing would be unsafe: `NextBotInstaller/Program.cs:844-860`, `NextBotInstaller/Program.cs:1146-1150`

---

## Common Mistakes

- Swallowing exceptions without preserving the real reason.
- Converting actionable process failures into generic messages.
- Throwing for normal user cancellations, which should usually return early with a yellow informational message.
- Adding custom exception types before the project actually needs them.
- Catching broad exceptions inside helpers unless the method is intentionally probing or falling back, such as `IsUrlReachableAsync`: `NextBotInstaller/Program.cs:936-964`

---

## Examples

- Top-level catch-and-report flow: `NextBotInstaller/Program.cs:70-100`
- Guard clause for missing runtime prerequisites: `NextBotInstaller/Program.cs:245-247`
- Process failure preserves exit code and output: `NextBotInstaller/Program.cs:1146-1150`
