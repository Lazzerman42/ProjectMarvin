using Marvin.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using ProjectMarvin.Data;

namespace ProjectMarvin.Components.Pages;

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
    catch (Exception ex)
    {
      // Om SignalR inte är startat, försök igen efter 10 sekunder
      _ = Task.Run(async () =>
      {
        await Task.Delay(10000);
        await SignalRRetryAsync();
      });
      Console.WriteLine($"Error connecting to SignalR: {ex.Message} Retrying");
    }
  }
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

  private readonly PaginationState _pagination = new() { ItemsPerPage = 42 };
  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  private HubConnection? _hubConnection;

  [Inject] public IDbContextFactory<ApplicationDbContextLogData>? LogDBFactory { get; set; }
  [Inject] public IConfiguration? Configuration { get; set; }
  [Inject] public NavigationManager? NavMan { get; set; }

  private string? _searchMessageFilter = "";
  private string? _searchSenderFilter = "";
  private QuickGrid<LogEntry>? _myLogGrid;

  private bool _showDialog = false;
  private bool _showDistinct = false;

  private void ShowConfirmDialog() => _showDialog = true;
  private void CloseDialog() => _showDialog = false;

  private async Task HandleConfirmationAsync(bool confirmed)
  {
    _showDialog = false;
    if (confirmed)
    {
      await DeleteLogTableDataAsync();
    }
  }
  public string SearchSenderFilterTitle =>
      string.IsNullOrEmpty(_searchSenderFilter) ? "Sender" : "Sender : " + _searchSenderFilter;
  public string SearchMesssageFilterTitle =>
      string.IsNullOrEmpty(_searchMessageFilter) ? "Message" : "Message : " + _searchMessageFilter;

  /// <summary>
  /// ItemsProvider – laddar bara den sida som behövs från DB
  /// </summary>
  private async ValueTask<GridItemsProviderResult<LogEntry>> LoadLogEntriesAsync(GridItemsProviderRequest<LogEntry> request)
  {

    if (LogDBFactory is null || !IsConnected)
      return GridItemsProviderResult.From(Array.Empty<LogEntry>(), 0);

    await using var db = await LogDBFactory.CreateDbContextAsync();

    IQueryable<LogEntry> query;

    if (_showDistinct)
    {
      // Hämta senaste per IP
      query = db.LogEntries
          .Where(l => l.LogDate == db.LogEntries
              .Where(inner => inner.IPAdress == l.IPAdress)
              .Max(inner => inner.LogDate));
    }
    else
    {
      query = db.LogEntries;
    }
    // Applicera filter
    if (!string.IsNullOrEmpty(_searchMessageFilter))
    {
      var msgFilter = _searchMessageFilter.ToUpper();
      query = query.Where(l => l.Message!.ToUpper().Contains(msgFilter));
    }
    if (!string.IsNullOrEmpty(_searchSenderFilter))
    {
      var senderFilter = _searchSenderFilter.ToUpper();
      query = query.Where(l => l.Sender!.ToUpper().Contains(senderFilter));
    }
    // Räkna FÖRE sortering och paginering
    var totalCount = await query.CountAsync();

    // --- SORTERING ---
    var sortByProperties = request.GetSortByProperties();
    if (sortByProperties.Any())
    {
      var firstSort = sortByProperties.First();
      var propertyName = firstSort.PropertyName;
      var descending = firstSort.Direction == SortDirection.Descending;

      // Hantera sortering för varje kolumn
      query = propertyName switch
      {
        nameof(LogEntry.LogDate) => descending ? query.OrderByDescending(x => x.LogDate) : query.OrderBy(x => x.LogDate),
        nameof(LogEntry.IPAdress) => descending ? query.OrderByDescending(x => x.IPAdress) : query.OrderBy(x => x.IPAdress),
        nameof(LogEntry.LogType) => descending ? query.OrderByDescending(x => x.LogType) : query.OrderBy(x => x.LogType),
        nameof(LogEntry.Sender) => descending ? query.OrderByDescending(x => x.Sender) : query.OrderBy(x => x.Sender),
        nameof(LogEntry.Message) => descending ? query.OrderByDescending(x => x.Message) : query.OrderBy(x => x.Message),
        _ => query.OrderByDescending(l => l.LogDate) // Default
      };
    }
    else
    {
      // Default sortering om ingen sortering är vald
      query = query.OrderByDescending(l => l.LogDate);
    }

    // Paginering
    var takeCount = request.Count ?? _pagination.ItemsPerPage;
    var items = await query
        .Skip(request.StartIndex)
        .Take(takeCount)
        .ToListAsync();

    Console.WriteLine($"Start {request.StartIndex}  Take {takeCount}\r\n");

    return GridItemsProviderResult.From(items, totalCount);
  }
  public async Task ShowDistinctsAsync()
  {
    _showDistinct = !_showDistinct;
    await UpdateDataAsync();
  }
  private Task StartLogHubAsync()
  {
    _hubConnection = new HubConnectionBuilder()
        .WithUrl(Configuration?.GetConnectionString("SignalRAPI") ?? "")
        .Build();

    _hubConnection.Closed += HubConnection_ClosedAsync;
    _hubConnection.On("ReceiveLogUpdate", async () => await UpdateDataAsync());
    return Task.CompletedTask;
  }
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
    const int delayBetweenAttempts = 5000;

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

  public async Task DeleteLogTableDataAsync()
  {
    if (LogDBFactory is null) return;
    await using var db = await LogDBFactory.CreateDbContextAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
    await UpdateDataAsync();
  }

  public async ValueTask DisposeAsync()
  {
    if (_hubConnection is not null)
    {
      _hubConnection.Closed -= HubConnection_ClosedAsync;
      await _hubConnection.DisposeAsync();
    }
    GC.SuppressFinalize(this);
  }
}
