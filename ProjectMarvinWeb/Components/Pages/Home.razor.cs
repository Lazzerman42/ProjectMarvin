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
  protected override async Task OnInitializedAsync()
  {
    await StartLogHubAsync();

    try
    {
      if (_hubConnection != null)
      {
        await _hubConnection.StartAsync();
        Console.WriteLine("SignalR connected.");
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

  public async Task UpdateDataAsync()
  {
    await InvokeAsync(async () =>
    {
      // Invalidera cache n�r ny data kommer
      InvalidateFilterCache();

      if (_myLogGrid is not null)
      {
        await _myLogGrid.RefreshDataAsync();
        StateHasChanged();
      }
    });
  }

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

  // Cache f�r FilteredLog prestanda
  private IQueryable<LogEntry>? _cachedFilteredLog;
  private string? _lastFilterKey;

  private void ShowConfirmDialog()
  {
    _showDialog = true;
  }
  private void CloseDialog()
  {
    _showDialog = false;
  }

  /// <summary>
  /// Displays "Are you sure" dialog - if YES - Deletes all logdata in SQLite
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
  /// Invaliderar filter cache n�r filter �ndras
  /// </summary>
  private void InvalidateFilterCache()
  {
    _cachedFilteredLog = null;
    _lastFilterKey = null;
  }

  /// <summary>
  /// OPTIMERAD version - Datasource for the Quickgrid. Uses SQLite database and entityframework
  /// </summary>
  IQueryable<LogEntry> FilteredLog
  {
    get
    {
      if (_logDB is null)
        return new List<LogEntry>().AsQueryable();

      // Skapa cache key baserat p� alla filter
      var currentFilterKey = $"{_searchMessageFilter}|{_searchSenderFilter}|{_showDistinct}";

      // Returnera cached version om inget �ndrats
      if (_cachedFilteredLog != null && _lastFilterKey == currentFilterKey)
      {
        return _cachedFilteredLog;
      }

      IQueryable<LogEntry> result;

      if (_showDistinct)
      {
        // OPTIMERAD distinct query som stannar i SQLite
        result = _logDB.LogEntries
          .Where(l => l.LogDate == _logDB.LogEntries
            .Where(inner => inner.IPAdress == l.IPAdress)
            .Max(inner => inner.LogDate))
          .AsQueryable();
      }
      else
      {
        result = _logDB.LogEntries.AsQueryable();
      }

      // L�gg till text-filter (h�lls i SQLite)
      if (!string.IsNullOrEmpty(_searchMessageFilter))
      {
        result = result.Where(l => l.Message!.ToUpper().Contains(_searchMessageFilter.ToUpper()));
      }

      if (!string.IsNullOrEmpty(_searchSenderFilter))
      {
        result = result.Where(l => l.Sender!.ToUpper().Contains(_searchSenderFilter.ToUpper()));
      }

      // Cacha resultatet
      _cachedFilteredLog = result;
      _lastFilterKey = currentFilterKey;

      return result;
    }
  }

  // The ShowDistincts per IP/Sender toggle
  public async Task ShowDistinctsAsync()
  {
    _showDistinct = !_showDistinct; // Toggle view
    InvalidateFilterCache(); // Rensa cache
    await UpdateDataAsync();
  }

  /// <summary>
  /// Uppdaterad f�r att hantera filter-�ndringar korrekt
  /// </summary>
  public async Task OnSearchMessageChangedAsync(string newValue)
  {
    _searchMessageFilter = newValue;
    InvalidateFilterCache();
    await UpdateDataAsync();
  }

  /// <summary>
  /// Uppdaterad f�r att hantera filter-�ndringar korrekt
  /// </summary>
  public async Task OnSearchSenderChangedAsync(string newValue)
  {
    _searchSenderFilter = newValue;
    InvalidateFilterCache();
    await UpdateDataAsync();
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
    const int delayBetweenAttempts = 5000; // 5 sekunder i millisekunder

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
    InvalidateFilterCache(); // Rensa cache efter borttagning
    await UpdateDataAsync();
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