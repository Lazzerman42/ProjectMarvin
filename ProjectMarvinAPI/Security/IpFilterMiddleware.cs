using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ProjectMarvinAPI.Security;

public sealed class IpFilterOptions
{
  public const string SectionName = "IpFilter";

  /// <summary>When true (default), only loopback and private network addresses may call the API.</summary>
  public bool Enabled { get; set; } = true;
}

/// <summary>
/// Answers 403 to callers outside the local network (loopback, RFC 1918 IPv4, IPv6 unique-local
/// and link-local addresses). The API has no authentication, so it must not be reachable from the internet.
/// </summary>
public sealed class IpFilterMiddleware(RequestDelegate next, IOptions<IpFilterOptions> options, ILogger<IpFilterMiddleware> logger)
{
  public async Task InvokeAsync(HttpContext context)
  {
    if (options.Value.Enabled && !IsLocalNetworkAddress(context.Connection.RemoteIpAddress))
    {
      logger.LogWarning("Blocked request from {RemoteIp} to {Path}", context.Connection.RemoteIpAddress, context.Request.Path);
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsync("Access denied. Only LAN IPs are allowed for API calls.");
      return;
    }

    await next(context);
  }

  public static bool IsLocalNetworkAddress(IPAddress? ip)
  {
    if (ip is null)
    {
      return false;
    }

    if (ip.IsIPv4MappedToIPv6)
    {
      ip = ip.MapToIPv4();
    }

    if (IPAddress.IsLoopback(ip))
    {
      return true;
    }

    if (ip.AddressFamily == AddressFamily.InterNetwork)
    {
      var bytes = ip.GetAddressBytes();
      return bytes[0] == 10
          || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
          || (bytes[0] == 192 && bytes[1] == 168);
    }

    return ip.AddressFamily == AddressFamily.InterNetworkV6
        && (ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal);
  }
}
