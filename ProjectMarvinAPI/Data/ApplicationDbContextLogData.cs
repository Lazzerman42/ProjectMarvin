using Marvin.Common;
using Microsoft.EntityFrameworkCore;

namespace ProjectMarvinAPI.Data;

/// <summary>
/// DbContext for the shared log database. The schema is owned by the migrations in the Web project.
/// </summary>
public class ApplicationDbContextLogData(DbContextOptions<ApplicationDbContextLogData> options) : DbContext(options)
{
  public DbSet<LogEntry> LogEntries => Set<LogEntry>();
}
