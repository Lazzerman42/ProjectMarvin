using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ProjectMarvinAPI.Tests;

/// <summary>POST api/Log/ (raw body) and POST api/LogEx/ (typed JSON).</summary>
public class PostLogTests(MarvinApiFactory factory) : ApiTestBase(factory)
{
  [Fact]
  public async Task Post_json_body_is_parsed_and_echoed()
  {
    var json = """{"sender":"VS Test API","logtype":"Warning","Message":"Here are some important stuff to know..."}""";

    var response = await Client.PostAsync("api/Log/", new StringContent(json, Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(json, await response.Content.ReadFromJsonAsync<string>());

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("VS Test API", entry.Sender);
    Assert.Equal("Warning", entry.LogType);
    Assert.Equal("Here are some important stuff to know...", entry.Message);
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, entry.IPAdress);
    Assert.NotNull(entry.LogDate);
  }

  [Fact]
  public async Task Post_without_trailing_slash_works()
  {
    var response = await Client.PostAsync("Api/Log", new StringContent("""{"Message":"no slash"}""", Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("no slash", (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task Post_plain_text_body_becomes_the_message()
  {
    var response = await Client.PostAsync("api/Log/", new StringContent("Just a line of text", Encoding.UTF8, "text/plain"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Just a line of text", entry.Message);
    Assert.Equal("Info", entry.LogType);
    Assert.Null(entry.Sender);
  }

  [Theory]
  [InlineData("application/x-www-form-urlencoded")]
  [InlineData("application/octet-stream")]
  [InlineData(null)]
  public async Task Post_accepts_any_content_type(string? contentType)
  {
    var content = new ByteArrayContent(Encoding.UTF8.GetBytes("""{"Sender":"Form","Message":"Any type"}"""));
    if (contentType is not null)
    {
      content.Headers.ContentType = new(contentType);
    }

    var response = await Client.PostAsync("api/Log/", content);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Form", entry.Sender);
    Assert.Equal("Any type", entry.Message);
  }

  [Fact]
  public async Task Post_url_encoded_latin1_message_is_decoded()
  {
    // The MicroPython example encodes non-ASCII characters as %XX of their ISO-8859-1 code
    var json = """{"Sender":"Pico","Message":"Temp%20%E5%E4%F6"}""";

    await Client.PostAsync("api/Log/", new StringContent(json, Encoding.UTF8, "application/json"));

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Temp åäö", entry.Message);
  }

  [Fact]
  public async Task Post_utf8_message_is_kept()
  {
    var json = """{"Message":"Räksmörgås"}""";

    await Client.PostAsync("api/Log/", new StringContent(json, Encoding.UTF8, "application/json"));

    Assert.Equal("Räksmörgås", (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task LogEx_saves_entry_and_returns_it()
  {
    var response = await Client.PostAsJsonAsync("api/LogEx/", new { Sender = "Modern", LogType = "Error", Message = "Typed JSON" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var root = doc.RootElement;
    Assert.Equal("Modern", root.GetProperty("sender").GetString());
    Assert.Equal("Error", root.GetProperty("logType").GetString());
    Assert.Equal("Typed JSON", root.GetProperty("message").GetString());
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, root.GetProperty("ipAdress").GetString());
    Assert.Equal(JsonValueKind.String, root.GetProperty("logDate").ValueKind);
    Assert.False(root.TryGetProperty("id", out _));

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Modern", entry.Sender);
    Assert.Equal("Error", entry.LogType);
    Assert.Equal("Typed JSON", entry.Message);
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, entry.IPAdress);
  }

  [Fact]
  public async Task LogEx_defaults_missing_log_type_and_date()
  {
    var response = await Client.PostAsJsonAsync("api/LogEx", new { Message = "Minimal", LogType = (string?)null });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Info", entry.LogType);
    Assert.NotNull(entry.LogDate);
  }

  [Fact]
  public async Task LogEx_does_not_url_decode_the_message()
  {
    await Client.PostAsJsonAsync("api/LogEx/", new { Message = "50%25 done + more" });

    Assert.Equal("50%25 done + more", (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task LogEx_rejects_malformed_json()
  {
    var response = await Client.PostAsync("api/LogEx/", new StringContent("{not json", Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Empty(await Factory.GetLogEntriesAsync());
  }
}
