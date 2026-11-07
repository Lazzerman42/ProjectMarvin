using System.Text;
using System.Web;

namespace ProjectMarvinAPI.Services;

public static class UrlText
{
  private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  /// <summary>
  /// URL-decodes text ('+' becomes a space). Escapes are read as UTF-8 (%C3%A5 = 'å'); when they
  /// aren't valid UTF-8 they're read as ISO-8859-1 instead (%E5 = 'å'), which is what simple
  /// devices such as the MicroPython example send.
  /// </summary>
  public static string Decode(string text)
  {
    ArgumentNullException.ThrowIfNull(text);

    var bytes = HttpUtility.UrlDecodeToBytes(text, Encoding.UTF8);
    try
    {
      return _strictUtf8.GetString(bytes);
    }
    catch (DecoderFallbackException)
    {
      return Encoding.Latin1.GetString(bytes);
    }
  }
}
