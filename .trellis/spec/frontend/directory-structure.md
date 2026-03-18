# Directory Structure

> How frontend-like UI code is organized in this project.

---

## Overview

This repository does not have a browser frontend.
The closest equivalent is the terminal UI built with `Spectre.Console` inside the CLI application.

Current reality:

- There is no `src/`, `pages/`, `components/`, or `hooks/` directory.
- UI rendering, prompts, workflow orchestration, and helper methods all live in `NextBotInstaller/Program.cs`.
- The "frontend" guidance in this repo should be read as **terminal interaction guidance**.

Relevant sources:

- `NextBotInstaller/NextBotInstaller.csproj:10-12`
- `NextBotInstaller/Program.cs:52-111`
- `NextBotInstaller/Program.cs:574-628`

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

There is only one maintained source file for the application today.
UI-related code is grouped by method responsibility inside `Program.cs` rather than split into separate component files.

---

## Module Organization

UI-related code is currently organized into these sections inside `Program.cs`:

1. **Entry and main menu**
   - Main loop and primary prompt: `NextBotInstaller/Program.cs:52-111`
2. **Workflow screens**
   - Install / update / NapCat flows render section titles, prompts, and summaries: `NextBotInstaller/Program.cs:141-465`
3. **Shared UI helpers**
   - Welcome screen, menu header, section title, and progress spinner helpers: `NextBotInstaller/Program.cs:574-628`
4. **UI-adjacent state and display formatting**
   - Proxy status formatting and mutable state: `NextBotInstaller/Program.cs:676-836`, `NextBotInstaller/Program.cs:1361-1365`

When adding terminal UI behavior:

- Put reusable presentation helpers near the existing UI helpers.
- Keep workflow-specific prompts inside the workflow they belong to.
- Only split code into extra files if the project genuinely grows beyond the current single-file structure.

---

## Naming Conventions

- Use **PascalCase** for methods and internal types.
- Name UI methods after the user-visible action or screen purpose.
  - `ShowWelcomeScreen`
  - `ShowMenuHeader`
  - `ShowSectionTitle`
  - `RunProxyManager`
- Name workflow methods with clear verbs and domain terms.
  - `RunOneClickInstallAsync`
  - `RunNextBotUpdateAsync`
  - `RunNapCatInstallerAsync`

Avoid web-specific names like `Page`, `ViewModel`, or `Component` unless the architecture actually changes.

---

## Examples

- Main selection menu: `NextBotInstaller/Program.cs:57-67`
- Welcome panel and environment summary: `NextBotInstaller/Program.cs:574-596`
- Reusable section title helper: `NextBotInstaller/Program.cs:604-612`
- Shared status spinner helper: `NextBotInstaller/Program.cs:615-628`
