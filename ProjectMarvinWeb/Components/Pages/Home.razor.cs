using System.Linq.Expressions;
using Marvin.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using ProjectMarvin.Data;

namespace ProjectMarvin.Components.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
  private enum SortColumn { Time, Level, Sender, Ip, Message }

  private const int BatchSize = 20;
  private static readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan _refreshDebounce = TimeSpan.FromMilliseconds(250);
  private static readonly TimeSpan _searchDebounce = TimeSpan.FromMilliseconds(300);

  private static readonly string[] Levels = ["All", "Info", "Warning", "Error"];
  private static readonly string[] _infoTypes = ["info", "information"];
  private static readonly string[] _warningTypes = ["warn", "warning"];
  private static readonly string[] _errorTypes = ["error", "err", "fatal", "critical"];

  private readonly CancellationTokenSource _cts = new();
  private readonly List<LogEntry> _items = [];

  private HubConnection? _hubConnection;
  private DotNetObjectReference<Home>? _selfRef;
  private ElementReference _scrollEl;
  private ElementReference _sentinel;
  private CancellationTokenSource? _searchCts;
  private int _refreshPending;
  private bool _disposed;

  // Filters and sorting
  private string _query = "";
  private string _appliedQuery = ""; // the search the loaded rows were fetched with (used for highlighting)
  private string _level = "All";
  private SortColumn _sortColumn = SortColumn.Time;
  private bool _sortDescending = true;
  private bool _showLatest;
  private bool _showDialog;

  // Loaded data
  private int _total;      // entries matching the filters
  private int _allTotal;   // all entries in the DB
  private bool _loaded;
  private bool _loading;
  private bool _recheckSentinel;
  private bool _scrollToTop;

  // Bumped whenever the list is replaced, so a "load more" that was started before is discarded
  private int _generation;
  private int _loadMoreId;

  [Inject] public IDbContextFactory<ApplicationDbContextLogData>? LogDBFactory { get; set; }
  [Inject] public IConfiguration? Configuration { get; set; }
  [Inject] public IJSRuntime JS { get; set; } = default!;

  public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

  private bool HasFilters => _query.Length > 0 || _level != "All";

  private string CountLabel => $"{_items.Count} of {_total} items";

  protected override Task OnInitializedAsync() => ReloadAsync(keepLoaded: false);

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    try
    {
      if (firstRender)
      {
        _selfRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("marvinLog.observe", _sentinel, _scrollEl, _selfRef);

        // The list loads straight from the DB - SignalR is only used to learn that new rows
        // exist. Starting the hub after the first render keeps it off the render path.
        _ = ConnectHubAsync();
      }
      if (_scrollToTop)
      {
        _scrollToTop = false;
        await JS.InvokeVoidAsync("marvinLog.scrollToTop", _scrollEl, false);
      }
      if (_recheckSentinel)
      {
        _recheckSentinel = false;
        await JS.InvokeVoidAsync("marvinLog.recheck", _sentinel);
      }
    }
    catch (JSDisconnectedException)
    {
    }
    catch (TaskCanceledException)
    {
    }
  }

  // Maps a free-text LogType to a CSS class used for the colored badge / row accent
  private static string TypeClass(string? logType) => (logType ?? "").Trim().ToLowerInvariant() switch
  {
    "info" or "information" => "lvl-info",
    "warn" or "warning" => "lvl-warning",
    "error" or "err" or "fatal" or "critical" => "lvl-error",
    "ok" or "success" => "lvl-success",
    _ => "lvl-debug"
  };

  private static string[]? LevelTypes(string level) => level switch
  {
    "Info" => _infoTypes,
    "Warning" => _warningTypes,
    "Error" => _errorTypes,
    _ => null
  };

  private string Arrow(SortColumn column) =>
      column != _sortColumn ? "↕" : _sortDescending ? "↓" : "↑";

  private string ArrowClass(SortColumn column) => column == _sortColumn ? "active" : "";

  // Highlights the first case-insensitive match of the search inside a cell
  private RenderFragment Highlight(string? text) => builder =>
  {
    if (string.IsNullOrEmpty(text)) return;

    var term = _appliedQuery;
    var index = term.Length == 0 ? -1 : text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
      builder.AddContent(0, text);
      return;
    }

    builder.AddContent(1, text[..index]);
    builder.OpenElement(2, "mark");
    builder.AddAttribute(3, "class", "hl");
    builder.AddContent(4, text.Substring(index, term.Length));
    builder.CloseElement();
    builder.AddContent(5, text[(index + term.Length)..]);
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

  private async Task OnQueryInputAsync(ChangeEventArgs e)
  {
    _query = e.Value?.ToString() ?? "";

    // Wait until the user stops typing before querying
    _searchCts?.Cancel();
    _searchCts?.Dispose();
    var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
    try
    {
      await Task.Delay(_searchDebounce, cts.Token);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    await ReloadAsync(keepLoaded: false);
  }

  private Task SetLevelAsync(string level)
  {
    _level = level;
    return ReloadAsync(keepLoaded: false);
  }

  private Task ClearFiltersAsync()
  {
    _searchCts?.Cancel();
    _query = "";
    _level = "All";
    return ReloadAsync(keepLoaded: false);
  }

  private Task ToggleShowLatestAsync()
  {
    _showLatest = !_showLatest;
    return ReloadAsync(keepLoaded: false);
  }

  private Task SortByAsync(SortColumn column)
  {
    if (column == _sortColumn)
    {
      _sortDescending = !_sortDescending;
    }
    else
    {
      _sortColumn = column;
      _sortDescending = column == SortColumn.Time; // newest first, text columns A-Z
    }
    return ReloadAsync(keepLoaded: false);
  }

  private async Task ScrollToTopAsync()
  {
    try
    {
      await JS.InvokeVoidAsync("marvinLog.scrollToTop", _scrollEl, true);
    }
    catch (JSDisconnectedException)
    {
    }
  }

  public async Task DeleteLogTableDataAsync()
  {
    if (LogDBFactory is null) return;
    await using var db = await LogDBFactory.CreateDbContextAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
    await ReloadAsync(keepLoaded: false);
  }

  private IQueryable<LogEntry> BuildQuery(ApplicationDbContextLogData db, string search)
  {
    IQueryable<LogEntry> query = db.LogEntries;

    if (_showLatest)
    {
      // Latest entry per IP (Id breaks ties between entries with identical timestamps)
      query = query.Where(l => l.Id == db.LogEntries
          .Where(inner => inner.IPAdress == l.IPAdress)
          .OrderByDescending(inner => inner.LogDate)
          .ThenByDescending(inner => inner.Id)
          .Select(inner => inner.Id)
          .First());
    }

    if (search.Length > 0)
    {
      var upper = search.ToUpperInvariant();
      query = query.Where(l =>
          ApplicationDbContextLogData.UnicodeUpper(l.Sender)!.Contains(upper) ||
          ApplicationDbContextLogData.UnicodeUpper(l.Message)!.Contains(upper) ||
          ApplicationDbContextLogData.UnicodeUpper(l.IPAdress)!.Contains(upper));
    }

    var types = LevelTypes(_level);
    if (types is not null)
    {
      query = query.Where(l => types.Contains(l.LogType!.Trim().ToLower()));
    }

    return query;
  }

  private IQueryable<LogEntry> ApplySort(IQueryable<LogEntry> query)
  {
    var ordered = _sortColumn switch
    {
      SortColumn.Level => OrderBy(query, l => l.LogType),
      SortColumn.Sender => OrderBy(query, l => l.Sender),
      SortColumn.Ip => OrderBy(query, l => l.IPAdress),
      SortColumn.Message => OrderBy(query, l => l.Message),
      _ => OrderBy(query, l => l.LogDate)
    };

    // Stable order is required for Skip/Take paging
    return _sortDescending ? ordered.ThenByDescending(l => l.Id) : ordered.ThenBy(l => l.Id);
  }

  private IOrderedQueryable<LogEntry> OrderBy<TKey>(IQueryable<LogEntry> query, Expression<Func<LogEntry, TKey>> key) =>
      _sortDescending ? query.OrderByDescending(key) : query.OrderBy(key);

  /// <summary>
  /// Replaces the list with the first rows for the current filters. With <paramref name="keepLoaded"/>
  /// (live updates) as many rows as are already shown are reloaded, so the scroll position survives.
  /// Independent of the SignalR connection, so the log is visible even when the API/hub is down.
  /// </summary>
  private async Task ReloadAsync(bool keepLoaded)
  {
    if (LogDBFactory is null || _disposed) return;

    var generation = ++_generation;
    var search = _query.Trim();
    var take = keepLoaded ? Math.Max(_items.Count, BatchSize) : BatchSize;
    var ct = _cts.Token;

    try
    {
      await using var db = await LogDBFactory.CreateDbContextAsync(ct);
      var query = BuildQuery(db, search);

      var total = await query.CountAsync(ct);
      var items = await ApplySort(query).Take(take).ToListAsync(ct);
      var allTotal = await db.LogEntries.CountAsync(ct);

      if (generation != _generation) return; // a newer reload has started

      _generation++;
      _items.Clear();
      _items.AddRange(items);
      _total = total;
      _allTotal = allTotal;
      _appliedQuery = search;
      _loaded = true;
      _loading = false;
      _recheckSentinel = true;
      _scrollToTop |= !keepLoaded;
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Loading log entries failed: {ex.Message}");
      return;
    }

    await NotifyStateChangedAsync();
  }

  /// <summary>Called from JS when the end of the list scrolls into view.</summary>
  [JSInvokable]
  public async Task LoadMoreAsync()
  {
    if (_loading || !_loaded || _disposed || _items.Count >= _total || LogDBFactory is null) return;

    var generation = _generation;
    var loadMoreId = ++_loadMoreId;
    var search = _appliedQuery;
    var skip = _items.Count;
    var ct = _cts.Token;
    _loading = true;
    await NotifyStateChangedAsync();

    try
    {
      await using var db = await LogDBFactory.CreateDbContextAsync(ct);
      var items = await ApplySort(BuildQuery(db, search)).Skip(skip).Take(BatchSize).ToListAsync(ct);

      if (generation != _generation) return; // the list was replaced meanwhile

      // New rows arriving at the top shift the offsets - skip rows we already show
      var shown = _items.Select(i => i.Id).ToHashSet();
      _items.AddRange(items.Where(i => shown.Add(i.Id)));
      if (items.Count < BatchSize)
      {
        _total = _items.Count; // reached the end (rows may have been deleted meanwhile)
      }
      _recheckSentinel = true;
    }
    catch (OperationCanceledException)
    {
      return;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Loading more log entries failed: {ex.Message}");
    }
    finally
    {
      // A reload may have cleared the flag and let a newer "load more" start
      if (loadMoreId == _loadMoreId)
      {
        _loading = false;
      }
    }

    await NotifyStateChangedAsync();
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
      // The circuit was torn down - nothing to do
    }
  }

  private Task RefreshAsync() => _disposed ? Task.CompletedTask : InvokeAsync(() => ReloadAsync(keepLoaded: true));

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
    await RefreshAsync();
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
      await RefreshAsync(); // catch up with entries that arrived while we were offline
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
      await RefreshAsync();
    }
  }

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    await _cts.CancelAsync();
    _searchCts?.Dispose();
    try
    {
      await JS.InvokeVoidAsync("marvinLog.dispose", _sentinel);
    }
    catch (JSDisconnectedException)
    {
    }
    catch (InvalidOperationException)
    {
      // Never rendered, so there is no element to clean up
    }
    _selfRef?.Dispose();
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
