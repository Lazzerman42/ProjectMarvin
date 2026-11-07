using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Marvin.Common;
using Microsoft.AspNetCore.SignalR.Client;

namespace ProjectMarvinAPI.Tests;

/// <summary>Echo, GetLocalTime, LogCount, the ExampleApp endpoint, the API key check and the SignalR hub.</summary>
public class UtilityEndpointTests(MarvinApiFactory factory) : ApiTestBase(factory)
{
  [Fact]
  public async Task Echo_returns_the_message_as_plain_text()
  {
    var response = await Client.GetAsync("api/Echo/Ping123");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal("Ping123", await response.Content.ReadAsStringAsync());
    Assert.Empty(await Factory.GetLogEntriesAsync());
  }

  [Fact]
  public async Task GetLocalTime_returns_round_trip_local_time()
  {
    var response = await Client.GetAsync("api/GetLocalTime");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    var time = DateTimeOffset.ParseExact(body, "O", CultureInfo.InvariantCulture);
    Assert.InRange(time, DateTimeOffset.Now.AddSeconds(-10), DateTimeOffset.Now.AddSeconds(10));
    Assert.Equal(DateTimeOffset.Now.Offset, time.Offset);
  }

  [Fact]
  public async Task LogCount_reports_the_number_of_entries()
  {
    await Client.GetAsync("api/Log/One");
    await Client.GetAsync("api/Log/Two");

    var response = await Client.GetAsync("api/LogCount");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Database LogEntries Count: 2", await response.Content.ReadFromJsonAsync<string>());
  }

  [Fact]
  public async Task ExampleApp_sets_the_sender()
  {
    var response = await Client.GetAsync("api/Log/ExampleApp/Special+entry");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    Assert.EndsWith(" : Special+entry", await response.Content.ReadAsStringAsync());

    var entry = await Factory.GetSingleLogEntryAsync();
    Assert.Equal("Magical App", entry.Sender);
    Assert.Equal("Special entry", entry.Message);
    Assert.Equal("Info", entry.LogType);
  }

  [Fact]
  public async Task ExampleApp_overrides_sender_from_json()
  {
    var json = """{"Sender":"Someone else","Message":"Hi"}""";

    await Client.GetAsync($"api/Log/ExampleApp/{Uri.EscapeDataString(json)}");

    Assert.Equal("Magical App", (await Factory.GetSingleLogEntryAsync()).Sender);
  }

  [Fact]
  public async Task Protected_endpoint_requires_an_api_key()
  {
    var response = await Client.GetAsync("api/protected");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Protected_endpoint_rejects_a_wrong_api_key()
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/protected");
    request.Headers.Add("X-API-Key", "wrong-key");

    var response = await Client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Unknown_route_returns_404()
  {
    var response = await Client.GetAsync("api/DoesNotExist");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Theory]
  [InlineData("get")]
  [InlineData("post")]
  [InlineData("postEx")]
  public async Task Saving_a_log_entry_notifies_hub_clients(string method)
  {
    await using var hub = Factory.CreateHubConnection();
    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    hub.On("ReceiveLogUpdate", () => received.TrySetResult());
    await hub.StartAsync();

    var response = method switch
    {
      "get" => await Client.GetAsync("api/Log/Notify"),
      "post" => await Client.PostAsync("api/Log/", new StringContent("Notify")),
      _ => await Client.PostAsJsonAsync("api/LogEx/", new LogEntry("Notify"))
    };

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
  }
}
