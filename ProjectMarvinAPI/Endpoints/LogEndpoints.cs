using System.Globalization;
using Marvin.Common;
using Microsoft.EntityFrameworkCore;
using ProjectMarvinAPI.Data;
using ProjectMarvinAPI.Security;
using ProjectMarvinAPI.Services;

namespace ProjectMarvinAPI.Endpoints;

/// <summary>The device-facing endpoints. Routes and response formats are a contract with deployed devices.</summary>
public static class LogEndpoints
{
  /// <summary>Sender used by the <c>api/Log/ExampleApp/{message}</c> endpoint.</summary>
  public const string ExampleAppSender = "Magical App";

  public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
  {
    app.MapGet("/api/protected", () => "This endpoint is protected by API Key")
        .RequireApiKey()
        .WithName("GetProtectedData")
        .WithSummary("Example of an endpoint that requires the X-API-Key header.");

    // Creating a log entry with GET isn't RESTful, but it's deliberate: some single-chip
    // sensors can only make plain HTTP GET requests.
    app.MapGet("api/Log/{message}", LogFromUrlAsync)
        .WithName("Log")
        .WithSummary("Creates a log entry from the URL. The message is URL-encoded text or a URL-encoded LogEntry JSON object.");

    // A special endpoint for one application, with preset values (here the sender), so simple
    // scripts don't have to send them.
    app.MapGet("api/Log/ExampleApp/{message}", LogFromExampleAppAsync)
        .WithName("LogExampleApp")
        .WithSummary($"Like api/Log/{{message}}, but the sender is always \"{ExampleAppSender}\".");

    app.MapPost("api/Log/", LogFromBodyAsync)
        .WithName("LogPost")
        .WithSummary("Creates a log entry from the raw body (any content type): plain text or LogEntry JSON.");

    app.MapPost("api/LogEx/", LogFromJsonAsync)
        .WithName("LogEx")
        .WithSummary("Creates a log entry from LogEntry JSON. The preferred endpoint for clients that can send JSON.");

    app.MapGet("api/LogCount", LogCountAsync)
        .WithName("LogCount")
        .WithSummary("Returns the number of stored log entries (handy to check that the database is found).");

    // Lets a device check that its Wi-Fi works: send something known and compare the reply
    app.MapGet("api/Echo/{message}", (string message) => message)
        .WithName("Echo")
        .WithSummary("Returns the message unchanged.");

    // Many small devices have no real-time clock (or no way to set it at boot), so they ask for the time here
    app.MapGet("api/GetLocalTime", (TimeProvider timeProvider) =>
            timeProvider.GetLocalNow().ToString("O", CultureInfo.InvariantCulture))
        .WithName("GetLocalTime")
        .WithSummary("Returns the server's local time in round-trip (ISO 8601) format.");

    return app;
  }

  private static async Task<IResult> LogFromUrlAsync(string message, HttpContext context, LogIngestionService ingestion, TimeProvider timeProvider)
  {
    var result = await IngestAsync(LogEntry.Load(UrlText.Decode(message)), context, ingestion);
    return result ?? TypedResults.Ok(Receipt(timeProvider, message));
  }

  private static async Task<IResult> LogFromExampleAppAsync(string message, HttpContext context, LogIngestionService ingestion, TimeProvider timeProvider)
  {
    var result = await IngestAsync(LogEntry.Load(UrlText.Decode(message)), context, ingestion, ExampleAppSender);
    return result ?? TypedResults.Text(Receipt(timeProvider, message));
  }

  private static async Task<IResult> LogFromBodyAsync(HttpRequest request, LogIngestionService ingestion)
  {
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

    var result = await IngestAsync(LogEntry.Load(body), request.HttpContext, ingestion);
    return result ?? TypedResults.Ok(body);
  }

  private static async Task<IResult> LogFromJsonAsync(LogEntry logEntry, HttpContext context, LogIngestionService ingestion)
  {
    var result = await IngestAsync(logEntry, context, ingestion);
    return result ?? TypedResults.Ok(logEntry);
  }

  private static async Task<IResult> LogCountAsync(ApplicationDbContextLogData db, CancellationToken cancellationToken)
  {
    var count = await db.LogEntries.CountAsync(cancellationToken);
    return TypedResults.Ok($"Database LogEntries Count: {count}");
  }

  /// <returns>A validation problem, or null when the entry was accepted.</returns>
  private static async Task<IResult?> IngestAsync(LogEntry entry, HttpContext context, LogIngestionService ingestion, string? senderOverride = null)
  {
    var errors = LogEntryValidator.Validate(entry);
    if (errors.Count > 0)
    {
      return TypedResults.ValidationProblem(errors);
    }

    await ingestion.IngestAsync(entry, context.Connection.RemoteIpAddress, senderOverride);
    return null;
  }

  private static string Receipt(TimeProvider timeProvider, string message) =>
      $"{timeProvider.GetLocalNow().DateTime} : {message}";
}
