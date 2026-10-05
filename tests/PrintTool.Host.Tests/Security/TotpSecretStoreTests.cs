using PrintTool.Host.Security;
using Xunit;

namespace PrintTool.Host.Tests.Security;

public class TotpSecretStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_totpsecret_" + Guid.NewGuid());
    private readonly string _path;

    public TotpSecretStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "totp-secret.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadOrCreate_CalledTwice_ReturnsSameSecret()
    {
        TotpSecretStore first = TotpSecretStore.LoadOrCreate(_path);
        TotpSecretStore second = TotpSecretStore.LoadOrCreate(_path);

        Assert.Equal(first.SecretBase32, second.SecretBase32);
        Assert.Equal(first.Secret, second.Secret);
    }

    [Fact]
    public void LoadOrCreate_FirstCall_GeneratesNonEmptySecret()
    {
        TotpSecretStore store = TotpSecretStore.LoadOrCreate(_path);

        Assert.Equal(20, store.Secret.Length);
        Assert.NotEmpty(store.SecretBase32);
    }
}
