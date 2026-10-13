using System.Net.Security;
using System.Net.Sockets;
using PrintTool.Client.Discovery;
using PrintTool.Common.Protocol;
using PrintTool.Common.Protocol.Messages;

namespace PrintTool.Client.Security;

public sealed record PairingOutcome(bool Success, string? ErrorMessage = null);

/// <summary>
/// Pareamento sob demanda com o Host que serve uma impressora: resolve o Host via descoberta,
/// conecta por TLS confiando (TOFU) no thumbprint que a própria descoberta anunciou — ainda
/// não há nada fixado para este Host — envia o código TOTP digitado pelo administrador, e
/// grava o token emitido (e o thumbprint, agora fixado) em <see cref="HostTokenStore"/>.
/// Usado pelo subcomando de CLI <c>pair</c>, não pelo serviço em segundo plano.
/// </summary>
public sealed class PairingClient
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);

    private readonly DiscoveredHostTable _hostTable;
    private readonly ClientIdentity _clientIdentity;
    private readonly HostTokenStore _tokenStore;

    public PairingClient(DiscoveredHostTable hostTable, ClientIdentity clientIdentity, HostTokenStore tokenStore)
    {
        _hostTable = hostTable;
        _clientIdentity = clientIdentity;
        _tokenStore = tokenStore;
    }

    public async Task<PairingOutcome> PairAsync(string printerName, string code, CancellationToken cancellationToken)
    {
        await _hostTable.RefreshAsync(DiscoveryTimeout, cancellationToken).ConfigureAwait(false);
        if (!_hostTable.TryResolve(printerName, out ResolvedHost? host) || host is null)
        {
            return new PairingOutcome(false, $"Nenhum Host anunciando a impressora '{printerName}' foi encontrado na rede.");
        }

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(host.Address, host.TcpPort, cancellationToken).ConfigureAwait(false);

        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) => PinnedCertificateValidator.Matches(certificate, host.CertThumbprint));
        await sslStream.AuthenticateAsClientAsync(host.HostName, null, false).ConfigureAwait(false);

        var request = new PairingRequest(_clientIdentity.ClientId, _clientIdentity.DisplayName, code);
        await FrameWriter.WriteMessageAsync(sslStream, MessageType.PairingRequest, request, cancellationToken).ConfigureAwait(false);

        MessageEnvelope envelope = await FrameReader.ReadFrameAsync(sslStream, cancellationToken).ConfigureAwait(false);
        PairingResult result = FrameReader.ReadMessage<PairingResult>(envelope);

        if (!result.Approved || result.Token is null || result.HostId is null)
        {
            return new PairingOutcome(false, result.Reason ?? "Pareamento não aprovado pelo Host.");
        }

        _tokenStore.Save(new HostTokenEntry(result.HostId.Value, host.HostName, host.CertThumbprint, result.Token, DateTimeOffset.UtcNow));
        return new PairingOutcome(true);
    }
}
