namespace PrintTool.Host.Alerting;

/// <summary>Envia um alerta de uma linha para o endereço configurado em <see cref="AlertingOptions"/>.</summary>
public interface IAlertSender
{
    Task SendAsync(string subject, string body, CancellationToken cancellationToken);
}
