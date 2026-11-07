namespace ProjectMarvinAPI.Tests;

/// <summary>
/// Shares one in-memory API per test class and empties the log table before every test,
/// so each test starts from a known state. Tests within a class run one at a time.
/// </summary>
public abstract class ApiTestBase(MarvinApiFactory factory) : IClassFixture<MarvinApiFactory>, IAsyncLifetime
{
  protected MarvinApiFactory Factory { get; } = factory;

  protected HttpClient Client { get; } = factory.CreateClient();

  public Task InitializeAsync() => Factory.ResetDatabaseAsync();

  public Task DisposeAsync()
  {
    Client.Dispose();
    return Task.CompletedTask;
  }
}
