using System.Text.Json;

namespace PrintTool.Client.Security;

/// <summary>
/// Identidade estável desta máquina Client: um <see cref="ClientId"/> (GUID) persistido
/// localmente, gerado uma única vez. É o identificador que o Host usa para emitir e validar
/// o token de longa duração — trocar esse arquivo (ou reinstalar o Client do zero) exige
/// parear de novo.
/// </summary>
public sealed class ClientIdentity
{
    public Guid ClientId { get; }

    /// <summary>Nome de exibição mostrado ao administrador durante o pareamento — sempre o nome atual da máquina, não persistido.</summary>
    public string DisplayName => Environment.MachineName;

    private ClientIdentity(Guid clientId)
    {
        ClientId = clientId;
    }

    public static ClientIdentity LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            ClientIdentityData? data = JsonSerializer.Deserialize<ClientIdentityData>(json);
            if (data is not null)
            {
                return new ClientIdentity(data.ClientId);
            }
        }

        var clientId = Guid.NewGuid();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(new ClientIdentityData(clientId)));
        return new ClientIdentity(clientId);
    }

    private sealed record ClientIdentityData(Guid ClientId);
}
