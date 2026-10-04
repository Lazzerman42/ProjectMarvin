using System.Data.Common;
using Marvin.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ProjectMarvin.Data
{
  /// <summary>
  /// DBContext for LogData
  /// </summary>
  public class ApplicationDbContextLogData : DbContext
  {
    public ApplicationDbContextLogData(DbContextOptions<ApplicationDbContextLogData> options)
        : base(options)
    {
    }

    public DbSet<LogEntry> LogEntries { get; set; }

    /// <summary>
    /// Unicode-aware upper case for case-insensitive search in LINQ queries. SQLite's built-in
    /// upper() only handles ASCII, so "ä" would never match "Ä".
    /// </summary>
    public static string? UnicodeUpper(string? value) => throw new NotSupportedException("Only for use in LINQ queries");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      optionsBuilder.AddInterceptors(UnicodeUpperInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);
      modelBuilder.HasDbFunction(typeof(ApplicationDbContextLogData).GetMethod(nameof(UnicodeUpper))!)
          .HasName("unicode_upper");
    }

    // Registers unicode_upper() on every SQLite connection EF opens
    private sealed class UnicodeUpperInterceptor : DbConnectionInterceptor
    {
      public static readonly UnicodeUpperInterceptor Instance = new();

      public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Register(connection);

      public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
      {
        Register(connection);
        return Task.CompletedTask;
      }

      private static void Register(DbConnection connection)
      {
        if (connection is SqliteConnection sqlite)
        {
          sqlite.CreateFunction("unicode_upper", (string? s) => s?.ToUpperInvariant(), isDeterministic: true);
        }
      }
    }
  }
}
