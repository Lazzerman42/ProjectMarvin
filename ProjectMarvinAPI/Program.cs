using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using ProjectMarvinAPI.Data;
using ProjectMarvinAPI.Endpoints;
using ProjectMarvinAPI.Hubs;
using ProjectMarvinAPI.Security;
using ProjectMarvinAPI.Services;
using Scalar.AspNetCore;

// Devices only send small log entries, so keep request bodies small
const long MaxRequestBodySize = 64 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LogIngestionService>();
builder.Services.Configure<IpFilterOptions>(builder.Configuration.GetSection(IpFilterOptions.SectionName));

var logDataConnection = builder.Configuration.GetConnectionString("LogDataConnection")
    ?? throw new InvalidOperationException("Connection string 'LogDataConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContextLogData>(options => options.UseSqlite(logDataConnection));

builder.Services.Configure<KestrelServerOptions>(options =>
{
  options.Limits.MaxRequestBodySize = MaxRequestBodySize;
  options.AddServerHeader = false;
});
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = MaxRequestBodySize);

// Listen on this machine's LAN address so devices can reach the API, unless endpoints are configured
// explicitly (the "Kestrel:Endpoints" section). The port can be changed with "Marvin:Port".
if (!builder.Configuration.GetSection("Kestrel:Endpoints").Exists())
{
  var port = builder.Configuration.GetValue("Marvin:Port", 4200);
  builder.WebHost.ConfigureKestrel(options => options.Listen(LocalNetwork.GetLocalIPv4Address(), port));
}

var app = builder.Build();

// WAL lets the Web app read the shared database while the API writes to it
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContextLogData>>().CreateDbContextAsync())
{
  await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
  await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size=5000;");
}

app.UseExceptionHandler(new ExceptionHandlerOptions
{
  // e.g. 413 when a body is larger than MaxRequestBodySize, instead of 500
  StatusCodeSelector = ex => ex is Microsoft.AspNetCore.Http.BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError
});
app.UseMiddleware<IpFilterMiddleware>();
app.Use((context, next) =>
{
  context.Response.Headers.XContentTypeOptions = "nosniff";
  return next(context);
});

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
  app.MapScalarApiReference();
}

// Tells the Web UI that new log entries have arrived
app.MapHub<LogHub>("/loghub");
app.MapLogEndpoints();

await app.RunAsync();

/// <summary>Entry point; public so the integration tests can host the API.</summary>
public partial class Program;
