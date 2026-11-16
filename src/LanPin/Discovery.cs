using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LanPin;

record LanInterface(string Name, IPAddress Address, IPAddress Mask)
{
    public override string ToString() => $"{Name} · {Address}";
    public bool Contains(IPAddress other)
    {
        if (other.AddressFamily != AddressFamily.InterNetwork) return false;
        var a = Address.GetAddressBytes(); var b = other.GetAddressBytes(); var m = Mask.GetAddressBytes();
        return Enumerable.Range(0, 4).All(i => (a[i] & m[i]) == (b[i] & m[i]));
    }
    public static List<LanInterface> All() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && Subnet.IsPrivate(Subnet.Number(a.Address)))
            .Select(a => new LanInterface(n.Name, a.Address, a.IPv4Mask))).ToList();
}
record Game(string Key, string Motd, string Host, int Port);
static class Discovery
{
    public static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    public static Game? Parse(byte[] packet, IPAddress source)
    {
        if (packet.Length > 2048) return null;
        var text = Encoding.UTF8.GetString(packet);
        var match = Regex.Match(text, @"^\[MOTD\](.*?)\[/MOTD\]\[AD\]([0-9]{1,5})\[/AD\]$", RegexOptions.Singleline);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int port) || port is < 1 or > 65535) return null;
        string motd = match.Groups[1].Value.Replace("[", "(").Replace("]", ")");
        if (motd.Length > 256) motd = motd[..256];
        return new($"{source}:{port}", motd, source.ToString(), port);
    }
    public static async Task Listen(LanInterface nic, Func<Game, Task> onGame, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
        udp.JoinMulticastGroup(Group, nic.Address);
        while (true)
        {
            var result = await udp.ReceiveAsync(ct);
            if (!nic.Contains(result.RemoteEndPoint.Address)) continue;
            var game = Parse(result.Buffer, result.RemoteEndPoint.Address);
            if (game != null) await onGame(game);
        }
    }
    public static async Task Announce(LanInterface nic, string motd, int port, CancellationToken ct)
    {
        using var udp = new UdpClient(new IPEndPoint(nic.Address, 0));
        udp.MulticastLoopback = true;
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());
        var bytes = Encoding.UTF8.GetBytes($"[MOTD]{motd}[/MOTD][AD]{port}[/AD]");
        await udp.SendAsync(bytes, new IPEndPoint(Group, 4445), ct);
    }
}
