# Database Guidelines

> Database patterns and conventions for this project.

---

## Overview

This project does **not** currently implement an application-managed database layer.
There is no ORM, no migration system, and no repository layer in the installer itself.

Important reality checks:

- The project file only references `Spectre.Console`; there are no database packages in `NextBotInstaller/NextBotInstaller.csproj:10-12`.
- The codebase is centered around filesystem, HTTP, archive, and process orchestration in `NextBotInstaller/Program.cs:141-1365`.
- `app.db` appears only as a file that should be preserved during NextBot updates, not as a database that this installer queries directly: `NextBotInstaller/Program.cs:38-46`.

If a future feature introduces persistence, this guideline must be updated before new patterns are treated as standard.

---

## Query Patterns

There are no SQL or ORM query patterns to follow today.
Current persistence-related behavior uses these patterns instead:

1. **Filesystem operations over database access**
   - The installer works with directories, archives, scripts, and copied files rather than tables or records.
   - Examples: `NextBotInstaller/Program.cs:147-179`, `NextBotInstaller/Program.cs:974-984`, `NextBotInstaller/Program.cs:1215-1293`
2. **JSON parsing for remote metadata**
   - Remote release metadata is parsed with `JsonDocument`, not stored in a database.
   - Example: `NextBotInstaller/Program.cs:1310-1340`
3. **Preserve external app data instead of managing it**
   - The update flow explicitly protects `.env`, `.webui_auth.json`, and `app.db` from cleanup.
   - Example: `NextBotInstaller/Program.cs:38-46`

If you need durable state in the installer, prefer documenting why file-based storage is insufficient before introducing a database dependency.

---

## Migrations

There is no migration workflow in this repository.

Current expectations:

- Do not add migration terminology to this project unless a real database layer has been introduced.
- Do not create placeholder migration folders or scripts “for future use”.
- If a database becomes necessary, document:
  - chosen storage engine,
  - schema ownership,
  - migration command,
  - rollback approach,
  - and compatibility expectations.

---

## Naming Conventions

No database naming convention exists yet because no database schema is maintained here.

Until that changes:

- Treat data files as files, not tables.
- Keep file and directory names aligned with their operational purpose (`python`, `napcat`, `run_bot.sh`, `run_napcat.bat`).
- Preserve external project file names exactly when they are part of the installed NextBot workspace.

---

## Common Mistakes

- Assuming `app.db` means the installer itself owns a database layer. In reality it only preserves that file during updates: `NextBotInstaller/Program.cs:38-46`.
- Introducing ORM-style abstractions when the current code only needs filesystem and JSON helpers.
- Hiding file-based state behind misleading names like `Repository` or `Migration` when no such architecture exists.
- Adding a database dependency without first documenting why the current file-based approach is insufficient.

---

## Examples

- No DB packages in project file: `NextBotInstaller/NextBotInstaller.csproj:10-12`
- Protected external data files, including `app.db`: `NextBotInstaller/Program.cs:38-46`
- JSON metadata parsing instead of DB access: `NextBotInstaller/Program.cs:1310-1340`
