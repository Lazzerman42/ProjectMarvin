using Microsoft.AspNetCore.SignalR;

namespace ProjectMarvin.Hubs
{
  /// <summary>
  /// SignalR LogHub, notifies Client(Web UI) that new data has arrived
  /// </summary>
  public class LogHub : Hub
  {
    public async Task<Task> SendLogUpdateAsync()
    {
      try
      {
        await Clients.All.SendAsync("ReceiveLogUpdate");
        Console.WriteLine("I Loghubben ");
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"I Loghubben error: {ex.Message} ");
        return Task.FromException(ex);
      }
    }
  }
}
