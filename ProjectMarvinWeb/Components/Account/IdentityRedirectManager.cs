using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using ProjectMarvin.Data;

namespace ProjectMarvin.Components.Account
{
  /// <summary>
  /// Redirects from the statically rendered Identity pages. With BlazorDisableThrowNavigationException
  /// (see the project file) NavigateTo doesn't throw, so these methods return and the caller must
  /// <c>return</c> right after redirecting.
  /// </summary>
  internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
  {
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
      SameSite = SameSiteMode.Strict,
      HttpOnly = true,
      IsEssential = true,
      MaxAge = TimeSpan.FromSeconds(5),
    };

    public void RedirectTo(string? uri)
    {
      uri ??= "";

      // Prevent open redirects. "//evil.example" is a well-formed relative URI, but browsers
      // treat it as a link to another host.
      if (!Uri.IsWellFormedUriString(uri, UriKind.Relative)
          || uri.StartsWith("//", StringComparison.Ordinal)
          || uri.StartsWith("/\\", StringComparison.Ordinal))
      {
        uri = ToLocalPath(uri);
      }

      navigationManager.NavigateTo(uri);
    }

    // An absolute URI on this site becomes relative; anything pointing elsewhere goes to the start page
    private string ToLocalPath(string uri)
    {
      var baseUri = new Uri(navigationManager.BaseUri);
      return Uri.TryCreate(baseUri, uri, out var absolute)
          && absolute.GetLeftPart(UriPartial.Authority) == baseUri.GetLeftPart(UriPartial.Authority)
          && absolute.AbsoluteUri.StartsWith(navigationManager.BaseUri, StringComparison.Ordinal)
              ? navigationManager.ToBaseRelativePath(absolute.AbsoluteUri)
              : "";
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
      var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
      var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
      RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
      context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
      RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);

    public void RedirectToInvalidUser(UserManager<ApplicationUser> userManager, HttpContext context)
        => RedirectToWithStatus("Account/InvalidUser", $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.", context);
  }
}
