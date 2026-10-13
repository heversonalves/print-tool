using System.Collections.Concurrent;

namespace PrintTool.Host.Alerting;

/// <summary>
/// Quantas conexões autenticadas cada Client tem abertas agora, em memória. Usa uma contagem
/// (não só um bool) porque uma reconexão pode chegar antes do <c>finally</c> da conexão antiga
/// processar a desconexão — contando, o cliente só aparece "desconectado" quando a última
/// conexão dele realmente cair.
/// </summary>
public sealed class ConnectivityRegistry
{
    private readonly ConcurrentDictionary<Guid, int> _activeConnections = new();

    public void MarkConnected(Guid clientId) =>
        _activeConnections.AddOrUpdate(clientId, 1, (_, count) => count + 1);

    public void MarkDisconnected(Guid clientId) =>
        _activeConnections.AddOrUpdate(clientId, 0, (_, count) => Math.Max(0, count - 1));

    public bool IsConnected(Guid clientId) =>
        _activeConnections.TryGetValue(clientId, out int count) && count > 0;
}
