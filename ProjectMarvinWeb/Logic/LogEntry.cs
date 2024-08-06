﻿using System.Text;
using System.Text.Json;
using System.Web;

public class LogEntry
{
	public int Id { get; set; }
	public string? Message { get; set; }
	public DateTime? LogDate { get; set; }
	public string LogDateSE => LogDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
	public string? IPAdress { get; set; }
	public string? Sender { get; set; }
	public string LogType { get; set; }

	public LogEntry(string msg)
	{
		Message = msg;
	}
	public LogEntry()
	{

	}
	public static LogEntry Load(string JSONString)
	{
		try
		{

			LogEntry logEntry = JsonSerializer.Deserialize<LogEntry>(JSONString,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					IncludeFields = true
				}
				) ?? new LogEntry(JSONString);

			byte[] bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(logEntry.Message);
			string decodedString = HttpUtility.UrlDecode(logEntry.Message, Encoding.GetEncoding("iso-8859-1"));
			logEntry.Message = decodedString;
			return logEntry;
		}
		catch
		{
			return new LogEntry(JSONString);
		}
	}
}