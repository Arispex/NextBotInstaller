# State Management

> How state is managed in this project.

---

## Overview

This project does not use a frontend state library.
State is managed directly inside the CLI process through:

- local variables inside workflows,
- a single small mutable shared state object for GitHub proxy configuration,
- immutable constants and record types for shared configuration or results.

Representative examples:

- shared proxy state instance: `NextBotInstaller/Program.cs:49`
- state mutation helper: `NextBotInstaller/Program.cs:832-836`
- immutable result records: `NextBotInstaller/Program.cs:1357-1359`
- immutable protected file / directory sets: `NextBotInstaller/Program.cs:33-46`

---

## State Categories

### Local workflow state

Use local variables for values that only matter within one action.
Examples:

- install flow paths such as `workingDirectory`, `cacheDirectory`, `installDirectory`: `NextBotInstaller/Program.cs:147-149`
- update flow runtime variables: `NextBotInstaller/Program.cs:240-252`

### Shared process state

Use the existing singleton-like state object only for values that must persist across menu actions in the running process.
Example:

- `GithubProxyState` tracks whether a proxy is enabled, which base URL is used, and where it came from: `NextBotInstaller/Program.cs:1361-1365`

### Immutable shared configuration

Use constants, readonly arrays, readonly sets, and records for static or computed configuration.
Examples:

- built-in proxy sites: `NextBotInstaller/Program.cs:25-32`
- protected update directories and files: `NextBotInstaller/Program.cs:33-46`
- archive and metadata records: `NextBotInstaller/Program.cs:1357-1359`

---

## When to Use Global State

Only promote data to shared process state when all of these are true:

- multiple menu actions need access to the value,
- the value represents current runtime configuration,
- passing it through every method would add unnecessary friction.

The current example is GitHub proxy selection:

- read in the welcome screen and summaries: `NextBotInstaller/Program.cs:588`, `NextBotInstaller/Program.cs:707`, `NextBotInstaller/Program.cs:822-830`
- updated by proxy management and auto-detection: `NextBotInstaller/Program.cs:133-137`, `NextBotInstaller/Program.cs:738`, `NextBotInstaller/Program.cs:756`, `NextBotInstaller/Program.cs:770`, `NextBotInstaller/Program.cs:775`

If a value is only used within one workflow, keep it local.

---

## Server State

There is no frontend server-state cache in this project.
Remote data is fetched on demand and used immediately.

Current patterns:

- fetch latest release metadata when needed: `NextBotInstaller/Program.cs:839-847`
- probe URL reachability through helper methods: `NextBotInstaller/Program.cs:923-965`
- do not maintain a separate client-side cache abstraction for these values

---

## Common Mistakes

- Introducing a heavy state abstraction when local variables are enough.
- Turning static configuration into mutable global state unnecessarily.
- Hiding shared runtime state updates in many places instead of using a small dedicated helper like `ApplyProxyState`: `NextBotInstaller/Program.cs:832-836`
- Treating remote release metadata as long-lived application state when it is just a one-time workflow input.

---

## Examples

- Shared proxy state instance: `NextBotInstaller/Program.cs:49`, `NextBotInstaller/Program.cs:1361-1365`
- Shared state update helper: `NextBotInstaller/Program.cs:832-836`
- Local workflow state in install flow: `NextBotInstaller/Program.cs:147-149`
- Immutable config collections: `NextBotInstaller/Program.cs:25-46`
