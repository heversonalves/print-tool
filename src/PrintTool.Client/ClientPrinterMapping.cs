namespace PrintTool.Client;

/// <summary>
/// Associa uma porta local (onde o driver Windows, cadastrado como "Porta TCP/IP Padrão",
/// escreve os jobs) ao nome da impressora compartilhada do outro lado, no Host.
/// </summary>
public sealed record ClientPrinterMapping(int LocalPort, string RemotePrinterName);
