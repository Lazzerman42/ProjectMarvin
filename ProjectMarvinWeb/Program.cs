using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectMarvin.Components;
using ProjectMarvin.Components.Account;
using ProjectMarvin.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
  options.DefaultScheme = IdentityConstants.ApplicationScheme;
  options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
    .AddIdentityCookies();

// DbContextFactory for the SQLite Identity database
var identityConnection = builder.Configuration.GetConnectionString("IdentityConnection")
    ?? throw new InvalidOperationException("Connection string 'IdentityConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContextIdentity>(options => options.UseSqlite(identityConnection));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
// The Identity components need a scoped DbContext, so hand them one from the factory
builder.Services.AddScoped(p => p.GetRequiredService<IDbContextFactory<ApplicationDbContextIdentity>>().CreateDbContext());

// DbContextFactory for the SQLite log database (shared with the API, which writes to it)
var logDataConnection = builder.Configuration.GetConnectionString("LogDataConnection")
    ?? throw new InvalidOperationException("Connection string 'LogDataConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContextLogData>(options =>
    options.UseSqlite(logDataConnection),
    ServiceLifetime.Scoped);

// No mail server, so accounts aren't email-confirmed. Lock an account for a while after
// repeated failed logins, since the site is reachable from the internet.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
  options.SignIn.RequireConfirmedAccount = false;
  // Version 3 adds the AspNetUserPasskeys table, so users can sign in with passkeys
  options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
  options.Lockout.AllowedForNewUsers = true;
  options.Lockout.MaxFailedAccessAttempts = 5;
  options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<ApplicationDbContextIdentity>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Bring the login database up to date (e.g. the passkey table) when a new version is deployed.
// Only the Identity database: the log database is shared with the API and is left alone.
await using (var identityDb = await app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContextIdentity>>().CreateDbContextAsync())
{
  await identityDb.Database.MigrateAsync();
}

// Long-lived caching for static files. Stylesheets and scripts are referenced through @Assets[...],
// whose URLs change with the content, so they can be cached as immutable.
app.Use((context, next) =>
{
  context.Response.OnStarting(() =>
  {
    var extension = Path.GetExtension(context.Request.Path.Value);
    if (context.Response.StatusCode == StatusCodes.Status200OK && !string.IsNullOrEmpty(extension))
    {
      var headers = context.Response.Headers;
      headers.AltSvc = "h2=\":443\"; ma=86400, h3=\":443\"; ma=86400";

      switch (extension.ToLowerInvariant())
      {
        case ".css" or ".js" or ".woff" or ".woff2" or ".ttf":
          headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
          headers.CacheControl = "public, max-age=31536000, immutable";
          break;

        case ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" or ".webp":
          headers.Expires = DateTime.UtcNow.AddDays(30).ToString("R");
          headers.CacheControl = "public, max-age=2592000";
          break;
      }
    }
    return Task.CompletedTask;
  });

  return next(context);
});

app.Use((context, next) =>
{
  var headers = context.Response.Headers;
  headers.XFrameOptions = "DENY";
  headers.XContentTypeOptions = "nosniff";
  headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
  headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
  return next(context);
});

if (app.Environment.IsDevelopment())
{
  app.UseMigrationsEndPoint();
}
else
{
  app.UseExceptionHandler("/Error", createScopeForErrors: true);
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}

app.UseHttpsRedirection();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Additional endpoints required by the Identity /Account Razor components
app.MapAdditionalIdentityEndpoints();

await app.RunAsync();
