using Marvin.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using ProjectMarvin.Data;

namespace ProjectMarvin.Components.Pages;

/// <summary>
/// (Thanks AI)
/// Summary of Home.razor.cs:
/// This file defines a Blazor component called 'Home' that implements a log viewing and management system using SignalR for real-time updates and Entity Framework Core for database interactions.
/// Key Features:
/// Uses QuickGrid for displaying log entries with pagination.
/// Implements SignalR for real-time updates of log data.
/// Provides filtering capabilities for log messages and senders.
/// Offers a distinct view option to show only the latest log entry for each unique IP address and sender combination.
/// Includes a dialog for confirming log data deletion.
/// Main Components:
/// SignalR hub connection for real-time updates
/// Entity Framework Core context factory for database operations
/// QuickGrid for displaying log entries
/// Filtering logic for log entries
/// Toggle for distinct view of log entries
/// Dialog for confirming log deletion
/// Notable Methods:
/// OnInitializedAsync: Initializes SignalR connection
/// UpdateData: Refreshes the QuickGrid data
/// DeleteLogTableData: Clears all log entries from the database
/// FilteredLog: Property that returns filtered log entries based on search criteria and distinct view toggle
/// The component also implements IAsyncDisposable for proper resource cleanup of the SignalR connection and database context.
/// This code demonstrates a robust implementation of a log viewer with real-time updates, filtering, and database interactions in a Blazor server application.
/// </summary>
public partial class Home : ComponentBase, IAsyncDisposable
{
  PaginationState pagination = new PaginationState { ItemsPerPage = 42 };
  public bool IsConnected => hubConnection?.State == HubConnectionState.Connected; // For displaying the connected / disconneced symbol

  private HubConnection? hubConnection; // For our SignalR connection to the API

  [Inject]
  public IDbContextFactory<ApplicationDbContextLogData>? _LogDBFactory { get; set; } // Factory is used to get our DBContext
  public ApplicationDbContextLogData? _LogDB; // Will be created from Factory

  [Inject] // Read the SignalR URL from config
  IConfiguration? Configuration { get; set; }
  [Inject]
  public NavigationManager NavMan { get; set; }
  //[Inject]
  //public LogEntries logEntries { get; set; }

  private string? searchMessageFilter = "",
                  searchSenderFilter = "";

  private QuickGrid<LogEntry> myLogGrid; // Our UI QuickGrid reference

  private bool showDialog = false;
  private bool showDistinct = false;
  private void ShowConfirmDialog()
  {
    showDialog = true;
  }
  private void CloseDialog()
  {
    showDialog = false;
  }
  /// <summary>
  /// Displays Are you sure dialog - if YES - Deletes all logdata in SQLite
  /// </summary>
  /// <param name="confirmed"></param>
  /// <returns></returns>
  private async Task HandleConfirmation(bool confirmed)
  {
    showDialog = false;
    if (confirmed)
    {
      await DeleteLogTableData();
    }
  }
  /// <summary>
  /// Sets the Sender Title to include the search keyword
  /// </summary>
  public string searchSenderFilterTitle
  {
    get
    {
      if (!string.IsNullOrEmpty(searchSenderFilter))
        return "Sender : " + searchSenderFilter;
      else
        return "Sender";
    }
  }
  /// <summary>
  /// Sets the Message Title to include the search keyword
  /// </summary>
  public string searchMesssageFilterTitle
  {
    get
    {
      if (!string.IsNullOrEmpty(searchMessageFilter))
        return "Message : " + searchMessageFilter;
      else
        return "Message";
    }
  }
  /// <summary>
  /// Datasource for the Quickgrid. Uses SQLite database and entityframework
  /// </summary>
  IQueryable<LogEntry> FilteredLog
  {
    get
    {
      var result = _LogDB.LogEntries.AsQueryable();
      // If we have Search keywords
      if (!string.IsNullOrEmpty(searchMessageFilter) || !string.IsNullOrEmpty(searchSenderFilter))
      {
        result = result.Where(l =>
            (string.IsNullOrEmpty(searchMessageFilter) || l.Message!.ToUpper().Contains(searchMessageFilter.ToUpper())) &&
            (string.IsNullOrEmpty(searchSenderFilter) || l.Sender!.ToUpper().Contains(searchSenderFilter.ToUpper()))
        );
      }
      // Uses IPAdress + Sender to get a list of Distinct Log "devices", diplays there latest post
      if (showDistinct) // Toggled by Button in UI
      {
        var latestDates = _LogDB.LogEntries
                  .GroupBy(l => new { l.IPAdress, l.Sender })
                  .Select(g => new
                  {
                    g.Key.IPAdress,
                    g.Key.Sender,
                    LatestDate = g.Max(l => l.LogDate)
                  }).AsQueryable();
        // The resulting list here contains all data from the LogEntry, could be used to set an alarm 
        // if the last logPost is older than XXX....
        result = (IQueryable<LogEntry>)_LogDB.LogEntries
         .Where(l => latestDates.Any(ld =>
             ld.IPAdress == l.IPAdress &&
             ld.Sender == l.Sender &&
             ld.LatestDate == l.LogDate))
         .OrderByDescending(l => l.LogDate).AsQueryable();
      }
      return result;
    }
  }
  // The ShowDistincts per IP/Sender toggle
  public async Task ShowDistincts()
  {
    showDistinct = !showDistinct; // Toggle view

    await UpdateData();
  }
  protected override async Task OnInitializedAsync()
  {
    await StartLogHub();

    try
    {
      if (hubConnection != null)
      {
        await hubConnection.StartAsync();
        Console.WriteLine("SignalR connected.");
        await UpdateData();
      }
    }
    catch (Exception ex) // If the SignalR/API is NOT started, let the page render before retrying connecting SignalR
    {
      _ = Task.Run(async () =>
     {
       await Task.Delay(10000); // 10 seconds delay to let the page render
       await SignalRRetry();
     });
      Console.WriteLine($"Error connecting to SignalR: {ex.Message} Retrying");
    }
  }
  /// <summary>
  /// Starts our SignalR hub(LogHub) and wires up the "event" when message "ReceiveLogUpdate" is received.
  /// </summary>
  /// <returns></returns>
  private async Task StartLogHub()
  {
    if (_LogDBFactory is not null)
      _LogDB = await _LogDBFactory.CreateDbContextAsync();

    hubConnection = new HubConnectionBuilder()
        .WithUrl(Configuration.GetConnectionString("SignalRAPI"))
        .Build();

    hubConnection.Closed += HubConnection_Closed;

    hubConnection.On("ReceiveLogUpdate", async () =>
    {
      await UpdateData();
    });
  }
  /// <summary>
  /// If Client detects SignalR connection is lost, connect again
  /// </summary>
  /// <param name="arg"></param>
  /// <returns></returns>
  private async Task HubConnection_Closed(Exception? arg)
  {
    await SignalRRetry();

    Console.WriteLine("Couldn't reconnect SignalR - check API/SignalR Hub - retrying");
  }

  private async Task SignalRRetry()
  {
    Console.WriteLine("HUB Closed");
    await UpdateData();

    const int maxRetryAttempts = 15;
    const int delayBetweenAttempts = 5000; // 10 sekunder i millisekunder

    for (int i = 0; i < maxRetryAttempts; i++)
    {
      try
      {
        if (hubConnection != null)
        {
          await hubConnection.StartAsync();
          await UpdateData();
        }
        else
        {
          await StartLogHub();
        }
        Console.WriteLine("Reconnected!");
        await UpdateData();
        return;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Reconnect SignalR Try: {i + 1} Fail: {ex.Message}");

        if (i < maxRetryAttempts - 1)
        {
          await Task.Delay(delayBetweenAttempts);
          await UpdateData();
        }
      }
    }
  }

  /// <summary>
  /// Deletes ALL logposts in the LogEntries Table in SQLite
  /// </summary>
  /// <returns></returns>
  public async Task DeleteLogTableData()
  {
    await _LogDB!.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
    await UpdateData();
  }
  /// <summary>
  /// We must tell the Quickgrid to refresh its data and we must do it on the UI thread
  /// Hence the InvokeAsync code
  /// </summary>
  /// <returns></returns>
  public async Task UpdateData()
  {
    await InvokeAsync(async () =>
    {
      await myLogGrid.RefreshDataAsync();
      StateHasChanged();
    });
  }
  /// <summary>
  /// Make sure we dispose our resources
  /// </summary>
  /// <returns></returns>
  public async ValueTask DisposeAsync()
  {
    if (hubConnection is not null)
    {
      hubConnection.Closed -= HubConnection_Closed;
      await hubConnection.DisposeAsync();
    }
    if (_LogDB is not null)
    {
      await _LogDB.DisposeAsync();
    }
  }
}