# Component Guidelines

> How UI building blocks are composed in this project.

---

## Overview

This project does not use React, Vue, or traditional frontend components.
The nearest equivalent to components is the set of reusable `Spectre.Console` UI building blocks used to compose the terminal interface.

Current patterns include:

- `SelectionPrompt<string>` for menus,
- `Grid` for key-value summaries,
- `Panel` for grouped information,
- `Rule` for section separators,
- shared wrapper methods for repeated screen patterns.

Representative examples:

- Main menu prompt: `NextBotInstaller/Program.cs:60-67`
- Welcome panel: `NextBotInstaller/Program.cs:583-595`
- Proxy configuration panel: `NextBotInstaller/Program.cs:704-715`
- Section title helper: `NextBotInstaller/Program.cs:604-612`

---

## Component Structure

A typical terminal UI block in this project follows this shape:

1. create a `Grid`, `Panel`, prompt, or rule,
2. fill it with escaped dynamic values,
3. render it via `AnsiConsole.Write` or `AnsiConsole.Prompt`,
4. keep repeated patterns behind small helper methods.

Examples:

- Welcome info grid plus panel: `NextBotInstaller/Program.cs:583-595`
- Summary grid plus success panel: `NextBotInstaller/Program.cs:214-228`
- Prompt plus `switch` dispatch: `NextBotInstaller/Program.cs:717-780`

If a UI block is reused across workflows, prefer introducing or extending a helper method instead of duplicating the same `Grid` / `Panel` construction.

---

## Props Conventions

There are no frontend props in this codebase.
The equivalent convention is:

- pass plain method parameters into UI helper methods,
- keep helper signatures small and explicit,
- escape dynamic strings before rendering.

Examples:

- `ShowSectionTitle(string title, string subtitle)`: `NextBotInstaller/Program.cs:604-612`
- `RunWithStatusAsync(string message, Func<Task> action)`: `NextBotInstaller/Program.cs:615-620`
- `RunWithStatusAsync<T>(string message, Func<Task<T>> action)`: `NextBotInstaller/Program.cs:623-628`

Avoid passing large mutable objects into presentation helpers unless there is already a clear shared state need.

---

## Styling Patterns

Styling is defined inline through `Spectre.Console` primitives and markup.
Current patterns:

- use `Rule` with a muted style for section separation,
- use `Panel` with headers and rounded borders for grouped information,
- use consistent highlight colors for prompts and status,
- use semantic colors for success, warning, and failure output.

Examples:

- aquamarine title and highlight usage: `NextBotInstaller/Program.cs:578-581`, `NextBotInstaller/Program.cs:619-620`
- panel headers and rounded borders: `NextBotInstaller/Program.cs:592-595`, `NextBotInstaller/Program.cs:712-715`, `NextBotInstaller/Program.cs:223-228`
- semantic message colors: `NextBotInstaller/Program.cs:77-78`, `NextBotInstaller/Program.cs:172`, `NextBotInstaller/Program.cs:230-231`

Use `Markup.Escape` whenever a styled string includes runtime data.

---

## Accessibility

Terminal accessibility in this project mainly means clarity and predictability rather than browser a11y APIs.

Current expectations:

- menus should use explicit action labels,
- step progress should be visible and ordered,
- important runtime values should appear in readable key-value layouts,
- color should reinforce meaning, not be the only source of meaning.

Examples:

- numbered step text during install: `NextBotInstaller/Program.cs:155-210`
- key-value environment summary: `NextBotInstaller/Program.cs:586-589`
- descriptive menu labels: `NextBotInstaller/Program.cs:66`, `NextBotInstaller/Program.cs:721-726`

---

## Common Mistakes

- Treating `Spectre.Console` output as ad-hoc strings instead of consistent UI composition.
- Duplicating similar `Grid` and `Panel` structures instead of reusing a helper.
- Printing dynamic values without `Markup.Escape`.
- Mixing workflow logic and presentation so tightly that repeated UI patterns become hard to maintain.

---

## Examples

- Main menu prompt: `NextBotInstaller/Program.cs:60-67`
- Welcome panel composition: `NextBotInstaller/Program.cs:583-595`
- Proxy settings panel and menu: `NextBotInstaller/Program.cs:704-780`
- Shared section and status helpers: `NextBotInstaller/Program.cs:604-628`
