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

      string decodedString = HttpUtility.UrlDecode(logEntry.Message ?? "", Encoding.GetEncoding("utf-8"));
      logEntry.Message = decodedString;
      string decodedStringSender = HttpUtility.UrlDecode(logEntry.Sender ?? "", Encoding.GetEncoding("utf-8"));
      logEntry.Sender = decodedStringSender;
      return logEntry;
    }
    catch
    {
      return new LogEntry(JSONString);
    }
  }
}
