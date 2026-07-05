# worklog.today

A Google Keep–inspired workspace where professionals capture **notes** and **daily tasks**, then turn
them into **timesheets** and **status reports** — with **AI summaries**. Built with ASP.NET Core 8 MVC,
EF Core code-first migrations, and ASP.NET Core Identity.

## Highlights

- **SEO-perfect landing page** (`/`) — semantic HTML, meta + Open Graph/Twitter tags, JSON-LD, `robots.txt`, `sitemap.xml`.
- **Production-ready auth** — Identity with register/login, lockout, secure sliding cookie, anti-forgery.
- **One-page workspace** (`/app`) — tabbed SPA-feel UI:
  - **Notes** — Keep-style masonry board: color swatches, pin, labels, archive, edit, live search.
  - **Daily Tasks** — log what you worked on (project, category, status, hours, billable) per week.
  - **Timesheet** — weekly totals, daily breakdown, by-project bars, **AI weekly summary**.
  - **Reports** — hours by category / project / day.
- **AI where feasible (no slowdown)** — `AiService` prefers a local **Ollama** model when reachable
  (short timeout) and otherwise falls back to an **instant local engine**, so the UI never blocks.
  Configure via `appsettings.json` → `Ai` (`Provider`: `Auto` | `Ollama` | `Local`).
- **Installable desktop app (PWA)** — `manifest.webmanifest` + service worker (offline app-shell) + in-app
  **Install** button. Once installed, worklog runs in its own desktop window with an icon.
- **Desktop sticky notes** — every note has a **Pop out** button that opens it as a small floating
  sticky window (`/sticky/{id}`) with live auto-save and color switching. Use **New sticky** to spawn a
  blank one. Scatter several across your desktop; installed as a PWA they behave like native stickies.
- **Auto-seeded demo data** on first run.

## Run

```bash
cd src/WorklogToday
dotnet run
```

DB is created/migrated + seeded automatically. Sign in with the demo account:

- **Email:** `demo@worklog.today`
- **Password:** `Demo#2026`

## AI / Ollama (optional)

By default `Ai:Provider` is `Auto`. If you run Ollama locally (`ollama serve` with a model such as
`llama3.2`), summaries and tag suggestions use it; otherwise the fast local engine is used. Set
`Ai:Provider` to `Local` to force the local engine, or `Ollama` to require the model.

## Database

SQLite by default (`DataSource=app.db`). To use SQL Server, change the connection string and swap
`UseSqlite(...)` for `UseSqlServer(...)` in `Program.cs`. Create/apply migrations with:

```bash
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef database update
```

## Tech stack

ASP.NET Core 8 MVC · EF Core 8 (SQLite, code-first) · ASP.NET Core Identity · vanilla JS + Bootstrap Icons · custom CSS design system.
