using System.Net;

namespace ProjectMarvin.Logic;

public class IPFilterMiddleware
{
	private readonly RequestDelegate _next;

	public IPFilterMiddleware(RequestDelegate next)
	{
		_next = next;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var path = context.Request.Path.Value?.ToLower();

		// Check if URL-Path contains "/api/"
		if (path != null && path.Contains("/api/"))
		{
			var remoteIp = context.Connection.RemoteIpAddress;
			var isLanIp = IsLanIP(remoteIp);

			if (!isLanIp)
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				await context.Response.WriteAsync("Access denied. Only LAN IPs are allowed for API calls.");
				return;
			}
		}

		await _next(context);
	}

	private bool IsLanIP(IPAddress? ip)
	{
		if (ip == null)
			return false;

		if (IPAddress.IsLoopback(ip))
		{
			Console.WriteLine("Filter: Local IP");
			return true;
		}


		byte[] bytes = ip.GetAddressBytes();
		return (bytes[0] == 10 ||
						(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
						(bytes[0] == 192 && bytes[1] == 168));
	}
}