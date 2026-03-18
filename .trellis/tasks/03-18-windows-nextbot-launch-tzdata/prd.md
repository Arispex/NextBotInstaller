# Fix Windows NextBot launch tzdata bug

## Goal

Fix the Windows-specific NextBot launch failure after installation. The generated run script currently leads to a runtime error where `ZoneInfo("Asia/Shanghai")` cannot be resolved because `tzdata` is missing on Windows.

## What I already know

- The installer generates the Windows run script in `NextBotInstaller/Program.cs:1219-1233`.
- The Windows run script currently does:
  - add bundled Python directories to `PATH`
  - run `uv run python bot.py`
- The user reported a Windows traceback ending in `ZoneInfoNotFoundError: 'No time zone found with key Asia/Shanghai'`.
- The visible NextBot dependency snapshot contains `playwright>=1.58.0` but does not show `tzdata` in the main dependency list: `NextBotInstaller/bin/Debug/net9.0/pyproject.toml:7-16`.
- Linux works, which is consistent with `zoneinfo` reading system timezone data from the OS, while Windows often requires the `tzdata` Python package.
- This suggests the root cause may be missing Windows-specific timezone data rather than the batch script syntax itself.

## Assumptions (temporary)

- The bug can likely be fixed in the installer without editing the downloaded NextBot project source.
- The minimal acceptable fix is to ensure the installed environment contains usable timezone data on Windows.
- If the root cause is confirmed as missing `tzdata`, the fix should be Windows-specific and should not change Linux behavior.

## Open Questions

- Confirm whether the correct minimal fix is to install `tzdata` on Windows during the install flow.

## Requirements (evolving)

- Fix the Windows launch failure for newly installed NextBot environments.
- Keep Linux behavior unchanged.
- Reuse the current installer flow and helper patterns.
- Preserve actionable failure behavior if dependency installation fails.

## Acceptance Criteria (evolving)

- [ ] A Windows installation performed by the installer includes whatever is needed for `ZoneInfo("Asia/Shanghai")` to work.
- [ ] The generated run flow on Windows can start NextBot without the reported `tzdata` error.
- [ ] The fix is minimal and does not introduce unnecessary architectural changes.
- [ ] `dotnet build` passes after the change.

## Definition of Done (team quality bar)

- Relevant installer flow updated
- Root cause explained
- `dotnet build` passes
- Scope limited to the current Windows launch bug

## Out of Scope (explicit)

- Refactoring the whole script generation system
- Changing unrelated Linux or NapCat launch behavior
- Modifying the upstream NextBot project dependency files in this repository

## Technical Notes

- Main install flow: `NextBotInstaller/Program.cs:141-232`
- Windows run script generation: `NextBotInstaller/Program.cs:1219-1233`
- NextBot time zone usage will be confirmed via source search
