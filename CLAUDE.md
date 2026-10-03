# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ProjectMarvin is a logging/monitoring hub for home-built IoT devices (e.g. RPi Pico W running MicroPython). Devices send `LogEntry` records over simple HTTP calls; a Blazor Server UI shows them in real time via SignalR. All projects target `net10.0`. There is no test project.

`ProjectMarvin.sln` (repo root) contains:
- `Common/` — shared `LogEntry` model (namespace `Marvin.Common`). `LogEntry.Load(string)` parses the plain-string/form payloads simple devices send.
- `ProjectMarvinAPI/` — Minimal API that ingests logs. All endpoints and helpers are in `Program.cs`.
- `ProjectMarvinWeb/` — Blazor Server UI with ASP.NET Core Identity. The project file is `ProjectMarvin.csproj`. (`ProjectMarvinWeb/ProjectMarvin.sln` is a stray secondary solution.)

`Databases/SQLiteLogData.db` (shared log DB) and `ProjectMarvinWeb/Databases/SQLiteLogin.db` (Identity) are checked into git.

## Commands

```powershell
dotnet build ProjectMarvin.sln
dotnet run --project ProjectMarvinAPI   # binds to this machine's first IPv4 address, port 4200
dotnet run --project ProjectMarvinWeb   # https://localhost:7032 ("https" profile)
dotnet ef database update --context <ApplicationDbContextIdentity|ApplicationDbContextLogData>   # run from the project dir
```

Client examples for exercising the API: `ProjectMarvinAPI/Example Code/` (`API Test.http`, CURL, MicroPython). Scalar API docs are served by the API project in Development.

## Architecture

Data flow: device → API endpoint (`GET api/Log/{message}`, `POST api/Log/`, `POST api/LogEx/` JSON) → EF Core saves to `SQLiteLogData.db` → API broadcasts `ReceiveLogUpdate` on its SignalR hub `/loghub` → `Home.razor` (Web) holds a `HubConnection` to the **API's** hub (URL from the `SignalRAPI` connection string) and re-queries its QuickGrid. Reconnect logic and the green/red status indicator are in `Home.razor.cs`.

Non-obvious points:
- **Leftover code in Web:** `Hubs`, `Logic` and `Data` folders exist as parallel copies in the API and Web projects (LogHub, IPFilterMiddleware, APIKeyEndPointValidator, FixedSizeList, LogEntries, LogData DbContext). The API endpoints in `ProjectMarvinWeb/Program.cs` are commented out (the API project owns them), so `APIKeyEndPointValidator`, `FixedSizeList` and `LogEntries` are unused in Web, and `IPFilterMiddleware` only matches `/api/` paths that no longer exist there. Web still maps its own `/loghub`, but `Home.razor.cs` connects to the API's hub, so Web's hub is not used by the UI.
- **Shared SQLite file:** both processes open `SQLiteLogData.db` via relative connection strings in each `appsettings.json` (`LogDataConnection`; Web also has `IdentityConnection`). The API sets `PRAGMA journal_mode=WAL` at startup. Both use `AddDbContextFactory`; Web additionally registers a scoped `ApplicationDbContextIdentity` built from the factory for the Identity components.
- **Error policy:** log-saving errors are deliberately swallowed and written to the console, since simple IoT clients can't handle errors.
- **Security:** `IPFilterMiddleware` restricts to private LAN ranges; `[RequireApiKey]`/`APIKeyEndPointValidator` is only applied to `/api/protected`; Web's `/loghub` requires authorization and `Home.razor` uses `[Authorize]`. Web's `Register` page requires login so nobody can self-register. The seeded login is in the README — change before exposing publicly.
- The API's `GetLocalIPAddress()` throws if the machine has no IPv4 adapter.
- QuickGrid styling uses scoped CSS (`Home.razor.css`, `::deep`), so the grid must sit inside a container element.
- Web is a PWA (`wwwroot/manifest.webmanifest`, `service-worker.js`).
