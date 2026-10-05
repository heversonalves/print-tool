using System.Net;

namespace PrintTool.Client.Discovery;

public sealed record ResolvedHost(Guid HostId, string HostName, IPAddress Address, int TcpPort, string CertThumbprint);
