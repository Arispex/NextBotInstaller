# Quality Guidelines

> Code quality standards for frontend-like terminal UI development.

---

## Overview

This repository has no browser frontend.
The quality bar for UI work applies to the terminal interface built with `Spectre.Console`.

Good UI changes in this repo should:

- keep menus and workflow steps clear,
- preserve consistent visual composition,
- escape dynamic values before rendering,
- keep user-visible messages actionable and specific,
- avoid duplicating repeated prompt / panel / spinner patterns.

Representative examples:

- main menu prompt: `NextBotInstaller/Program.cs:60-67`
- reusable screen helpers: `NextBotInstaller/Program.cs:574-628`
- summary panels and completion hints: `NextBotInstaller/Program.cs:213-231`, `NextBotInstaller/Program.cs:377-393`, `NextBotInstaller/Program.cs:447-464`

---

## Forbidden Patterns

- **Do not treat terminal output as raw string dumping** when it should be a structured `Panel`, `Grid`, `Rule`, or prompt.
- **Do not print dynamic values without `Markup.Escape`**.
- **Do not hide real failure reasons** behind vague messages.
- **Do not introduce web-frontend assumptions** such as React component patterns or browser accessibility rules into this CLI-only repository.
- **Do not duplicate menu / status / summary UI patterns** when an existing helper or composition style already exists.

---

## Required Patterns

- Use `SelectionPrompt<string>` for explicit menu choices where user selection is required: `NextBotInstaller/Program.cs:60-67`, `NextBotInstaller/Program.cs:717-748`
- Use `Grid` plus `Panel` for key-value summaries and grouped information: `NextBotInstaller/Program.cs:583-595`, `NextBotInstaller/Program.cs:214-228`, `NextBotInstaller/Program.cs:704-715`
- Use shared helpers for repeated screen framing and progress behavior: `NextBotInstaller/Program.cs:604-628`
- Preserve specific failure details in user-facing output: `NextBotInstaller/Program.cs:75-78`, `NextBotInstaller/Program.cs:88-89`, `NextBotInstaller/Program.cs:99-100`
- Keep action labels and step text explicit so the user always knows what the installer is doing.

---

## Testing Requirements

There is no dedicated frontend test runner in this repository.
For UI-related changes, the minimum checks are:

1. `dotnet build` succeeds.
2. The affected CLI flow renders correctly when run manually.
3. User-visible messages remain readable in the terminal and still include the actual failure reason when something goes wrong.
4. If the flow is platform-specific, validate the relevant OS branch in code review.

Important flows to spot-check manually:

- main menu
- install NextBot
- update NextBot
- install NapCat
- proxy management

---

## Code Review Checklist

- Are terminal UI blocks composed consistently with existing `Spectre.Console` patterns?
- Are dynamic values escaped before markup rendering?
- Are action labels and progress steps clear and specific?
- Is the message format aligned with the current user-facing style of success, warning, and failure output?
- Was an existing prompt, panel, or helper reused where appropriate?
- Does the change still make sense in a CLI app instead of assuming a browser UI?

---

## Examples

- Main menu selection prompt: `NextBotInstaller/Program.cs:60-67`
- Welcome screen composition: `NextBotInstaller/Program.cs:574-596`
- Shared section and spinner helpers: `NextBotInstaller/Program.cs:604-628`
- Success summary and follow-up action hint: `NextBotInstaller/Program.cs:223-231`
