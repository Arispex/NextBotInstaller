# Logging Guidelines

> How logging is done in this project.

---

## Overview

This project does not currently use a structured logging library.
Operational feedback is written directly to the terminal through `Spectre.Console`.

Current observability patterns are:

- status spinners for long-running steps,
- colored success / warning / failure messages,
- summary panels for completed workflows,
- preserved command output when subprocesses fail.

Representative examples:

- shared status helper: `NextBotInstaller/Program.cs:615-628`
- workflow step messages: `NextBotInstaller/Program.cs:153-211`
- warning with fallback detail: `NextBotInstaller/Program.cs:1045-1050`
- process failure detail: `NextBotInstaller/Program.cs:1146-1150`

If the project later grows into a background service or HTTP API, this guideline should be replaced with real structured logging guidance.

---

## Log Levels

There are no formal log levels implemented today, but the console output has clear equivalents:

- **Informational / progress**
  - step banners, status messages, environment summaries
  - examples: `NextBotInstaller/Program.cs:143-145`, `NextBotInstaller/Program.cs:155-210`, `NextBotInstaller/Program.cs:586-589`
- **Warning / non-fatal issue**
  - user cancellation, disabled proxy, fallback behavior
  - examples: `NextBotInstaller/Program.cs:172`, `NextBotInstaller/Program.cs:776`, `NextBotInstaller/Program.cs:1048-1049`
- **Error / failed action**
  - top-level failed workflow output with the original reason
  - examples: `NextBotInstaller/Program.cs:77-78`, `NextBotInstaller/Program.cs:88-89`, `NextBotInstaller/Program.cs:99-100`
- **Success / completion**
  - completion panels and final hints
  - examples: `NextBotInstaller/Program.cs:223-231`

---

## Structured Logging

Structured logging is **not** part of the current codebase.

Current conventions instead:

- Use helper methods to keep progress output consistent.
- Escape dynamic values before writing Spectre markup.
- Keep raw error details intact instead of rewriting them into vague summaries.
- Buffer subprocess output and include it in thrown exceptions when execution fails.

Examples:

- escaped dynamic values in UI: `NextBotInstaller/Program.cs:586-589`, `NextBotInstaller/Program.cs:707-709`
- shared status output helper: `NextBotInstaller/Program.cs:615-628`
- buffered subprocess output: `NextBotInstaller/Program.cs:1139-1150`, `NextBotInstaller/Program.cs:1177-1187`

---

## What to Log

In the current CLI model, make terminal output visible for:

- start and completion of major workflow steps,
- important selected paths and runtime context,
- fallback behavior that changes execution strategy,
- command failures with enough detail to troubleshoot.

Concrete examples:

- selected OS, architecture, and current directory on startup: `NextBotInstaller/Program.cs:583-595`
- Python executable path after installation: `NextBotInstaller/Program.cs:195-197`
- tar fallback warning with captured details: `NextBotInstaller/Program.cs:1045-1050`
- failed subprocess exit code and output: `NextBotInstaller/Program.cs:1146-1150`

---

## What NOT to Log

Avoid printing or persisting sensitive or noisy data such as:

- contents of `.env` or other credentials,
- full secrets or tokens if future integrations add them,
- unnecessary repeated status spam inside tight loops,
- reformatted error text that hides the original reason.

Keep in mind that the installer explicitly preserves `.env` and other user data files during updates: `NextBotInstaller/Program.cs:38-46`.
Those files should be treated as sensitive operational data.

---

## Common Mistakes

- Treating ad-hoc `AnsiConsole` messages as if they were a full logging system.
- Printing dynamic strings without `Markup.Escape`.
- Hiding failure details behind generic “something went wrong” text.
- Logging sensitive user file contents while troubleshooting.

---

## Examples

- Shared progress helper: `NextBotInstaller/Program.cs:615-628`
- Warning on extraction fallback: `NextBotInstaller/Program.cs:1045-1050`
- Rich process failure details: `NextBotInstaller/Program.cs:1146-1150`
