using System.Net;
using System.Net.Http.Json;
using System.Text;
using Marvin.Common;
using Microsoft.AspNetCore.Mvc;
using ProjectMarvinAPI.Security;

namespace ProjectMarvinAPI.Tests;

/// <summary>Input validation, the LAN-only IP filter, the API key check and response headers.</summary>
public class HardeningTests(MarvinApiFactory factory) : ApiTestBase(factory)
{
  [Fact]
  public async Task Get_with_too_long_message_is_rejected()
  {
    var message = new string('x', LogEntry.MaxMessageLength + 1);

    var response = await Client.GetAsync($"api/Log/{message}");

    await AssertValidationProblemAsync(response, nameof(LogEntry.Message));
  }

  [Fact]
  public async Task Message_at_the_limit_is_accepted()
  {
    var message = new string('x', LogEntry.MaxMessageLength);

    var response = await Client.PostAsync("api/Log/", new StringContent(message));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(message, (await Factory.GetSingleLogEntryAsync()).Message);
  }

  [Fact]
  public async Task Post_with_too_long_message_is_rejected()
  {
    var response = await Client.PostAsync("api/Log/", new StringContent(new string('x', LogEntry.MaxMessageLength + 1)));

    await AssertValidationProblemAsync(response, nameof(LogEntry.Message));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("""{"Sender":"Pico"}""")]
  [InlineData("""{"Sender":"Pico","Message":""}""")]
  public async Task Post_without_message_is_rejected(string body)
  {
    var response = await Client.PostAsync("api/Log/", new StringContent(body, Encoding.UTF8, "application/json"));

    await AssertValidationProblemAsync(response, nameof(LogEntry.Message));
  }

  [Fact]
  public async Task Too_long_sender_is_rejected()
  {
    var json = $$"""{"Sender":"{{new string('s', LogEntry.MaxSenderLength + 1)}}","Message":"Hi"}""";

    var response = await Client.GetAsync($"api/Log/{Uri.EscapeDataString(json)}");

    await AssertValidationProblemAsync(response, nameof(LogEntry.Sender));
  }

  [Fact]
  public async Task LogEx_with_too_long_log_type_is_rejected()
  {
    var response = await Client.PostAsJsonAsync("api/LogEx/", new { Message = "Hi", LogType = new string('t', LogEntry.MaxLogTypeLength + 1) });

    await AssertValidationProblemAsync(response, nameof(LogEntry.LogType));
  }

  [Fact]
  public async Task LogEx_without_message_is_rejected()
  {
    var response = await Client.PostAsJsonAsync("api/LogEx/", new { Sender = "Modern" });

    await AssertValidationProblemAsync(response, nameof(LogEntry.Message));
  }

  [Fact]
  public async Task LogEx_ignores_a_client_supplied_id()
  {
    await Client.PostAsJsonAsync("api/LogEx/", new LogEntry("First"));
    var first = await Factory.GetSingleLogEntryAsync();

    var response = await Client.PostAsync("api/LogEx/",
        new StringContent($$"""{"Id":{{first.Id}},"Message":"Second"}""", Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(["First", "Second"], (await Factory.GetLogEntriesAsync()).Select(e => e.Message));
  }

  [Theory]
  [InlineData("8.8.8.8")]
  [InlineData("172.32.0.1")]
  [InlineData("2001:4860:4860::8888")]
  public async Task Callers_outside_the_lan_are_blocked(string ip)
  {
    foreach (var path in new[] { "api/Log/Intruder", "api/Echo/hi", "api/GetLocalTime", "loghub/negotiate" })
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Add(MarvinApiFactory.RemoteIpHeader, ip);

      var response = await Client.SendAsync(request);

      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    Assert.Empty(await Factory.GetLogEntriesAsync());
  }

  [Theory]
  [InlineData("127.0.0.1", true)]
  [InlineData("::1", true)]
  [InlineData("10.1.2.3", true)]
  [InlineData("172.16.0.1", true)]
  [InlineData("172.31.255.255", true)]
  [InlineData("192.168.0.10", true)]
  [InlineData("::ffff:192.168.0.10", true)]
  [InlineData("fd12:3456::1", true)]
  [InlineData("fe80::1", true)]
  [InlineData("172.15.0.1", false)]
  [InlineData("172.32.0.1", false)]
  [InlineData("192.169.0.1", false)]
  [InlineData("8.8.8.8", false)]
  [InlineData("::ffff:8.8.8.8", false)]
  [InlineData("2001:db8::1", false)]
  public void Local_network_addresses_are_recognized(string ip, bool expected)
  {
    Assert.Equal(expected, IpFilterMiddleware.IsLocalNetworkAddress(IPAddress.Parse(ip)));
  }

  [Fact]
  public void Missing_address_is_not_local()
  {
    Assert.False(IpFilterMiddleware.IsLocalNetworkAddress(null));
  }

  [Fact]
  public async Task Ipv4_mapped_caller_address_is_stored_as_ipv4()
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/Log/Mapped");
    request.Headers.Add(MarvinApiFactory.RemoteIpHeader, "::ffff:192.168.1.77");

    await Client.SendAsync(request);

    Assert.Equal("192.168.1.77", (await Factory.GetSingleLogEntryAsync()).IPAdress);
  }

  [Fact]
  public async Task Protected_endpoint_accepts_the_right_api_key()
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/protected");
    request.Headers.Add(ApiKeyEndpointFilter.HeaderName, MarvinApiFactory.TestApiKey);

    var response = await Client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("This endpoint is protected by API Key", await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Protected_endpoint_rejects_a_key_with_a_matching_prefix()
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/protected");
    request.Headers.Add(ApiKeyEndpointFilter.HeaderName, MarvinApiFactory.TestApiKey + "x");

    var response = await Client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Responses_forbid_content_sniffing()
  {
    var response = await Client.GetAsync("api/Echo/<script>");

    Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
  }

  private async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
  {
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.NotNull(problem);
    Assert.Contains(field, problem.Errors.Keys);
    Assert.Empty(await Factory.GetLogEntriesAsync());
  }
}
