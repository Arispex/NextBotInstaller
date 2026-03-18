# Quality Guidelines

> Code quality standards for backend-like development in this project.

---

## Overview

This repository is a small .NET 9 CLI installer, so quality standards are centered on:

- keeping cross-platform workflows reliable,
- preserving actionable user feedback,
- reusing helper methods instead of duplicating shell / file / network logic,
- staying compatible with Native AOT publishing in CI.

Relevant project facts:

- `.NET 9`, implicit usings, nullable enabled: `NextBotInstaller/NextBotInstaller.csproj:3-12`
- Native AOT publish matrix in CI: `.github/workflows/release.yml:39-103`
- single maintained source module with grouped helpers: `NextBotInstaller/Program.cs:12-1368`

---

## Forbidden Patterns

- **Do not edit generated or bundled runtime output** under `bin/`, `obj/`, embedded Python runtimes, or vendored package directories. Source changes belong in the maintained project files.
- **Do not duplicate subprocess setup inline** when `RunProcessAsync` or `RunProcessWithLiveOutputAsync` already matches the need: `NextBotInstaller/Program.cs:1116-1189`
- **Do not concatenate shell commands into one string** when `ProcessStartInfo.ArgumentList` is available: `NextBotInstaller/Program.cs:1128-1131`, `NextBotInstaller/Program.cs:1166-1169`
- **Do not write unescaped dynamic Spectre markup**. Use `Markup.Escape` for paths, URLs, and runtime values: `NextBotInstaller/Program.cs:586-589`, `NextBotInstaller/Program.cs:739`, `NextBotInstaller/Program.cs:757`
- **Do not introduce fake architecture layers** such as repositories or services unless the codebase actually grows beyond the current single-file workflow structure.

---

## Required Patterns

- **Keep nullable reference types enabled** and follow the current explicit-null patterns: `NextBotInstaller/NextBotInstaller.csproj:5-7`, `NextBotInstaller/Program.cs:1310-1344`
- **Reuse shared helpers** for status, downloads, extraction, process execution, and script generation where possible: `NextBotInstaller/Program.cs:615-628`, `NextBotInstaller/Program.cs:968-1058`, `NextBotInstaller/Program.cs:1116-1293`
- **Preserve raw failure reasons** when reporting errors to the user: `NextBotInstaller/Program.cs:75-78`, `NextBotInstaller/Program.cs:1146-1150`
- **Keep cross-platform logic explicit** with `RuntimeInformation` checks and platform-specific branches: `NextBotInstaller/Program.cs:631-648`, `NextBotInstaller/Program.cs:868-918`, `NextBotInstaller/Program.cs:1219-1293`
- **Prefer small, purpose-built helpers** over repeating filesystem or archive logic inline.

---

## Testing Requirements

There is no dedicated automated test suite in the current repository.
The minimum quality bar for changes is:

1. `dotnet build` succeeds for the project.
2. If packaging behavior changed, `dotnet publish` should still be compatible with the Native AOT release workflow in `.github/workflows/release.yml:76-103`.
3. Manual verification should cover the affected user flow, such as:
   - install NextBot,
   - update NextBot,
   - install NapCat,
   - proxy management.

For cross-platform changes, reviewers should pay special attention to Windows / Linux / macOS branches because they are explicitly encoded in the source: `NextBotInstaller/Program.cs:868-918`, `NextBotInstaller/Program.cs:1219-1293`.

---

## Code Review Checklist

- Does the change reflect the real single-project CLI architecture instead of inventing new layers?
- Are dynamic values escaped before being written to Spectre markup?
- Does the code preserve the actual error reason instead of hiding it?
- Is cross-platform logic correct for all affected OS and architecture branches?
- Is existing helper logic reused instead of duplicated?
- Will the change remain compatible with `.NET 9` and Native AOT publishing?
- If new files were added, are they truly necessary for the current scope?

---

## Examples

- Nullable enabled in project config: `NextBotInstaller/NextBotInstaller.csproj:3-12`
- Native AOT release pipeline: `.github/workflows/release.yml:76-103`
- Safe process argument construction: `NextBotInstaller/Program.cs:1128-1131`, `NextBotInstaller/Program.cs:1166-1169`
- Escaped dynamic UI output: `NextBotInstaller/Program.cs:586-589`, `NextBotInstaller/Program.cs:739`, `NextBotInstaller/Program.cs:757`
