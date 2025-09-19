using Marvin.Common;
using Microsoft.EntityFrameworkCore;
using ProjectMarvinAPI.Data;
using ProjectMarvinAPI.Hubs;
using ProjectMarvinAPI.Middleware;
using Scalar.AspNetCore;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LogHub>(); // Serverside of SignalR

// DBContextFactory for SQLite LogData database
var LogDataConnection = builder.Configuration.GetConnectionString("LogDataConnection");
builder.Services.AddDbContextFactory<ApplicationDbContextLogData>(options =>
    options.UseSqlite(LogDataConnection));

// Start API on this machines IP-Adress on port 4200
builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.Listen(IPAddress.Parse(GetLocalIPAddress()), 4200));

var app = builder.Build();
app.MapOpenApi();
// Our SignalR hub - signals frontend that new data has arrived
app.MapHub<LogHub>("/loghub");

if (app.Environment.IsDevelopment())
{
  app.MapScalarApiReference();
}

static string GetLocalIPAddress()
{
  var host = Dns.GetHostEntry(Dns.GetHostName());
  foreach (var ip in host.AddressList)
  {
    if (ip.AddressFamily == AddressFamily.InterNetwork)
    {
      return ip.ToString();
    }
  }
  throw new System.Net.Sockets.SocketException(1, "No network adapters with an IPv4 address in the system!");
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////
/// Here we will map our Minimal API Endpoints
app.MapGet("/api/protected", [RequireApiKey] () => "This endpoint is protected by API Key")
.RequireApiKey()
.WithName("GetProtectedData")
.WithOpenApi();

// GET LOG - receives a new Log Entry by parsing the GET URL. I know a GET method isn't "right",
// but it is a good fit for many small-footprint IoT boards with limited power
app.MapGet("api/Log/{message}", async (string message, HttpContext context) =>
{
  try
  {
    await HandleLogRequestAsync(context, HttpUtility.UrlDecode(message), app.Services);

    return Results.Ok($"{DateTime.Now} : {message}");
  }
  catch (Exception ex)
  {
    return Results.Problem(ex.Message);
  }
})
.WithName("Log")
.WithOpenApi();

// Get LogCount - mostly used to chech that the Databasefile is found :-)
app.MapGet("api/LogCount", async (HttpContext context) =>
{
  try
  {
    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContextLogData>();
    int count = await db.LogEntries.CountAsync();
    return Results.Ok($"Database LogEntries Count: {count}");
  }
  catch (Exception ex)
  {
    return Results.Problem(
        detail: $"An error occurred while retrieving the log count. Error {ex.Message}",
        statusCode: 500
    );
  }
})
.WithName("LogCount")
.WithOpenApi();

// This is an Exmaple of a slightly modified simple GET, where you offer a special Endpoint for
// a special Application - so you can set default values for just that APP. Here we set the Sender attribute
app.MapGet("api/Log/ExampleApp/{message}", async (string message, HttpContext context) =>
{
  // Here the last arguemnt(optional) is the Sender - we set it to "Magical App", this mean you can 
  // make simple bash/shell/powershell etc with special endpoints with prearranged attributes if you like
  await HandleLogRequestAsync(context, HttpUtility.UrlDecode(message), app.Services, "Magical App");

  return $"{DateTime.Now} : {message}";
})
.WithName("LogExampleApp")
.WithOpenApi();

// Receive new LogEntry via Form POST
app.MapPost("api/Log/", async (HttpRequest request, HttpContext context) =>
{
  try
  {
    var body = new StreamReader(request.Body);
    string postData = await body.ReadToEndAsync();

    await HandleLogRequestAsync(context, postData, app.Services);
    return Results.Ok(postData);
  }
  catch (Exception ex)
  {
    return Results.Problem(detail: ex.Message, statusCode: 500);
  }
});

// Receive new LogEntry via JSON POST - this is the preferred method for most applications
// Modern version ment for non IoT devices
app.MapPost("api/LogEx/", async (LogEntry logEntry, HttpContext context) =>
{
  try
  {
    await HandleLogRequestAsyncEx(context, logEntry, app.Services);
    return Results.Ok(logEntry);
  }
  catch (Exception ex)
  {
    return Results.Problem(detail: ex.Message, statusCode: 500);
  }
});

// ECHO function - you can use this to check that WiFi is working as expected(ie send something known,
// and compare the returned result. 
app.MapGet("api/Echo/{message}", (string message) => message)
.WithName("Echo")
.WithOpenApi();

// Get LocalTime - many small and simple devices don't have any Real time clock, or maybe they do have a RTC chip
// but no way to set the time at Boot - so I use this API Call to set the date/time on my RPI Pico W:s
app.MapGet("api/GetLocalTime", string () =>
{
  // Local time for Sweden - I use this for my RPi Pico W:s
  // Change CultureInfo to fit your needs
  try
  {
    var currentTime = DateTimeOffset.Now;
    var formattedTime = currentTime.ToString("O", new CultureInfo("sv-SE"));

    return formattedTime;
  }
  catch
  {
    return "";
  }
});
// Common logic for handling the Log-requests

static async Task SaveLogEntryAsync(IServiceProvider services, LogEntry logEntry)
{
  try
  {
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContextLogData>();
    await db.LogEntries.AddAsync(logEntry);
    await db.SaveChangesAsync();
  }
  catch (Exception ex) // Don't expect the simple IoT devices to handle any errors
  {
    Console.WriteLine($"SaveLogEntryAsync Error: {ex.Message} ");
  }
}
// Main method for handling saving of LogEntries 
static async Task HandleLogRequestAsync(HttpContext context, string postData, IServiceProvider services, string sender = "")
{
  try
  {
    var callerIpAddress = context.Connection.RemoteIpAddress?.ToString();

    using var scope = services.CreateScope();
    var logHub = scope.ServiceProvider.GetRequiredService<LogHub>();

    LogEntry logEntry = LogEntry.Load(postData);
    logEntry.IPAdress = callerIpAddress;

    if (logEntry.LogDate == null)
      logEntry.LogDate = DateTime.Now;
    if (sender != "")
      logEntry.Sender = sender;

    if (string.IsNullOrEmpty(logEntry.LogType))
      logEntry.LogType = "Info";

    await SaveLogEntryAsync(services, logEntry);

    await logHub.SendLogUpdateAsync();
  }
  catch (Exception ex) // Don't expect the simple IoT devices to handle any errors
  {
    Console.WriteLine($"HandleLogRequestAsync Error: {ex.Message} ");
  }
}
// Main method for handling saving of LogEntries for modern devices
static async Task HandleLogRequestAsyncEx(HttpContext context, LogEntry logEntry, IServiceProvider services, string sender = "")
{
  try
  {
    var callerIpAddress = context.Connection.RemoteIpAddress?.ToString();

    using var scope = services.CreateScope();
    var logHub = scope.ServiceProvider.GetRequiredService<LogHub>();

    logEntry.IPAdress = callerIpAddress;

    if (logEntry.LogDate == null)
      logEntry.LogDate = DateTime.Now;
    if (sender != "")
      logEntry.Sender = sender;

    if (string.IsNullOrEmpty(logEntry.LogType))
      logEntry.LogType = "Info";

    await SaveLogEntryAsync(services, logEntry);

    await logHub.SendLogUpdateAsync();
  }
  catch (Exception ex) // Don't expect the simple IoT devices to handle any errors
  {
    Console.WriteLine($"HandleLogRequestAsync Error: {ex.Message} ");
  }
}
/////////////////////////////////////////////////////////////////////////////////////////////////////////

#pragma warning disable S6966 // Awaitable method should be used
app.Run();
#pragma warning restore S6966 // Awaitable method should be used
