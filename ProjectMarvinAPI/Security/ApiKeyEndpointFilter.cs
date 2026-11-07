using System.Security.Cryptography;
using System.Text;

namespace ProjectMarvinAPI.Security;

/// <summary>
/// Rejects requests whose <c>X-API-Key</c> header doesn't match the configured <c>ApiKey</c>.
/// When no key is configured, every request is rejected.
/// </summary>
public sealed class ApiKeyEndpointFilter(IConfiguration configuration) : IEndpointFilter
{
  public const string HeaderName = "X-API-Key";
  public const string ConfigurationKey = "ApiKey";

  public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
  {
    var expected = configuration[ConfigurationKey];
    var supplied = context.HttpContext.Request.Headers[HeaderName];

    if (string.IsNullOrEmpty(expected) || supplied.Count != 1 || !FixedTimeEquals(expected, supplied[0]))
    {
      return Results.Unauthorized();
    }

    return await next(context);
  }

  // Constant-time comparison of fixed-length hashes, so the response time reveals neither how much
  // of the key was right nor its length
  private static bool FixedTimeEquals(string expected, string? supplied) =>
      supplied is not null &&
      CryptographicOperations.FixedTimeEquals(
          SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
          SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
}

public static class ApiKeyEndpointExtensions
{
  /// <summary>Requires a valid <c>X-API-Key</c> header for this endpoint.</summary>
  public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
      builder.AddEndpointFilter<TBuilder, ApiKeyEndpointFilter>();
}
