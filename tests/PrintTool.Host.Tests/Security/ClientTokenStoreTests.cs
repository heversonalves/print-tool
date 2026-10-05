using PrintTool.Host.Security;
using Xunit;

namespace PrintTool.Host.Tests.Security;

public class ClientTokenStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_tokenstore_" + Guid.NewGuid());
    private readonly string _path;

    public ClientTokenStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "tokens.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void IssueToken_ThenValidate_WithSameToken_ReturnsTrue()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);
        var clientId = Guid.NewGuid();

        string token = store.IssueToken(clientId, "Notebook");

        Assert.True(store.Validate(clientId, token));
    }

    [Fact]
    public void Validate_WithWrongToken_ReturnsFalse()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);
        var clientId = Guid.NewGuid();
        store.IssueToken(clientId, "Notebook");

        Assert.False(store.Validate(clientId, "token-errado"));
    }

    [Fact]
    public void Validate_UnknownClientId_ReturnsFalse()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);

        Assert.False(store.Validate(Guid.NewGuid(), "qualquer-coisa"));
    }

    [Fact]
    public void Revoke_ThenValidate_ReturnsFalse()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);
        var clientId = Guid.NewGuid();
        string token = store.IssueToken(clientId, "Notebook");

        bool revoked = store.Revoke(clientId);

        Assert.True(revoked);
        Assert.False(store.Validate(clientId, token));
    }

    [Fact]
    public void Revoke_UnknownClientId_ReturnsFalse()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);

        Assert.False(store.Revoke(Guid.NewGuid()));
    }

    [Fact]
    public void IssueToken_Twice_ForSameClient_InvalidatesThePreviousToken()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);
        var clientId = Guid.NewGuid();
        string firstToken = store.IssueToken(clientId, "Notebook");
        string secondToken = store.IssueToken(clientId, "Notebook");

        Assert.False(store.Validate(clientId, firstToken));
        Assert.True(store.Validate(clientId, secondToken));
    }

    [Fact]
    public void LoadOrCreate_AfterPersisting_ReloadsTokensFromDisk()
    {
        var clientId = Guid.NewGuid();
        string token = ClientTokenStore.LoadOrCreate(_path).IssueToken(clientId, "Notebook");

        ClientTokenStore reloaded = ClientTokenStore.LoadOrCreate(_path);

        Assert.True(reloaded.Validate(clientId, token));
    }

    [Fact]
    public void TokenHash_NeverStoredInClearInTheUnderlyingFile()
    {
        ClientTokenStore store = ClientTokenStore.LoadOrCreate(_path);
        string token = store.IssueToken(Guid.NewGuid(), "Notebook");

        string fileContents = File.ReadAllText(_path);

        Assert.DoesNotContain(token, fileContents);
    }
}
