# Add playwright install step

## Goal

Add a new step to the `Install NextBot` flow so the installed project can use Playwright immediately after installation, while avoiding the very slow `playwright install` browser-download experience in China.

## What I already know

- The current installer flow is implemented in `NextBotInstaller/Program.cs:141-232`.
- The current install flow has 7 steps and step 6 runs:
  - `python -m ensurepip --upgrade`
  - `python -m pip install --upgrade pip uv`
  - `python -m uv sync`
  - source: `NextBotInstaller/Program.cs:199-207`
- The installer already has reusable subprocess helpers in `NextBotInstaller/Program.cs:1116-1189`.
- The installer already knows how to download and extract platform-specific archives:
  - archive resolution: `NextBotInstaller/Program.cs:839-921`
  - download: `NextBotInstaller/Program.cs:968-977`
  - extraction: `NextBotInstaller/Program.cs:987-1058`
- The current proxy mechanism only helps GitHub-hosted downloads and does not automatically solve Playwright browser downloads:
  - `NextBotInstaller/Program.cs:651-674`
- The only visible dependency snapshot in this repo is a build-output copy of `pyproject.toml`, which contains `playwright>=1.58.0`.
- The visible `uv.lock` snapshot contains a locked `playwright` package entry, so the downloaded NextBot project is likely using a lockfile-backed dependency resolution.
- The user selected the installer-managed pre-download / restore approach.
- The user wants the MVP to support Windows x64 first.

## Assumptions (temporary)

- The desired outcome is: after running the installer, Playwright browsers are ready to use without the slow `playwright install` download path.
- The initial MVP only changes the `Install NextBot` flow, not the `Update NextBot` flow.
- The user is open to a solution that is operationally equivalent to `playwright install`, even if it is implemented by pre-downloading and restoring the required browser artifacts.
- The MVP can stay focused on browser installation and does not need to solve every package mirror problem in the project.
- The MVP should install Chromium only unless later evidence shows other browsers are required.

## Open Questions

- What exact hosted artifact format should the installer expect for the MVP?

## Requirements (evolving)

- Add a new Playwright-related step to `Install NextBot`.
- The solution must be significantly faster and more reliable for users in China.
- The installer should avoid requiring users to manually run `playwright install chromium` after installation.
- The solution should preserve a predictable cross-platform experience as much as possible.
- The implementation should fit the existing installer style: explicit step output, helper reuse, and actionable failure messages.
- The MVP should target Chromium only.
- The MVP should target Windows x64 only.
- The installer should download a prebuilt Playwright browser bundle from GitHub and restore it locally.

## Acceptance Criteria (evolving)

- [ ] `Install NextBot` includes a dedicated Playwright preparation step.
- [ ] After installation, the target project can use Playwright Chromium without the original slow browser-download path.
- [ ] The new step reports clear success / failure information in the terminal.
- [ ] The implementation works within the current installer architecture and reuses existing helpers where appropriate.
- [ ] The MVP scope is clearly documented if it only covers install and not update parity.

## Definition of Done (team quality bar)

- Relevant installer flow updated
- Manual verification steps defined
- `dotnet build` passes
- Behavior and limitations documented if needed
- Risky platform-specific assumptions called out explicitly

## Out of Scope (explicit)

- Refactoring the whole install / update pipeline
- Solving generic mirror acceleration for every dependency in the project
- Adding a full package mirror management system unless the selected approach requires a very small targeted version of it
- Updating the `Update NextBot` flow unless we decide parity is required for MVP
- Installing Firefox or WebKit unless later confirmed necessary

## Technical Approach

Current feasible options are:

1. Keep `playwright install chromium`, but redirect its browser download host to a faster source.
2. Pre-download and host the required Playwright Chromium browser archives or warmed browser cache bundles, then restore them into the expected local location.
3. Pre-bundle Playwright browser artifacts in a project-controlled package if maintenance cost is acceptable.

## Decision (ADR-lite)

Pending final artifact-format decision. Current product direction is Approach B: installer-managed pre-download and restore.

## Technical Notes

### Relevant code locations

- Main install flow: `NextBotInstaller/Program.cs:141-232`
- Update flow for future parity consideration: `NextBotInstaller/Program.cs:234-303`
- Process execution helpers: `NextBotInstaller/Program.cs:1116-1189`
- Download helpers: `NextBotInstaller/Program.cs:839-1058`
- GitHub-only proxy behavior: `NextBotInstaller/Program.cs:651-674`
- Current Chromium-only Playwright usage: `NextBotInstaller/bin/Debug/net9.0/server/screenshot.py:31-56`

### Research Notes

#### What `playwright install` does

- `python -m playwright install` downloads the browser binaries that the Python `playwright` package needs at runtime.
- It is separate from installing the Python package itself.
- It usually downloads browser archives for the current OS / architecture and extracts them into Playwright's browser cache.

#### Downloaded artifacts and storage

Typical extracted artifacts include revisioned directories such as:

- `chromium-<revision>`
- `firefox-<revision>`
- `webkit-<revision>`
- sometimes helper payloads like `ffmpeg-<revision>`

Typical browser cache locations:

- Windows: `%USERPROFILE%\\AppData\\Local\\ms-playwright`
- macOS: `~/Library/Caches/ms-playwright`
- Linux: `~/.cache/ms-playwright`

#### Relevant env / config knobs

- `PLAYWRIGHT_BROWSERS_PATH`: controls cache location
- `PLAYWRIGHT_DOWNLOAD_HOST`: controls download host
- `PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST`
- `PLAYWRIGHT_FIREFOX_DOWNLOAD_HOST`
- `PLAYWRIGHT_WEBKIT_DOWNLOAD_HOST`
- standard proxy env vars such as `HTTP_PROXY` / `HTTPS_PROXY`

#### Feasible approaches here

**Approach A: Keep `playwright install`, override browser download host**

- How it works: still run the official install command, but point browser downloads to a faster mirror or self-hosted host.
- Pros: closest to upstream behavior, lower maintenance.
- Cons: requires a compatible mirror layout and still depends on Playwright-driven network steps.

**Approach B: Installer-managed pre-download and restore** (Recommended)

- How it works: host the required Playwright Chromium browser archives or prebuilt browser cache bundles on a faster source, let the installer download them, extract them into a controlled browser cache path, and run the bot with the same `PLAYWRIGHT_BROWSERS_PATH`.
- Pros: matches the user's manual-hosting idea, fastest and most deterministic for users in China.
- Cons: higher maintenance, sensitive to Playwright version, browser revision, OS, and architecture.

**Approach C: Bundle browser payloads into installer-controlled release artifacts**

- How it works: ship offline browser bundles inside installer-managed packages.
- Pros: highest reliability in restricted networks.
- Cons: larger artifacts and more maintenance across platforms.

#### Missing information before safe implementation

- Exact resolved Playwright browser revision and resulting cache directory layout for the locked version actually used by NextBot
- Target OS / architecture scope for the MVP
- Whether `Update NextBot` also needs browser-cache parity later
- Whether Linux system dependencies are in scope
