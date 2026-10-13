using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Common.Discovery;

/// <summary>
/// Lado Client da descoberta: sonda a rede local e coleta as respostas dos Hosts
/// disponíveis dentro de uma janela de tempo. Abstrai o transporte (UDP broadcast
/// na Fase 1) para permitir troca futura por mDNS sem afetar quem consome.
/// </summary>
public interface IDiscoveryClient
{
    Task<IReadOnlyList<DiscoveryAnnouncement>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
