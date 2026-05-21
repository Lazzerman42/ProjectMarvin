# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

ProjectMarvin is a Blazor Server + ASP.NET Core Minimal API solution for real-time IoT device monitoring and centralized logging. IoT devices (e.g., RPi Pico W) POST log entries to the API; SignalR pushes updates to the Blazor Server UI.

**Solution layout:**
- `Common/` — shared library (`LogEntry.cs` data model, targets net10.0)
- `ProjectMarvinWeb/` — Blazor Server web app (main UI, authentication, admin)
- `ProjectMarvinAPI/` — Minimal API project (lightweight REST endpoints for IoT devices)
- `Databases/` — SQLite `.db` files shared between Web and API

## Commands

```powershell
# Build entire solution (from repo root)
dotnet build ProjectMarvin.sln

# Run web app (from ProjectMarvinWeb/)
dotnet run

# Run API (from ProjectMarvinAPI/)
dotnet run

# Apply EF Core migrations
dotnet ef database update
```

**Launch profiles (ProjectMarvinWeb):**
- `https` — `https://localhost:7032` (primary dev)
- `http` — `http://192.168.1.42:4242` (LAN access)

Default test credentials (pre-seeded): `marvin@log.com` / `Marvin42!`

## Architecture

### Data flow
1. IoT device calls `GET /api/Log/{message}` or `POST /api/Log/` (form/JSON)
2. API saves a `LogEntry` to `SQLiteLogData.db` via EF Core
3. `LogHub` (SignalR) broadcasts `ReceiveLogUpdate` to all connected browser clients
4. `Home.razor` receives the signal and refreshes its QuickGrid

### Two databases
| Database | DbContext | Purpose |
|---|---|---|
| `SQLiteLogin.db` | `ApplicationDbContextIdentity` | ASP.NET Core Identity (users/auth) |
| `SQLiteLogData.db` | `ApplicationDbContextLogData` | Log entries (shared by Web & API) |

Connection strings are in `appsettings.json`.

### Security layers
- `[Authorize]` on `Home.razor` — cookie-based Identity auth
- `IPFilterMiddleware` — restricts API access to RFC-1918 LAN addresses (10.x, 172.16–31.x, 192.168.x)
- `RequireApiKeyAttribute` / `APIKeyEndPointValidator` — optional API key guard on sensitive endpoints

### Key files
| File | Role |
|---|---|
| `Program.cs` | Startup: DI registration, middleware pipeline, SignalR, EF Core |
| `Components/Pages/Home.razor(.cs)` | Main log viewer — QuickGrid, SignalR client, auth guard |
| `Hubs/LogHub.cs` | SignalR hub; broadcasts on new log entry |
| `Logic/IPFilterMiddleware.cs` | LAN-only IP filter for API routes |
| `Common/LogEntry.cs` | Core shared model used by both projects |
| `Data/ApplicationDbContext*.cs` | EF Core DbContext definitions |

## Tech Stack

- **Runtime:** .NET 10 (`net10.0`)
- **UI:** Blazor Server (interactive server render mode), Bootstrap, scoped CSS
- **Data grid:** `Microsoft.AspNetCore.Components.QuickGrid` (pagination, sort, filter)
- **Real-time:** SignalR WebSockets (`LogHub`)
- **ORM:** Entity Framework Core 10 with SQLite
- **API docs:** Scalar (`/scalar/v1`) + OpenAPI
- **Code analysis:** Roslynator.Analyzers 4.15.0
- **PWA:** `manifest.webmanifest` + `service-worker.js` for offline support
