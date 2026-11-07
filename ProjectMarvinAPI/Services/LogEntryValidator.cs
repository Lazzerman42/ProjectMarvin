using Marvin.Common;

namespace ProjectMarvinAPI.Services;

/// <summary>Checks an incoming <see cref="LogEntry"/> before it's saved.</summary>
public static class LogEntryValidator
{
  /// <returns>The problems per field, or an empty dictionary when the entry is valid.</returns>
  public static Dictionary<string, string[]> Validate(LogEntry entry)
  {
    ArgumentNullException.ThrowIfNull(entry);

    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(entry.Message))
    {
      errors[nameof(LogEntry.Message)] = ["A message is required."];
    }
    else if (entry.Message.Length > LogEntry.MaxMessageLength)
    {
      errors[nameof(LogEntry.Message)] = [$"The message can be at most {LogEntry.MaxMessageLength} characters."];
    }

    if (entry.Sender?.Length > LogEntry.MaxSenderLength)
    {
      errors[nameof(LogEntry.Sender)] = [$"The sender can be at most {LogEntry.MaxSenderLength} characters."];
    }

    if (entry.LogType?.Length > LogEntry.MaxLogTypeLength)
    {
      errors[nameof(LogEntry.LogType)] = [$"The log type can be at most {LogEntry.MaxLogTypeLength} characters."];
    }

    return errors;
  }
}
