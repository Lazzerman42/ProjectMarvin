using Microsoft.AspNetCore.SignalR;

namespace ProjectMarvin.Hubs
{
	/// <summary>
	/// SignalR LogHub, notifies Client(Web UI) that new data has arrived
	/// </summary>
	public class LogHub : Hub
	{
		public async Task SendLogUpdate()
		{
			await Clients.All.SendAsync("ReceiveLogUpdate");
			Console.WriteLine("I Loghubben ");
		}
	}
}
