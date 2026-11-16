using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LanPin;

static class SelfTest
{
    static readonly List<string> results = [];
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        results.Add("PASS " + name);
    }
    public static async Task Run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        Check(Protocol.Pin(Protocol.NewPin()).Length == 16, "PIN contains 128 random bits");
        bool invalid = false; try { Protocol.Pin("123456"); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "Short PIN rejected");
        var announcement = Encoding.UTF8.GetBytes("[MOTD]Test world[/MOTD][AD]25565[/AD]");
        var game = Discovery.Parse(announcement, IPAddress.Loopback);
        Check(game is { Port: 25565, Motd: "Test world" }, "Java LAN announcement parsed");
        Check(Discovery.Parse(Encoding.UTF8.GetBytes("[MOTD]x[/MOTD][AD]65536[/AD]"), IPAddress.Loopback) == null, "Invalid game port rejected");
        await AuthTest(false, false, ct); await AuthTest(true, false, ct); await AuthTest(false, true, ct);
        // A loopback multicast receiver exercises the actual Java discovery wire format.
        using (var udp = new UdpClient(AddressFamily.InterNetwork))
        {
            udp.ExclusiveAddressUse = false;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
            udp.JoinMulticastGroup(Discovery.Group, IPAddress.Loopback);
            var local = new LanInterface("virtual-test-loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
            await Discovery.Announce(local, "virtual-world", 25565, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(5000);
            var found = await udp.ReceiveAsync(timeout.Token);
            Check(Discovery.Parse(found.Buffer, found.RemoteEndPoint.Address)?.Motd == "virtual-world", "Real IPv4 multicast discovery on simulated LAN");
        }
        await EndToEnd(ct);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"),
            $"LanPin virtual network test\nUTC: {DateTime.UtcNow:O}\nRuntime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\nTopology: active [::1] <TLS over IPv6> passive [::1] -> simulated Java TCP server 127.0.0.1\n" +
            string.Join("\n", results) + "\nLimit: no real Minecraft client or two-host Internet test was performed.\n", ct);
    }
    static async Task AuthTest(bool wrongPin, bool wrongBinding, CancellationToken ct)
    {
        using var cert = Protocol.Certificate();
        byte[] pin = RandomNumberGenerator.GetBytes(16);
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0); listener.Start();
        try
        {
            async Task Server()
            {
                using var accepted = await listener.AcceptTcpClientAsync(ct);
                using var tls = new SslStream(accepted.GetStream(), false);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, ct);
                await Protocol.Authenticate(tls, pin, true, cert.RawData, ct);
            }
            var server = Server();
            async Task Client()
            {
                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(IPAddress.IPv6Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
                using var tls = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "test", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, ct);
                await Protocol.Authenticate(tls, wrongPin ? RandomNumberGenerator.GetBytes(16) : pin, false,
                    wrongBinding ? new byte[] { 0, 1, 2 } : tls.RemoteCertificate!.GetRawCertData(), ct);
            }
            var clientTask = Client();
            bool failed = false;
            try { await Task.WhenAll(server, clientTask); } catch (AuthenticationException e) { failed = true; if (!wrongPin && !wrongBinding) throw new Exception("TLS failed: " + e, e); }
            Check(failed == (wrongPin || wrongBinding), wrongPin ? "Wrong PIN rejected by TLS peers" : wrongBinding ? "Certificate binding mismatch rejected (MITM relay defense)" : "Correct PIN mutual authentication over real IPv6 TLS");
        }
        finally { listener.Stop(); }
    }
    static async Task EndToEnd(CancellationToken ct)
    {
        var gameServer = new TcpListener(IPAddress.Loopback, 0); gameServer.Start();
        var probe = new TcpListener(IPAddress.IPv6Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var trace = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var mapped = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Mapping(string text)
        {
            var value = text.Split('→')[1].Split('（')[0].Trim();
            if (IPEndPoint.TryParse(value, out var ep)) mapped.TrySetResult(ep);
        }
        var nic = new LanInterface("virtual-test-loopback", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        var active = new Session(trace.Enqueue, Mapping); var passive = new Session(trace.Enqueue, _ => { });
        string pin = Protocol.NewPin();
        Task activeTask = active.Share(pin, port, nic);
        int targetPort = ((IPEndPoint)gameServer.LocalEndpoint).Port;
        Task passiveTask = passive.Join("::1", pin, port, nic);
        using var servers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var echoes = new List<Task>();
        async Task EchoLoop()
        {
            try
            {
                while (!servers.IsCancellationRequested)
                {
                    var accepted = await gameServer.AcceptTcpClientAsync(servers.Token);
                    echoes.Add(Echo(accepted));
                }
            }
            catch (OperationCanceledException) { }
        }
        async Task Echo(TcpClient accepted)
        {
            using (accepted)
            {
                var stream = accepted.GetStream(); byte[] bytes = new byte[8192];
                int size; while ((size = await stream.ReadAsync(bytes, servers.Token)) > 0) await stream.WriteAsync(bytes.AsMemory(0, size), servers.Token);
            }
        }
        var echoLoop = EchoLoop(); IPEndPoint? localEndpoint = null;
        async Task Advertise()
        {
            while (true)
            {
                await Discovery.Announce(nic, "virtual-world", targetPort, servers.Token);
                await Task.Delay(500, servers.Token);
            }
        }
        var advertise = Advertise();
        try
        {
            localEndpoint = await mapped.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            Check(true, "Automatic discovery -> passive control channel -> active proxy, without manual target");
            Check(localEndpoint.Address.Equals(IPAddress.Loopback), "Remote world mapped to active-side local TCP listener");
            async Task Player(int index)
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(localEndpoint, ct);
                byte[] sent = RandomNumberGenerator.GetBytes(1024 * 1024), received = new byte[sent.Length];
                var stream = client.GetStream();
                Task write = stream.WriteAsync(sent, ct).AsTask();
                await stream.ReadExactlyAsync(received, ct); await write;
                if (!sent.AsSpan().SequenceEqual(received)) throw new Exception($"Data mismatch for player {index}");
            }
            await Task.WhenAll(Enumerable.Range(0, 4).Select(Player));
            Check(true, "Four concurrent game connections, 4 MiB round-trip, exact byte comparison");
        }
        catch (Exception e) { throw new Exception(e.Message + "\n" + string.Join("\n", trace), e); }
        finally
        {
            await passive.DisposeAsync(); await active.DisposeAsync();
            try { await passiveTask; } catch { } try { await activeTask; } catch { }
            servers.Cancel(); gameServer.Stop();
            try { await advertise; } catch (OperationCanceledException) { }
            try { await echoLoop; await Task.WhenAll(echoes); } catch (OperationCanceledException) { }
        }
        var rebound = new TcpListener(IPAddress.IPv6Loopback, port); rebound.Start(); rebound.Stop();
        var reboundGame = new TcpListener(localEndpoint!); reboundGame.Start(); reboundGame.Stop();
        Check(true, "Stop releases IPv6 listener and local game proxy port");
    }
}
