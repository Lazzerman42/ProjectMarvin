using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ProjectMarvinAPI.Services;

public static class LocalNetwork
{
  /// <summary>
  /// Finds this machine's LAN IPv4 address: preferably one on a network adapter that is up and has
  /// a default gateway (so not a virtual Hyper-V/WSL/VPN adapter), otherwise the first IPv4 address
  /// the host name resolves to.
  /// </summary>
  /// <exception cref="SocketException">The machine has no IPv4 address.</exception>
  public static IPAddress GetLocalIPv4Address()
  {
    var fromAdapters = NetworkInterface.GetAllNetworkInterfaces()
        .Where(nic => nic.OperationalStatus == OperationalStatus.Up
            && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(nic => nic.GetIPProperties())
        .Where(props => props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
            && !g.Address.Equals(IPAddress.Any)))
        .SelectMany(props => props.UnicastAddresses)
        .Select(ua => ua.Address)
        .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

    return fromAdapters
        ?? Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
        ?? throw new SocketException((int)SocketError.AddressNotAvailable, "No network adapters with an IPv4 address in the system!");
  }
}
