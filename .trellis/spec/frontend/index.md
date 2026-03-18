# Frontend Development Guidelines

> Frontend-like guidance for the terminal UI in this repository.

---

## Overview

This repository does not have a browser frontend.
The "frontend" guidance in this folder documents the terminal interaction layer built with `Spectre.Console`, including prompts, panels, layout helpers, workflow presentation, and UI-adjacent runtime state.

Use these docs to match the actual codebase:

- there is no `src/`, `components/`, `pages/`, or `hooks/` tree
- UI and workflow rendering live in `NextBotInstaller/Program.cs`
- the app uses `Spectre.Console`, not a web UI framework: `NextBotInstaller/NextBotInstaller.csproj:10-12`

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Terminal UI layout inside the single CLI source file | Filled |
| [Component Guidelines](./component-guidelines.md) | `Spectre.Console` composition patterns | Filled |
| [Hook Guidelines](./hook-guidelines.md) | What to do instead of React-style hooks | Filled |
| [State Management](./state-management.md) | CLI runtime state and local workflow state patterns | Filled |
| [Quality Guidelines](./quality-guidelines.md) | Terminal UI quality and review standards | Filled |
| [Type Safety](./type-safety.md) | C# type-safety and validation patterns | Filled |

---

## How to Use These Guidelines

When reviewing or changing frontend-like code in this repo:

1. Treat "frontend" as terminal UI guidance, not browser guidance
2. Reuse existing `Spectre.Console` composition patterns
3. Escape dynamic values before markup rendering
4. Keep menus, prompts, and completion summaries explicit and readable
5. Avoid introducing React, component, or hook assumptions that do not exist in this codebase

These docs intentionally describe the current CLI installer UI, not a generic web-frontend template.

---

**Language**: All documentation should be written in **English**.
