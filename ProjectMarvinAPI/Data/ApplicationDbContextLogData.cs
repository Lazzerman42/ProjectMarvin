using Microsoft.EntityFrameworkCore;

namespace ProjectMarvinAPI.Data
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
	}
}
