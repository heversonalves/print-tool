namespace PrintTool.Common.Discovery;

/// <summary>
/// Lado Host da descoberta: escuta sondagens na rede e responde com o estado atual
/// do Host (endereço, porta, impressoras compartilhadas). Abstrai o transporte
/// (UDP broadcast na Fase 1) para permitir troca futura por mDNS sem afetar quem consome.
/// </summary>
public interface IDiscoveryAnnouncer
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
