using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectMarvin.Components;
using ProjectMarvin.Components.Account;
using ProjectMarvin.Data;
using ProjectMarvin.Hubs;
using ProjectMarvin.Logic;
using System.Globalization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add services to the container.
builder.Services.AddRazorComponents()
		.AddInteractiveServerComponents();

// Our Services
//builder.Services.AddSingleton<LogEntries>(); // not used now, we use SQLite
builder.Services.AddSingleton<LogHub>();

builder.Services.AddQuickGridEntityFrameworkAdapter();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
		{
			options.DefaultScheme = IdentityConstants.ApplicationScheme;
			options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
		})
		.AddIdentityCookies();

// DBContextFactory for SQLite Identity database
var IdentityConnection = builder.Configuration.GetConnectionString("IdentityConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContextIdentity>(options =>
		options.UseSqlite(IdentityConnection)); builder.Services.AddDatabaseDeveloperPageExceptionFilter();
// Since we are using DbContextFactory here, we make this "helper" for the Identity-components to get a DbContext from The Factory
builder.Services.AddScoped<ApplicationDbContextIdentity>(p => p.GetRequiredService<IDbContextFactory<ApplicationDbContextIdentity>>().CreateDbContext());

// DBContextFactory for SQLite LogData database
var LogDataConnection = builder.Configuration.GetConnectionString("LogDataConnection");
builder.Services.AddDbContextFactory<ApplicationDbContextLogData>(options =>
		options.UseSqlite(LogDataConnection));

// Add Identitycore for our ApplicationUser, no mail confirmation 
builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
		.AddEntityFrameworkStores<ApplicationDbContextIdentity>()
		.AddSignInManager()
		.AddDefaultTokenProviders();

// No email-server so Accounts won't be Email verified
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

app.UseMiddleware<IPFilterMiddleware>();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
	app.UseMigrationsEndPoint();
}
//else
//{
//	app.UseExceptionHandler("/Error", createScopeForErrors: true);
//	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//	app.UseHsts();
//}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Our SignalR hub - signals frontend that new data has arrived
app.MapHub<LogHub>("/loghub");

app.MapRazorComponents<App>()
		.AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

//////////////////////////////////////////////////////////////////////////////////
/// Here we will map our Minimal API Endpoints
/// 

app.MapGet("/api/protected", [RequireApiKey] () =>
{
	return "This endpoint is protected by API Key";
})
.RequireApiKey()
.WithName("GetProtectedData")
.WithOpenApi();

// GET LOG - receives a new Log Entry by parsing the GET URL. I know a GET method isn't "right",
// but it is a good fit for many small-footprint IoT boards with limited power
app.MapGet("api/Log/{message}", async (string message, HttpContext context) =>
{
	await HandleLogRequestAsync(context, message, app.Services);

	return DateTime.Now + " : " + " : " + message;
})
.WithName("Log")
.WithOpenApi();

// This is an Exmaple of a slightly modified simple GET, where you offer a special Endpoint for
// a special Application - so you can set default values for just that APP. Here we set the Sender attribute
app.MapGet("api/Log/ExampleApp/{message}", async (string message, HttpContext context) =>
{
	// Here the last arguemnt(optional) is the Sender - we set it to "Magical App", this mean you can 
	// make simple bash/shell/powershell etc with special endpoints with prearranged attributes if you like
	await HandleLogRequestAsync(context, message, app.Services, "Sent by The magical App");

	return DateTime.Now + " : " + " : " + message;
})
.WithName("LogExampleApp")
.WithOpenApi();

// Receive new LogEntry via Form POST
app.MapPost("api/Log/", async (HttpRequest request, HttpContext context) =>
{
	var body = new StreamReader(request.Body);
	string postData = await body.ReadToEndAsync();
	Dictionary<string, dynamic> keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(postData) ?? new Dictionary<string, dynamic>();

	var callerIpAddress = context.Connection.RemoteIpAddress?.ToString();

	await HandleLogRequestAsync(context, postData, app.Services);
	return await Task.FromResult<string>(postData);
});

// ECHO function - you can use this to check that WiFi is working as expected(ie send something known,
// and compare the returned result. 
app.MapGet("api/Echo/{message}", (string message, HttpContext context) =>
{
	// var callerIpAddress = context.Connection.RemoteIpAddress?.ToString();
	return message;
})
.WithName("Echo")
.WithOpenApi();

// Get LocalTime - many small and simple devices don't have any Real time clock, or maybe they do have a RTC chip
// but no way to set the time at Boot - so I use this API Call to set the date/time on my RPI Pico W:s
app.MapGet("api/GetLocalTime", string (HttpContext context) =>
{
	// Local time for Sweden - I use this for my RPi Pico W:s
	// Change CultureInfo to fit your needs
	var currentTime = DateTimeOffset.Now;
	var formattedTime = currentTime.ToString("O", new CultureInfo("sv-SE"));

	return formattedTime;
});
// Common logic for handling the Log-requests

static async Task SaveLogEntryAsync(IServiceProvider services, LogEntry logEntry)
{
	using var scope = services.CreateScope();
	var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContextLogData>();
	await db.LogEntries.AddAsync(logEntry);
	await db.SaveChangesAsync();

}
// Main method for handling saving of LogEntries 
static async Task HandleLogRequestAsync(HttpContext context, string postData, IServiceProvider services, string sender = "")
{
	var callerIpAddress = context.Connection.RemoteIpAddress?.ToString();

	using (var scope = services.CreateScope())
	{
		var logHub = scope.ServiceProvider.GetRequiredService<LogHub>();

		//var logEntries = scope.ServiceProvider.GetRequiredService<LogEntries>();
		LogEntry logEntry = LogEntry.Load(postData);
		logEntry.IPAdress = callerIpAddress;

		if (logEntry.LogDate == null)
			logEntry.LogDate = DateTime.Now;
		if (sender != "")
			logEntry.Sender = sender;

		if (string.IsNullOrEmpty(logEntry.LogType))
			logEntry.LogType = "Info";

		//logEntries?.messages?.Add(logEntry);

		await SaveLogEntryAsync(services, logEntry);

		await logHub.SendLogUpdate();
	}
}
//////////////////////////////////////////////////////////////////////////////////
app.Run();