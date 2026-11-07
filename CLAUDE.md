# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ProjectMarvin is a logging/monitoring hub for home-built IoT devices (e.g. RPi Pico W running MicroPython). Devices send `LogEntry` records over simple HTTP calls; a Blazor Server UI shows them in real time via SignalR. All projects target `net10.0`.

`ProjectMarvin.sln` (repo root) contains:
- `Common/` — shared `LogEntry` model (namespace `Marvin.Common`). `LogEntry.Load(string)` parses the plain-string/form payloads simple devices send.
- `ProjectMarvinAPI/` — Minimal API that ingests logs. `Program.cs` only wires things up; endpoints are in `Endpoints/LogEndpoints.cs`, saving + SignalR broadcast in `Services/LogIngestionService.cs`, input checks in `Services/LogEntryValidator.cs`, and the IP filter / API key filter in `Security/`.
- `ProjectMarvinAPI.Tests/` — xUnit integration tests for the API (`WebApplicationFactory`). `MarvinApiFactory` points the API at a temp SQLite file and fakes the caller IP (header `X-Test-Remote-Ip`, default 192.168.1.50), since TestServer requests have none.
- `ProjectMarvinWeb/` — Blazor Server UI with ASP.NET Core Identity. The project file is `ProjectMarvin.csproj`. (`ProjectMarvinWeb/ProjectMarvin.sln` is a stray secondary solution.)

`Databases/SQLiteLogData.db` (shared log DB) and `ProjectMarvinWeb/Databases/SQLiteLogin.db` (Identity) are checked into git.

## Commands

```powershell
dotnet build ProjectMarvin.sln
dotnet test ProjectMarvinAPI.Tests
dotnet test ProjectMarvinAPI.Tests --filter "FullyQualifiedName~GetLogTests"   # a single class
dotnet run --project ProjectMarvinAPI   # binds to this machine's LAN IPv4 address, port 4200 (Marvin:Port), unless Kestrel:Endpoints is configured
dotnet run --project ProjectMarvinWeb   # https://localhost:7032 ("https" profile)
dotnet ef database update --context <ApplicationDbContextIdentity|ApplicationDbContextLogData>   # run from the project dir
```

Client examples for exercising the API: `ProjectMarvinAPI/Example Code/` (`API Test.http`, CURL, MicroPython). Scalar API docs are served by the API project in Development.

## Architecture

Data flow: device → API endpoint (`GET api/Log/{message}`, `POST api/Log/`, `POST api/LogEx/` JSON) → EF Core saves to `SQLiteLogData.db` → API broadcasts `ReceiveLogUpdate` on its SignalR hub `/loghub` → `Home.razor` (Web) holds a `HubConnection` to the **API's** hub (URL from the `SignalRAPI` connection string) and reloads its list. Reconnect logic and the Live/Offline pill are in `Home.razor.cs`.

Non-obvious points:
- **Device contract:** routes, response formats and the GET-creates-a-log design are relied on by deployed devices (some sensors can only do HTTP GET) — keep them stable; the tests pin them. `api/Log/{message}` returns a JSON string `"<local time> : <message>"`, `ExampleApp` and `Echo` return text/plain, `POST api/Log/` accepts any content type and echoes the body.
- **Decoding:** GET messages go through `UrlText.Decode` (UTF-8, falling back to ISO-8859-1 when the escapes aren't valid UTF-8), then `LogEntry.Load`, which URL-decodes Message/Sender of a JSON payload as ISO-8859-1 — that's what the MicroPython example sends (`%E5` = 'å').
- **Web is UI only:** the API project owns all ingestion endpoints and the `/loghub` hub; `Home.razor.cs` connects to the API's hub.
- **Shared SQLite file:** both processes open `SQLiteLogData.db` via relative connection strings in each `appsettings.json` (`LogDataConnection`; Web also has `IdentityConnection`). The API sets `PRAGMA journal_mode=WAL` at startup. Both use `AddDbContextFactory`; Web additionally registers a scoped `ApplicationDbContextIdentity` built from the factory for the Identity components.
- **Error policy:** log-saving errors are deliberately swallowed and logged (`LogIngestionService`), since simple IoT clients can't handle errors. Invalid input gets a 400 validation problem (limits are constants on `LogEntry`); other exceptions become ProblemDetails without internals.
- **Security:** the API has no authentication; `IpFilterMiddleware` (on for every path, `IpFilter:Enabled`) answers 403 to non-LAN callers. `.RequireApiKey()` (constant-time check of `X-API-Key` against `ApiKey`; rejects all when unset) is only on `/api/protected`. Request bodies are capped at 64 KB. Web: `Home.razor` uses `[Authorize]`, `Register` requires login so nobody can self-register, and failed logins lock the account (5 attempts / 15 min). The seeded login is in the README — change before exposing publicly.
- `LocalNetwork.GetLocalIPv4Address()` prefers an adapter with a default gateway and throws if the machine has no IPv4 address.
- **Log list (`Home`):** a hand-built CSS-grid list, not QuickGrid. Rows load in batches of 20 (infinite scroll: `wwwroot/marvin.js` watches a sentinel element with an IntersectionObserver and calls `LoadMoreAsync`). Live updates reload as many rows as are already shown. The same markup is shown as table rows on desktop and as cards under 641px, and the mobile view has no sorting. Search covers Sender, Message and IP through `ApplicationDbContextLogData.UnicodeUpper` (a SQLite function registered per connection, because SQLite's `upper()` only handles ASCII, so 'ä' would not match 'Ä').
- **Styling:** the design tokens (dark/light, switched via `data-theme` + localStorage) are in `wwwroot/theme.css`, and the Geist fonts are self-hosted in `wwwroot/fonts`. Don't use the class name `row` (it clashes with Bootstrap, which is still loaded for the Account pages). Stylesheets are linked through `@Assets[...]` because `Program.cs` caches `.css`/`.js` as immutable.
- **Identity pages redirect without exceptions:** `BlazorDisableThrowNavigationException` is on, so `IdentityRedirectManager.RedirectTo*` returns — always `return;` right after it. Pages look up the user with `UserManager.GetUserAsync` and call `RedirectToInvalidUser` when it's null (there's no `IdentityUserAccessor`). `RedirectTo` sends redirects to other hosts (`//x`, `https://x`) to the start page.
- Web is a PWA (`wwwroot/manifest.webmanifest`, `service-worker.js`).
