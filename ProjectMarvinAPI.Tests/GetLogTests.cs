using System.Net;
using System.Net.Http.Json;

namespace ProjectMarvinAPI.Tests;

/// <summary>GET api/Log/{message} - the "post a log with a plain GET" endpoint used by the simplest devices.</summary>
public class GetLogTests(MarvinApiFactory factory) : ApiTestBase(factory)
{
  [Fact]
  public async Task Plain_message_is_saved_with_defaults()
  {
    var before = DateTime.Now.AddSeconds(-5);

    var response = await Client.GetAsync("api/Log/Hello");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Hello", entry.Message);
    Assert.Equal("Info", entry.LogType);
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, entry.IPAdress);
    Assert.Null(entry.Sender);
    Assert.NotNull(entry.LogDate);
    Assert.InRange(entry.LogDate.Value, before, DateTime.Now.AddSeconds(5));
  }

  [Fact]
  public async Task Response_is_a_json_string_with_timestamp_and_message()
  {
    var response = await Client.GetAsync("api/Log/Hello");

    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    var body = await response.Content.ReadFromJsonAsync<string>();
    Assert.NotNull(body);
    Assert.EndsWith(" : Hello", body);
    Assert.True(DateTime.TryParse(body[..body.IndexOf(" : ", StringComparison.Ordinal)], out _), body);
  }

  [Theory]
  [InlineData("Hello+World", "Hello World")]
  [InlineData("Hello%20World", "Hello World")]
  [InlineData("Temp%3A%2021%C2%B0C", "Temp: 21°C")]
  [InlineData("R%C3%A4ksm%C3%B6rg%C3%A5s", "Räksmörgås")]
  public async Task Url_encoded_message_is_decoded(string path, string expected)
  {
    var response = await Client.GetAsync($"api/Log/{path}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal(expected, entry.Message);
  }

  [Fact]
  public async Task MicroPython_example_payload_keeps_swedish_characters()
  {
    // logapi.py URL-encodes the message with ISO-8859-1 codes (å = %E5), wraps it in JSON and
    // URL-encodes the whole JSON again
    var json = """{"LogType":"Info","Sender":"PythonMachine1","Message":"Hej%20d%E5%20%C4ngen"}""";

    var response = await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("PythonMachine1", entry.Sender);
    Assert.Equal("Hej då Ängen", entry.Message);
  }

  [Fact]
  public async Task Latin1_encoded_plain_message_is_decoded()
  {
    var response = await Client.GetAsync("api/Log/V%E4xthus%20%F6ppnat");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Växthus öppnat", (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task Url_encoded_json_fills_all_fields()
  {
    var json = """{"Sender":"Pico-1","LogType":"Warning","Message":"Temp high"}""";

    var response = await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Pico-1", entry.Sender);
    Assert.Equal("Warning", entry.LogType);
    Assert.Equal("Temp high", entry.Message);
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, entry.IPAdress);
  }

  [Fact]
  public async Task Json_property_names_are_case_insensitive()
  {
    var json = """{"sender":"Pico-2","logtype":"Error","message":"Boom"}""";

    await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Pico-2", entry.Sender);
    Assert.Equal("Error", entry.LogType);
    Assert.Equal("Boom", entry.Message);
  }

  [Fact]
  public async Task Json_without_log_type_defaults_to_info()
  {
    var json = """{"Sender":"Pico-3","Message":"No type","LogType":null}""";

    await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Info", entry.LogType);
  }

  [Fact]
  public async Task Json_log_date_is_kept()
  {
    var json = """{"Message":"Dated","LogDate":"2024-05-01T12:34:56"}""";

    await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal(new DateTime(2024, 5, 1, 12, 34, 56), entry.LogDate);
  }

  [Fact]
  public async Task Client_supplied_ip_address_is_replaced_by_the_callers()
  {
    var json = """{"Message":"Spoof","IPAdress":"8.8.8.8"}""";

    await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal(MarvinApiFactory.DefaultRemoteIp, entry.IPAdress);
  }

  [Fact]
  public async Task Caller_ip_address_is_stored()
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/Log/FromOtherDevice");
    request.Headers.Add(MarvinApiFactory.RemoteIpHeader, "10.0.0.7");

    await Client.SendAsync(request);

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("10.0.0.7", entry.IPAdress);
  }

  [Fact]
  public async Task Route_is_case_insensitive()
  {
    // The example clients use "/Api/Log/"
    var response = await Client.GetAsync("Api/Log/Mixed");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Mixed", (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task Each_request_adds_a_new_entry()
  {
    await Client.GetAsync("api/Log/One");
    await Client.GetAsync("api/Log/Two");
    await Client.GetAsync("api/Log/Three");

    var entries = await Factory.GetLogEntriesAsync();
    Assert.Equal(["One", "Two", "Three"], entries.Select(e => e.Message));
  }
}
