
using Marvin.Common;

/// <summary>
/// Not used right now, can be used as a SQLite replacement - for in memory use with total control
/// </summary>
public class LogEntries
{
  public FixedSizeList<LogEntry>? messages { get; set; } = new FixedSizeList<LogEntry>(1000);
}
