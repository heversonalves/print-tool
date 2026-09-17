using System.Net;

namespace PrintTool.Client.Discovery;

public sealed record ResolvedHost(string HostName, IPAddress Address, int TcpPort);
