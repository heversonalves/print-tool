using PrintTool.Host.Alerting;
using PrintTool.Host.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PrintTool.Host.Tests.Alerting;

public class ConnectivityMonitorServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_connmonitor_" + Guid.NewGuid());
    private readonly ClientTokenStore _clientTokenStore;
    private readonly ConnectivityRegistry _connectivityRegistry = new();

    public ConnectivityMonitorServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        _clientTokenStore = ClientTokenStore.LoadOrCreate(Path.Combine(_tempDir, "tokens.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private sealed class FakeAlertSender : IAlertSender
    {
        private readonly List<(string Subject, string Body)> _sent = new();

        public IReadOnlyList<(string Subject, string Body)> Sent
        {
            get { lock (_sent) { return _sent.ToList(); } }
        }

        public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            lock (_sent)
            {
                _sent.Add((subject, body));
            }
            return Task.CompletedTask;
        }
    }

    // Terça-feira às 10h: dentro do horário comercial padrão (seg-sex 08:00-18:00).
    private static readonly DateTimeOffset WithinBusinessHours = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    // Domingo às 10h: fora do horário comercial padrão (sáb/dom fechados).
    private static readonly DateTimeOffset OutsideBusinessHours = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    private ConnectivityMonitorService CreateService(FakeAlertSender sender, Func<DateTimeOffset> clock, TimeSpan? interval = null) =>
        new(
            _clientTokenStore,
            _connectivityRegistry,
            new AlertingOptions(),
            sender,
            NullLogger<ConnectivityMonitorService>.Instance,
            checkInterval: interval ?? TimeSpan.FromMilliseconds(30),
            clock: clock);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task FirstCheck_ConnectedClient_DoesNotAlert()
    {
        var clientId = Guid.NewGuid();
        _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        _connectivityRegistry.MarkConnected(clientId);

        var sender = new FakeAlertSender();
        ConnectivityMonitorService service = CreateService(sender, () => WithinBusinessHours);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(150); // dá tempo pra várias checagens passarem
            Assert.Empty(sender.Sent);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientDisconnects_AfterBaseline_AlertsOnce()
    {
        var clientId = Guid.NewGuid();
        _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        _connectivityRegistry.MarkConnected(clientId);

        var sender = new FakeAlertSender();
        ConnectivityMonitorService service = CreateService(sender, () => WithinBusinessHours);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(60); // garante que a checagem de linha de base já rodou

            _connectivityRegistry.MarkDisconnected(clientId);

            await WaitUntilAsync(() => sender.Sent.Count >= 1);
            await Task.Delay(100); // garante que não manda alerta repetido nas checagens seguintes

            var alert = Assert.Single(sender.Sent);
            Assert.Contains("desconectad", alert.Subject, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Notebook de Teste", alert.Subject);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ClientReconnects_AfterBeingFlaggedDisconnected_AlertsRestored()
    {
        var clientId = Guid.NewGuid();
        _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        // Começa desconectado: a primeira checagem (linha de base) não alerta mesmo assim.

        var sender = new FakeAlertSender();
        ConnectivityMonitorService service = CreateService(sender, () => WithinBusinessHours);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(60); // linha de base (desconectado), sem alerta

            _connectivityRegistry.MarkConnected(clientId);

            await WaitUntilAsync(() => sender.Sent.Count >= 1);

            var alert = Assert.Single(sender.Sent);
            Assert.Contains("restaurad", alert.Subject, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task OutsideBusinessHours_NeverAlerts_EvenWithConnectivityChanges()
    {
        var clientId = Guid.NewGuid();
        _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        _connectivityRegistry.MarkConnected(clientId);

        var sender = new FakeAlertSender();
        ConnectivityMonitorService service = CreateService(sender, () => OutsideBusinessHours);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(60);
            _connectivityRegistry.MarkDisconnected(clientId);
            await Task.Delay(100);
            _connectivityRegistry.MarkConnected(clientId);
            await Task.Delay(100);

            Assert.Empty(sender.Sent);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RevokedClient_IsNeverAlerted()
    {
        var clientId = Guid.NewGuid();
        _clientTokenStore.IssueToken(clientId, "Notebook de Teste");
        _connectivityRegistry.MarkConnected(clientId);
        _clientTokenStore.Revoke(clientId);

        var sender = new FakeAlertSender();
        ConnectivityMonitorService service = CreateService(sender, () => WithinBusinessHours);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(60);
            _connectivityRegistry.MarkDisconnected(clientId);
            await Task.Delay(100);

            Assert.Empty(sender.Sent);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
