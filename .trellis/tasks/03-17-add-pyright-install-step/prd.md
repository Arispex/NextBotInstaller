# Add playwright install step

## Goal

Add a new step to the `Install NextBot` flow so the installed project can use Playwright immediately after installation by running the normal Playwright browser install command after `uv sync`.

## What I already know

- The current installer flow is implemented in `NextBotInstaller/Program.cs:141-232`.
- The current install flow has 7 steps and step 6 currently runs:
  - `python -m ensurepip --upgrade`
  - `python -m pip install --upgrade pip uv`
  - `python -m uv sync`
  - source: `NextBotInstaller/Program.cs:199-207`
- The installer already has reusable subprocess helpers in `NextBotInstaller/Program.cs:1116-1189`.
- The visible dependency snapshot contains `playwright>=1.58.0`.
- The visible runtime usage only launches Chromium, not Firefox or WebKit.
- The user re-tested download speed in China and wants the installer to run the normal Playwright install command instead of using a predownloaded bundle.
- The user explicitly wants this change only in `Install NextBot`, not in `Update NextBot`.

## Requirements

- Add a new Playwright-related step to `Install NextBot`.
- After `uv sync`, run Playwright browser installation through `uv`.
- The implementation should target Chromium only.
- The implementation should fit the existing installer style: explicit step output, helper reuse, and actionable failure messages.
- Do not add the same step to `Update NextBot`.

## Acceptance Criteria

- [ ] `Install NextBot` includes a dedicated Playwright preparation step after `uv sync`.
- [ ] The installer runs Playwright installation via `uv`.
- [ ] The command installs Chromium only.
- [ ] Status text remains clear and step numbering is updated correctly.
- [ ] `Update NextBot` remains unchanged.
- [ ] `dotnet build` passes.

## Definition of Done

- Relevant installer flow updated
- Manual verification steps defined
- `dotnet build` passes
- Scope limited to Playwright installation behavior

## Out of Scope

- Predownloaded browser bundles
- Custom mirrors or hosted browser archives
- Firefox or WebKit installation unless later required
- Changing the `Update NextBot` flow

## Technical Approach

Use the installer's existing subprocess helpers to run the current dependency sync sequence and then execute Playwright installation through the project environment via:

- `python -m uv run python -m playwright install chromium`

## Decision (ADR-lite)

**Context**: A predownload / restore design was initially considered because Playwright downloads were assumed to be slow in China.

**Decision**: Use the normal Playwright installation command after `uv sync`, and only in the install flow.

**Consequences**:
- Simpler implementation and lower maintenance.
- Keeps behavior close to normal project setup.
- Depends on current Playwright download path remaining reliable.
- Update flow intentionally stays unchanged.

## Technical Notes

- Main install flow: `NextBotInstaller/Program.cs:141-232`
- Update flow stays unchanged: `NextBotInstaller/Program.cs:234-303`
- Process execution helpers: `NextBotInstaller/Program.cs:1116-1189`
- Current Chromium-only usage: `NextBotInstaller/bin/Debug/net9.0/server/screenshot.py:31-56`
