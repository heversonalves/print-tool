using PrintTool.Host.Security;
using Xunit;

namespace PrintTool.Host.Tests.Security;

public class HostIdentityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_hostidentity_" + Guid.NewGuid());

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadOrCreate_CalledTwice_ReturnsSameHostIdAndCertificate()
    {
        Directory.CreateDirectory(_tempDir);

        HostIdentity first = HostIdentity.LoadOrCreate(_tempDir);
        HostIdentity second = HostIdentity.LoadOrCreate(_tempDir);

        Assert.Equal(first.HostId, second.HostId);
        Assert.Equal(first.CertificateThumbprint, second.CertificateThumbprint);
    }

    [Fact]
    public void LoadOrCreate_DifferentDirectories_ProducesDifferentIdentities()
    {
        string otherDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_hostidentity_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(otherDir);

        try
        {
            HostIdentity first = HostIdentity.LoadOrCreate(_tempDir);
            HostIdentity second = HostIdentity.LoadOrCreate(otherDir);

            Assert.NotEqual(first.HostId, second.HostId);
            Assert.NotEqual(first.CertificateThumbprint, second.CertificateThumbprint);
        }
        finally
        {
            Directory.Delete(otherDir, recursive: true);
        }
    }

    [Fact]
    public void Certificate_HasPrivateKey_UsableForTlsServerAuthentication()
    {
        Directory.CreateDirectory(_tempDir);

        HostIdentity identity = HostIdentity.LoadOrCreate(_tempDir);

        Assert.True(identity.Certificate.HasPrivateKey);
    }
}
