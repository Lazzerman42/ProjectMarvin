using Marvin.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.SignalR.Client;
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
  private readonly PaginationState _pagination = new() { ItemsPerPage = 42 };
  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected; // For displaying the connected / disconneced symbol

  private HubConnection? _hubConnection; // For our SignalR connection to the API

  [Inject]
  public IDbContextFactory<ApplicationDbContextLogData>? LogDBFactory { get; set; } // Factory is used to get our DBContext
  private ApplicationDbContextLogData? _logDB; // Will be created from Factory

  [Inject] // Read the SignalR URL from config
  IConfiguration? Configuration { get; set; }
  [Inject]
  public NavigationManager? NavMan { get; set; }

  private string? _searchMessageFilter = "",
                           _searchSenderFilter = "";

  private QuickGrid<LogEntry>? _myLogGrid; // Our UI QuickGrid reference

  private bool _showDialog = false;
  private bool _showDistinct = false;
  private void ShowConfirmDialog()
  {
    _showDialog = true;
  }
  private void CloseDialog()
  {
    _showDialog = false;
  }
  /// <summary>
  /// Displays Are you sure dialog - if YES - Deletes all logdata in SQLite
  /// </summary>
  /// <param name="confirmed"></param>
  /// <returns></returns>
  private async Task HandleConfirmationAsync(bool confirmed)
  {
    _showDialog = false;
    if (confirmed)
    {
      await DeleteLogTableDataAsync();
    }
  }
  /// <summary>
  /// Sets the Sender Title to include the search keyword
  /// </summary>
  public string SearchSenderFilterTitle
  {
    get
    {
      if (!string.IsNullOrEmpty(_searchSenderFilter))
        return "Sender : " + _searchSenderFilter;
      else
        return "Sender";
    }
  }
  /// <summary>
  /// Sets the Message Title to include the search keyword
  /// </summary>
  public string SearchMesssageFilterTitle
  {
    get
    {
      if (!string.IsNullOrEmpty(_searchMessageFilter))
        return "Message : " + _searchMessageFilter;
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
      if (_logDB is null)
        return new List<LogEntry>().AsQueryable();

      IQueryable<LogEntry>? result = _logDB.LogEntries.AsQueryable();

      // If we have Search keywords
      if (!string.IsNullOrEmpty(_searchMessageFilter) || !string.IsNullOrEmpty(_searchSenderFilter))
      {
        result = result.Where(l =>
            (string.IsNullOrEmpty(_searchMessageFilter) || l.Message!.Contains(_searchMessageFilter, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrEmpty(_searchSenderFilter) || l.Sender!.Contains(_searchSenderFilter, StringComparison.CurrentCultureIgnoreCase))
        );
      }
      // Uses IPAdress + Sender to get a list of Distinct Log "devices", diplays there latest post
      if (_showDistinct) // Toggled by Button in UI
      {
        var latestDates = _logDB.LogEntries
                  .GroupBy(l => new { l.IPAdress, l.Sender })
                  .Select(g => new
                  {
                    g.Key.IPAdress,
                    g.Key.Sender,
                    LatestDate = g.Max(l => l.LogDate)
                  }).AsQueryable();
        // The resulting list here contains all data from the LogEntry, could be used to set an alarm 
        // if the last logPost is older than XXX....
        result = _logDB.LogEntries
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
  public async Task ShowDistinctsAsync()
  {
    _showDistinct = !_showDistinct; // Toggle view

    await UpdateDataAsync();
  }
  protected override async Task OnInitializedAsync()
  {
    await StartLogHubAsync();

    try
    {
      if (_hubConnection != null)
      {
        await _hubConnection.StartAsync();
        Console.WriteLine("SignalR connected.");
        await UpdateDataAsync();
      }
    }
    catch (Exception ex) // If the SignalR/API is NOT started, let the page render before retrying connecting SignalR
    {
      _ = Task.Run(async () =>
     {
       await Task.Delay(10000); // 10 seconds delay to let the page render
       await SignalRRetryAsync();
     });
      Console.WriteLine($"Error connecting to SignalR: {ex.Message} Retrying");
    }
  }
  /// <summary>
  /// Starts our SignalR hub(LogHub) and wires up the "event" when message "ReceiveLogUpdate" is received.
  /// </summary>
  /// <returns></returns>
  private async Task StartLogHubAsync()
  {
    if (LogDBFactory is not null)
      _logDB = await LogDBFactory.CreateDbContextAsync();

    _hubConnection = new HubConnectionBuilder()
        .WithUrl(Configuration?.GetConnectionString("SignalRAPI") ?? "")
        .Build();

    _hubConnection.Closed += HubConnection_ClosedAsync;

    _hubConnection.On("ReceiveLogUpdate", async () => await UpdateDataAsync());
  }
  /// <summary>
  /// If Client detects SignalR connection is lost, connect again
  /// </summary>
  /// <param name="arg"></param>
  /// <returns></returns>
  private async Task HubConnection_ClosedAsync(Exception? arg)
  {
    await SignalRRetryAsync();

    Console.WriteLine("Couldn't reconnect SignalR - check API/SignalR Hub - retrying");
  }

  private async Task SignalRRetryAsync()
  {
    Console.WriteLine("HUB Closed");
    await UpdateDataAsync();

    const int maxRetryAttempts = 15;
    const int delayBetweenAttempts = 5000; // 10 sekunder i millisekunder

    for (int i = 0; i < maxRetryAttempts; i++)
    {
      try
      {
        if (_hubConnection != null)
        {
          await _hubConnection.StartAsync();
          await UpdateDataAsync();
        }
        else
        {
          await StartLogHubAsync();
        }
        Console.WriteLine("Reconnected!");
        await UpdateDataAsync();
        return;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Reconnect SignalR Try: {i + 1} Fail: {ex.Message}");

        if (i < maxRetryAttempts - 1)
        {
          await Task.Delay(delayBetweenAttempts);
          await UpdateDataAsync();
        }
      }
    }
  }

  /// <summary>
  /// Deletes ALL logposts in the LogEntries Table in SQLite
  /// </summary>
  /// <returns></returns>
  public async Task DeleteLogTableDataAsync()
  {
    await _logDB!.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
    await UpdateDataAsync();
  }
  /// <summary>
  /// We must tell the Quickgrid to refresh its data and we must do it on the UI thread
  /// Hence the InvokeAsync code
  /// </summary>
  /// <returns></returns>
  public async Task UpdateDataAsync()
  {
    await InvokeAsync(async () =>
    {
      if (_myLogGrid is not null)
      {
        await _myLogGrid.RefreshDataAsync();
        StateHasChanged();
      }
    });
  }
  /// <summary>
  /// Make sure we dispose our resources
  /// </summary>
  /// <returns></returns>
  public async ValueTask DisposeAsync()
  {
    if (_hubConnection is not null)
    {
      _hubConnection.Closed -= HubConnection_ClosedAsync;
      await _hubConnection.DisposeAsync();
    }
    if (_logDB is not null)
    {
      await _logDB.DisposeAsync();
    }
    GC.SuppressFinalize(this);
  }
}