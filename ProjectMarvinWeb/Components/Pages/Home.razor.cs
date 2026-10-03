using Marvin.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using ProjectMarvin.Data;

namespace ProjectMarvin.Components.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
  private static readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan _refreshDebounce = TimeSpan.FromMilliseconds(250);

  private readonly CancellationTokenSource _cts = new();
  private readonly PaginationState _pagination = new() { ItemsPerPage = 42 };
  private readonly GridSort<LogEntry> _sortByType = GridSort<LogEntry>.ByAscending(m => m.LogType);
  private readonly GridSort<LogEntry> _sortByIp = GridSort<LogEntry>.ByAscending(m => m.IPAdress);

  private HubConnection? _hubConnection;
  private QuickGrid<LogEntry>? _myLogGrid;
  private int _refreshPending;
  private bool _disposed;

  private string? _searchMessageFilter = "";
  private string? _searchSenderFilter = "";
  private bool _showDialog = false;
  private bool _showDistinct = false;

  [Inject] public IDbContextFactory<ApplicationDbContextLogData>? LogDBFactory { get; set; }
  [Inject] public IConfiguration? Configuration { get; set; }
  [Inject] public NavigationManager? NavMan { get; set; }

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  public string SearchSenderFilterTitle =>
      string.IsNullOrEmpty(_searchSenderFilter) ? "Sender" : "Sender : " + _searchSenderFilter;
  public string SearchMesssageFilterTitle =>
      string.IsNullOrEmpty(_searchMessageFilter) ? "Message" : "Message : " + _searchMessageFilter;

  // The grid loads its data on its own (from the DB) - SignalR is only used to learn that
  // new rows exist. Starting the hub after the first render keeps it off the render path.
  protected override void OnAfterRender(bool firstRender)
  {
    if (firstRender)
    {
      _ = ConnectHubAsync();
    }
  }

  // Maps a free-text LogType to a CSS class used for the colored pill / row accent
  private static string TypeClass(string? logType) => (logType ?? "").Trim().ToLowerInvariant() switch
  {
    "info" or "information" => "log-info",
    "warn" or "warning" => "log-warning",
    "error" or "err" or "fatal" or "critical" => "log-error",
    "ok" or "success" => "log-success",
    _ => "log-debug"
  };

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

  private async Task ClearFiltersAsync()
  {
    _searchMessageFilter = string.Empty;
    _searchSenderFilter = string.Empty;
    await RefreshGridAsync();
  }

  public async Task ShowDistinctsAsync()
  {
    _showDistinct = !_showDistinct;
    await RefreshGridAsync();
  }

  public async Task DeleteLogTableDataAsync()
  {
    if (LogDBFactory is null) return;
    await using var db = await LogDBFactory.CreateDbContextAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
    await RefreshGridAsync();
  }

  /// <summary>
  /// ItemsProvider - loads only the requested page from the DB. Independent of the SignalR
  /// connection, so the log is visible even when the API/hub is down.
  /// </summary>
  private async ValueTask<GridItemsProviderResult<LogEntry>> LoadLogEntriesAsync(GridItemsProviderRequest<LogEntry> request)
  {
    if (LogDBFactory is null)
      return GridItemsProviderResult.From(Array.Empty<LogEntry>(), 0);

    var ct = request.CancellationToken;
    await using var db = await LogDBFactory.CreateDbContextAsync(ct);

    IQueryable<LogEntry> query = db.LogEntries;

    if (_showDistinct)
    {
      // Latest entry per IP (Id breaks ties between entries with identical timestamps)
      query = query.Where(l => l.Id == db.LogEntries
          .Where(inner => inner.IPAdress == l.IPAdress)
          .OrderByDescending(inner => inner.LogDate)
          .ThenByDescending(inner => inner.Id)
          .Select(inner => inner.Id)
          .First());
    }

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

    // Count BEFORE sorting and paging
    var totalCount = await query.CountAsync(ct);

    var sortBy = request.GetSortByProperties().FirstOrDefault();
    var descending = sortBy.Direction == SortDirection.Descending;

    query = sortBy.PropertyName switch
    {
      nameof(LogEntry.LogDate) => descending ? query.OrderByDescending(x => x.LogDate) : query.OrderBy(x => x.LogDate),
      nameof(LogEntry.IPAdress) => descending ? query.OrderByDescending(x => x.IPAdress) : query.OrderBy(x => x.IPAdress),
      nameof(LogEntry.LogType) => descending ? query.OrderByDescending(x => x.LogType) : query.OrderBy(x => x.LogType),
      nameof(LogEntry.Sender) => descending ? query.OrderByDescending(x => x.Sender) : query.OrderBy(x => x.Sender),
      nameof(LogEntry.Message) => descending ? query.OrderByDescending(x => x.Message) : query.OrderBy(x => x.Message),
      _ => query.OrderByDescending(l => l.LogDate) // default
    };

    var takeCount = request.Count ?? _pagination.ItemsPerPage;
    var items = await query
        .Skip(request.StartIndex)
        .Take(takeCount)
        .ToListAsync(ct);

    Console.WriteLine($"Start {request.StartIndex}  Take {takeCount}");

    return GridItemsProviderResult.From(items, totalCount);
  }

  /// <summary>Reloads the grid. QuickGrid re-renders itself, so no StateHasChanged is needed.</summary>
  private async Task RefreshGridAsync()
  {
    if (_disposed) return;
    try
    {
      await InvokeAsync(async () =>
      {
        if (_myLogGrid is not null)
        {
          await _myLogGrid.RefreshDataAsync();
        }
      });
    }
    catch (ObjectDisposedException)
    {
      // The circuit was torn down while we were refreshing - nothing to do
    }
  }

  private async Task NotifyStateChangedAsync()
  {
    if (_disposed) return;
    try
    {
      await InvokeAsync(StateHasChanged);
    }
    catch (ObjectDisposedException)
    {
    }
  }

  // A burst of incoming log entries triggers many hub messages - coalesce them into one reload
  private async Task ScheduleRefreshAsync()
  {
    if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;

    try
    {
      await Task.Delay(_refreshDebounce, _cts.Token);
    }
    catch (OperationCanceledException)
    {
      return;
    }

    Interlocked.Exchange(ref _refreshPending, 0);
    await RefreshGridAsync();
  }

  private async Task ConnectHubAsync()
  {
    var url = Configuration?.GetConnectionString("SignalRAPI");
    if (string.IsNullOrEmpty(url)) return;

    var hub = new HubConnectionBuilder()
        .WithUrl(url)
        .WithAutomaticReconnect(new FixedRetryPolicy(_reconnectDelay))
        .Build();

    hub.On("ReceiveLogUpdate", ScheduleRefreshAsync);
    hub.Reconnecting += _ => NotifyStateChangedAsync();
    hub.Closed += _ => NotifyStateChangedAsync();
    hub.Reconnected += async _ =>
    {
      await NotifyStateChangedAsync();
      await RefreshGridAsync(); // catch up with entries that arrived while we were offline
    };
    _hubConnection = hub;

    // Automatic reconnect only applies once connected, so retry the initial connect ourselves
    var attempts = 0;
    while (!_cts.IsCancellationRequested)
    {
      try
      {
        await hub.StartAsync(_cts.Token);
        break;
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        attempts++;
        Console.WriteLine($"SignalR connect attempt {attempts} failed: {ex.Message}");
        try
        {
          await Task.Delay(_reconnectDelay, _cts.Token);
        }
        catch (OperationCanceledException)
        {
          return;
        }
      }
    }

    await NotifyStateChangedAsync();
    if (attempts > 0)
    {
      await RefreshGridAsync();
    }
  }

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    await _cts.CancelAsync();
    if (_hubConnection is not null)
    {
      await _hubConnection.DisposeAsync();
    }
    _cts.Dispose();
    GC.SuppressFinalize(this);
  }

  private sealed class FixedRetryPolicy(TimeSpan delay) : IRetryPolicy
  {
    public TimeSpan? NextRetryDelay(RetryContext retryContext) => delay;
  }
}
