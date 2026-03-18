# Directory Structure

> How backend-like code is organized in this project.

---

## Overview

This repository does not have a separate backend service, API layer, or database module.
The backend-like logic lives inside a single .NET 9 CLI application and is implemented in `NextBotInstaller/Program.cs`.

Document reality, not an idealized layered architecture:

- The solution contains one project: `NextBotInstaller.sln:2`
- The project targets `.NET 9` with nullable reference types enabled: `NextBotInstaller/NextBotInstaller.csproj:3-12`
- Most workflows, helper methods, and small internal types live in one file: `NextBotInstaller/Program.cs:12-1368`

---

## Directory Layout

```text
.
├── NextBotInstaller.sln
├── NextBotInstaller/
│   ├── NextBotInstaller.csproj
│   └── Program.cs
├── .github/
│   └── workflows/
│       └── release.yml
└── .trellis/
    └── spec/
```

Key points:

- `NextBotInstaller/Program.cs` contains the entrypoint, user workflows, network helpers, process helpers, and small internal types.
- `.github/workflows/release.yml` defines the release and Native AOT packaging pipeline.
- There are no `Controllers`, `Services`, `Repositories`, `Routes`, or `Database` directories in the current codebase.

---

## Module Organization

The single-file organization is grouped by responsibility rather than by folders.

Current structure inside `Program.cs`:

1. **Startup and main menu**
   - `Main()` drives the menu loop and action dispatch: `NextBotInstaller/Program.cs:52-111`
2. **Top-level workflows**
   - Install NextBot: `NextBotInstaller/Program.cs:141-232`
   - Update NextBot: `NextBotInstaller/Program.cs:234-303`
   - Install NapCat / manage proxy: `NextBotInstaller/Program.cs:305-465`, `NextBotInstaller/Program.cs:698-780`
3. **Shared helpers**
   - UI helpers such as `ShowWelcomeScreen` and `RunWithStatusAsync`: `NextBotInstaller/Program.cs:574-628`
   - Network and archive helpers: `NextBotInstaller/Program.cs:839-1058`
   - Process and script helpers: `NextBotInstaller/Program.cs:1116-1293`
4. **Small internal types**
   - `ArchivePlan`, `LatestReleaseMetadata`, `GithubProxyState`: `NextBotInstaller/Program.cs:1357-1365`

When adding code, follow the current grouping style:

- Put end-user flows near the existing workflow methods.
- Put reusable file / network / process logic in helper methods.
- Keep small internal data carriers near the bottom of the file unless a dedicated file becomes clearly necessary.

---

## Naming Conventions

Current naming conventions are standard C# conventions:

- **PascalCase** for methods, types, and properties.
- **Descriptive verb-based method names** for workflows and helpers:
  - `RunOneClickInstallAsync`
  - `ResolveArchivePlanAsync`
  - `CreateNapCatRunScript`
- **Constant-style names** for shared immutable values:
  - `PythonVersion`
  - `NextBotSourceZipUrl`
  - `BuiltinGithubProxySites`

Prefer names that describe the job directly instead of introducing abstract layers like `InstallerService` or `WorkflowManager` unless the codebase actually grows into multiple files.

---

## Examples

- Solution and single-project layout: `NextBotInstaller.sln:2`, `NextBotInstaller/NextBotInstaller.csproj:1-14`
- Main menu and action dispatch: `NextBotInstaller/Program.cs:57-108`
- Workflow grouping by user action: `NextBotInstaller/Program.cs:141-232`, `NextBotInstaller/Program.cs:234-303`
- Helper grouping by technical concern: `NextBotInstaller/Program.cs:574-628`, `NextBotInstaller/Program.cs:839-1058`, `NextBotInstaller/Program.cs:1116-1293`
