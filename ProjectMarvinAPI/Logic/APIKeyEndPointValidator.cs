
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class RequireApiKeyAttribute : Attribute
{
}

public class ApiKeyEndpointFilter : IEndpointFilter
{
	private const string APIKEY = "X-API-Key";
	private readonly IConfiguration _configuration;

	public ApiKeyEndpointFilter(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		if (!context.HttpContext.Request.Headers.TryGetValue(APIKEY, out var extractedApiKey))
		{
			return Results.Unauthorized();
		}

		var apiKey = _configuration.GetValue<string>("ApiKey");

		if (!apiKey.Equals(extractedApiKey))
		{
			return Results.Unauthorized();
		}

		return await next(context);
	}
}

public static class EndpointRouteBuilderExtensions
{
	public static RouteHandlerBuilder RequireApiKey(this RouteHandlerBuilder builder)
	{
		return builder.AddEndpointFilter<ApiKeyEndpointFilter>();
	}
}


