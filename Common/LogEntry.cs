using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace Marvin.Common;

public class LogEntry
{
	public const string DefaultLogType = "Info";

	/// <summary>Longest accepted <see cref="Message"/>, in characters.</summary>
	public const int MaxMessageLength = 8000;

	/// <summary>Longest accepted <see cref="Sender"/>, in characters.</summary>
	public const int MaxSenderLength = 200;

	/// <summary>Longest accepted <see cref="LogType"/>, in characters.</summary>
	public const int MaxLogTypeLength = 50;

	private static readonly JsonSerializerOptions _loadOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true
	};

	[JsonIgnore]
	public int Id { get; set; }
	public string? Message { get; set; }
	public DateTime? LogDate { get; set; }
	public string LogDateSE => LogDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
	public string? IPAdress { get; set; }
	public string? Sender { get; set; }
	public string? LogType { get; set; } = DefaultLogType;

	public LogEntry(string msg)
	{
		Message = msg;
	}

	public LogEntry()
	{
	}

	/// <summary>
	/// Parses the payload a simple device sends: either a JSON object with LogEntry fields, or plain
	/// text that becomes the message. In a JSON payload, Message and Sender may be URL-encoded with
	/// ISO-8859-1 escapes (%E5 = 'å'), which is what the MicroPython example sends.
	/// </summary>
	public static LogEntry Load(string payload)
	{
		ArgumentNullException.ThrowIfNull(payload);

		try
		{
			var logEntry = JsonSerializer.Deserialize<LogEntry>(payload, _loadOptions);
			if (logEntry is null)
			{
				return new LogEntry(payload);
			}

			logEntry.Message = HttpUtility.UrlDecode(logEntry.Message ?? "", Encoding.Latin1);
			logEntry.Sender = HttpUtility.UrlDecode(logEntry.Sender, Encoding.Latin1);
			return logEntry;
		}
		catch (JsonException)
		{
			return new LogEntry(payload);
		}
	}
}
