using System.Net;
using Marvin.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectMarvinAPI.Data;

namespace ProjectMarvinAPI.Tests;

/// <summary>
/// Hosts the API in memory against its own throw-away SQLite file. Every request gets a
/// LAN client address (override per request with the <see cref="RemoteIpHeader"/> header),
/// because TestServer requests have no remote address of their own.
/// </summary>
public sealed class MarvinApiFactory : WebApplicationFactory<Program>
{
  public const string RemoteIpHeader = "X-Test-Remote-Ip";
  public const string DefaultRemoteIp = "192.168.1.50";
  public const string TestApiKey = "test-api-key-0123456789";

  private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"marvin-api-tests-{Guid.NewGuid():N}.db");

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");
    builder.UseSetting("ApiKey", TestApiKey);

    builder.ConfigureTestServices(services =>
    {
      // Point the log database at a temporary file instead of the shared one
      var existing = services.Where(d => d.ServiceType == typeof(IDbContextOptionsConfiguration<ApplicationDbContextLogData>)).ToList();
      foreach (var descriptor in existing)
      {
        services.Remove(descriptor);
      }
      services.ConfigureDbContext<ApplicationDbContextLogData>(options => options.UseSqlite($"Data Source={_dbPath}"));

      services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
    });
  }

  protected override IHost CreateHost(IHostBuilder builder)
  {
    var host = base.CreateHost(builder);

    using var scope = host.Services.CreateScope();
    using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContextLogData>>().CreateDbContext();
    db.Database.EnsureCreated();

    return host;
  }

  public async Task ResetDatabaseAsync()
  {
    await using var db = await CreateDbContextAsync();
    await db.LogEntries.ExecuteDeleteAsync();
  }

  public Task<ApplicationDbContextLogData> CreateDbContextAsync() =>
      Services.GetRequiredService<IDbContextFactory<ApplicationDbContextLogData>>().CreateDbContextAsync();

  public async Task<List<LogEntry>> GetLogEntriesAsync()
  {
    await using var db = await CreateDbContextAsync();
    return await db.LogEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
  }

  public async Task<LogEntry> GetSingleLogEntryAsync()
  {
    var entries = await GetLogEntriesAsync();
    return Assert.Single(entries);
  }

  public HubConnection CreateHubConnection() =>
      new HubConnectionBuilder()
          .WithUrl(new Uri(Server.BaseAddress, "loghub"), options =>
          {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
          })
          .Build();

  public override async ValueTask DisposeAsync()
  {
    await base.DisposeAsync();
    SqliteConnection.ClearAllPools();
    foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
    {
      try
      {
        File.Delete(file);
      }
      catch (IOException)
      {
        // Best effort - it's a temp file
      }
    }
  }

  private sealed class FakeRemoteIpStartupFilter : IStartupFilter
  {
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
      app.Use((context, nextMiddleware) =>
      {
        var ip = context.Request.Headers[RemoteIpHeader].ToString();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip.Length > 0 ? ip : DefaultRemoteIp);
        return nextMiddleware(context);
      });
      next(app);
    };
  }
}
