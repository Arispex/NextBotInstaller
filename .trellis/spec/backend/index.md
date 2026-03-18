# Backend Development Guidelines

> Backend-like guidance for the single-project .NET CLI in this repository.

---

## Overview

This repository does not contain a separate backend service, HTTP API, or database module.
The "backend" guidance in this folder documents the backend-like parts of the installer that live inside `NextBotInstaller/Program.cs`, such as workflow orchestration, filesystem operations, networking, archive handling, and subprocess execution.

Use these docs to match the actual codebase:

- one solution with one project: `NextBotInstaller.sln:2`
- `.NET 9` with nullable reference types enabled: `NextBotInstaller/NextBotInstaller.csproj:3-12`
- main implementation in a single source file: `NextBotInstaller/Program.cs:12-1368`

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Single-file CLI layout and grouping | Filled |
| [Database Guidelines](./database-guidelines.md) | No-database reality and file-based persistence caveats | Filled |
| [Error Handling](./error-handling.md) | CLI failure handling and exception patterns | Filled |
| [Quality Guidelines](./quality-guidelines.md) | Cross-platform, AOT, and helper-reuse standards | Filled |
| [Logging Guidelines](./logging-guidelines.md) | Terminal output and failure-detail conventions | Filled |

---

## How to Use These Guidelines

When reviewing or changing backend-like code in this repo:

1. Start from `NextBotInstaller/Program.cs`
2. Follow the current single-file grouping rather than inventing new layers
3. Keep user-visible failure reasons intact
4. Reuse existing helpers for process, archive, and network operations
5. Check Native AOT and cross-platform implications when behavior changes

These docs intentionally describe the current CLI installer architecture, not a generic web-backend template.

---

**Language**: All documentation should be written in **English**.
