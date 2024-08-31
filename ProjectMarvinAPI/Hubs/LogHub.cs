using Microsoft.AspNetCore.SignalR;

namespace ProjectMarvinAPI.Hubs;

/// <summary>
/// SignalR LogHub, notifies Client(Web UI) that new data has arrived
/// </summary>
public class LogHub : Hub
{
  public async Task SendLogUpdateAsync()
  {
    if (Clients is not null)
      await Clients.All.SendAsync("ReceiveLogUpdate");
    Console.WriteLine("I Loghubben ");
  }
}
