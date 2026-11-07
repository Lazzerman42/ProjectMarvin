using System.Net;
using Marvin.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProjectMarvinAPI.Data;
using ProjectMarvinAPI.Hubs;

namespace ProjectMarvinAPI.Services;

/// <summary>
/// Completes, saves and announces incoming log entries. Failures are logged and swallowed on
/// purpose: the simple IoT clients that send logs can't do anything useful with an error.
/// </summary>
public sealed class LogIngestionService(
    IDbContextFactory<ApplicationDbContextLogData> dbFactory,
    IHubContext<LogHub> hubContext,
    TimeProvider timeProvider,
    ILogger<LogIngestionService> logger)
{
  public const string LogUpdatedMessage = "ReceiveLogUpdate";

  /// <summary>
  /// Stamps <paramref name="entry"/> with the caller's address and defaults, saves it and tells
  /// connected UIs to reload. Deliberately not cancellable: a device that hangs up straight after
  /// sending should still get its entry logged.
  /// </summary>
  /// <param name="senderOverride">When set, replaces whatever sender the client supplied.</param>
  public async Task IngestAsync(LogEntry entry, IPAddress? callerAddress, string? senderOverride = null)
  {
    ArgumentNullException.ThrowIfNull(entry);

    entry.Id = 0;
    entry.IPAdress = Normalize(callerAddress)?.ToString();
    entry.LogDate ??= timeProvider.GetLocalNow().DateTime;
    if (!string.IsNullOrEmpty(senderOverride))
    {
      entry.Sender = senderOverride;
    }
    if (string.IsNullOrEmpty(entry.LogType))
    {
      entry.LogType = LogEntry.DefaultLogType;
    }

    try
    {
      await using var db = await dbFactory.CreateDbContextAsync();
      db.LogEntries.Add(entry);
      await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Saving log entry from {CallerAddress} failed", entry.IPAdress);
      return;
    }

    try
    {
      await hubContext.Clients.All.SendAsync(LogUpdatedMessage);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Notifying SignalR clients about a new log entry failed");
    }
  }

  private static IPAddress? Normalize(IPAddress? address) =>
      address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
}
