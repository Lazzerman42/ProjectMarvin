using ProjectMarvinAPI.Services;

namespace ProjectMarvinAPI.Tests;

public class UrlTextTests
{
  [Theory]
  [InlineData("Hello+World", "Hello World")]
  [InlineData("Hello%20World", "Hello World")]
  [InlineData("R%C3%A4ksm%C3%B6rg%C3%A5s", "Räksmörgås")]
  [InlineData("R%E4ksm%F6rg%E5s", "Räksmörgås")]
  [InlineData("Räksmörgås", "Räksmörgås")]
  [InlineData("100%25", "100%")]
  [InlineData("50% off", "50% off")]
  [InlineData("", "")]
  public void Decodes_utf8_and_falls_back_to_latin1(string input, string expected)
  {
    Assert.Equal(expected, UrlText.Decode(input));
  }
}
