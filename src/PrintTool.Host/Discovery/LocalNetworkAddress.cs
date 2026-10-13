using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PrintTool.Host.Discovery;

/// <summary>
/// Resolve o IPv4 desta máquina na LAN — o endereço que deve ser anunciado aos Clients.
/// </summary>
public static class LocalNetworkAddress
{
    public static IPAddress GetPrimaryIPv4()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation addressInfo in nic.GetIPProperties().UnicastAddresses)
            {
                if (addressInfo.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addressInfo.Address))
                {
                    return addressInfo.Address;
                }
            }
        }

        return IPAddress.Loopback;
    }
}
