using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LanPin;

record Message(string Type, string Id = "", Game? Game = null, string Target = "");
sealed class Channel(SslStream stream) : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task Send(Message message, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await Protocol.WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes(message), ct); }
        finally { gate.Release(); }
    }
    public async Task<Message> Read(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var data = await Protocol.ReadFrame(stream, timeout.Token);
        if (data.Length > 4096) throw new IOException("控制消息过长");
        return JsonSerializer.Deserialize<Message>(data) ?? throw new IOException("控制消息无效");
    }
    public async Task Heartbeat(CancellationToken ct)
    {
        while (true) { await Task.Delay(5000, ct); await Send(new("ping"), ct); }
    }
    public void Dispose() => stream.Dispose();
}

sealed class Session : IAsyncDisposable
{
    readonly Action<string> log;
    readonly Action<string> gameLog;
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentDictionary<string, (Game Game, DateTime Seen)> games = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<SslStream>> pending = new();
    readonly ConcurrentDictionary<string, LocalGame> localGames = new();
    readonly ConcurrentDictionary<int, Task> tasks = new();
    readonly SemaphoreSlim slots = new(32);
    readonly SemaphoreSlim handshakes = new(16);
    TcpListener? listener;
    Channel? control;
    int claimed, taskId;
    byte[] pin = [];
    X509Certificate2? certificate;
    IPAddress? activeAddress;
    int port;
    LanInterface? nic;
    public Session(Action<string> log, Action<string> gameLog) { this.log = log; this.gameLog = gameLog; }
    void Track(Task task)
    {
        int id = Interlocked.Increment(ref taskId); tasks[id] = task;
        _ = task.ContinueWith(t => { tasks.TryRemove(id, out _); if (t.IsFaulted && !stop.IsCancellationRequested) log(t.Exception!.GetBaseException().Message); }, TaskScheduler.Default);
    }
    public async Task Share(string code, int listenPort, LanInterface lan)
    {
        pin = Protocol.Pin(code); port = listenPort; nic = lan; certificate = Protocol.Certificate();
        listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = false; listener.Start(16);
        log($"等待存档接入方连接，IPv6 TCP 端口 {port}。PIN 在等待 10 分钟后失效，配对成功后仅供当前连接使用。");
        var expires = DateTime.UtcNow.AddMinutes(10);
        using var lifetime = stop.Token.Register(() => listener.Stop());
        while (!stop.IsCancellationRequested)
        {
            if (claimed == 0 && DateTime.UtcNow >= expires) throw new TimeoutException("PIN 已过期，请停止后重新分享");
            using var acceptDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            if (claimed == 0) acceptDeadline.CancelAfter(expires - DateTime.UtcNow);
            var client = await listener.AcceptTcpClientAsync(acceptDeadline.Token);
            if (!await handshakes.WaitAsync(0, stop.Token)) { client.Dispose(); continue; }
            Track(Accept(client));
        }
    }
    async Task Accept(TcpClient client)
    {
        SslStream? tls = null; bool handedOff = false;
        try
        {
            client.NoDelay = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(10000);
            tls = new SslStream(client.GetStream(), false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
            await Protocol.Authenticate(tls, pin, true, certificate!.RawData, timeout.Token);
            var hello = JsonSerializer.Deserialize<Message>(await Protocol.ReadFrame(tls, timeout.Token));
            if (hello?.Type == "data" && pending.TryRemove(hello.Id, out var source))
            { handedOff = source.TrySetResult(tls); return; }
            if (hello?.Type != "control" || Interlocked.CompareExchange(ref claimed, 1, 0) != 0) throw new IOException("仅允许一个存档接入方配对");
            log("PIN 验证成功，正在接收 Java 版局域网存档。");
            using var channel = new Channel(tls); control = channel;
            Track(Guard(channel.Heartbeat(stop.Token)));
            try
            {
                while (true)
                {
                    var message = await channel.Read(stop.Token);
                    if (message.Type == "game" && message.Game is { } game && game.Key.Length <= 128 && game.Motd.Length <= 256)
                    {
                        if (localGames.TryGetValue(game.Key, out var local)) local.Seen = DateTime.UtcNow;
                        else if (localGames.Count < 16)
                        {
                            var entry = new LocalGame(game, nic!, this); localGames[game.Key] = entry;
                            Track(entry.Run(stop.Token));
                        }
                    }
                }
            }
            finally { control = null; stop.Cancel(); }
        }
        finally { if (!handedOff) { tls?.Dispose(); client.Dispose(); } handshakes.Release(); }
    }
    public async Task Join(string address, string code, int listenPort, LanInterface lan, Game? manual = null)
    {
        if (!IPAddress.TryParse(address.Trim().Trim('[', ']'), out activeAddress) || activeAddress.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("请输入加入方的 IPv6 地址，不带端口");
        pin = Protocol.Pin(code); port = listenPort; nic = lan;
        var tls = await Dial(stop.Token);
        using var channel = new Channel(tls); control = channel;
        await channel.Send(new("control"), stop.Token);
        log("PIN 验证成功。请在此网段的 Minecraft Java 版中选择“对局域网开放”。");
        Track(Guard(channel.Heartbeat(stop.Token)));
        async Task Found(Game game)
        {
            if (game.Motd.StartsWith("〈LanPin〉 ", StringComparison.Ordinal)) return;
            if (!games.ContainsKey(game.Key) && games.Count >= 16)
            {
                foreach (var old in games.Where(x => DateTime.UtcNow - x.Value.Seen > TimeSpan.FromSeconds(20))) games.TryRemove(old.Key, out _);
                if (games.Count >= 16) return;
            }
            bool fresh = games.TryAdd(game.Key, (game, DateTime.UtcNow));
            games[game.Key] = (game, DateTime.UtcNow);
            if (fresh) { log($"发现存档：{game.Motd} · {game.Key}"); gameLog($"{game.Motd} → {game.Key}"); }
            await channel.Send(new("game", Game: game), stop.Token);
        }
        Track(Guard(Discovery.Listen(lan, Found, stop.Token)));
        if (manual != null)
        {
            if (!IPAddress.TryParse(manual.Host, out var host) || (!lan.Contains(host) && !IPAddress.IsLoopback(host)) || manual.Port is < 1 or > 65535)
                throw new ArgumentException("手动游戏地址必须位于所选内网，或为 127.0.0.1");
            Track(Guard(Manual()));
            async Task Manual() { while (true) { await Found(manual); await Task.Delay(1500, stop.Token); } }
        }
        try
        {
            while (true)
            {
                var message = await channel.Read(stop.Token);
                if (message.Type == "open" && message.Id.Length == 32 && games.TryGetValue(message.Target, out var game)
                    && DateTime.UtcNow - game.Seen < TimeSpan.FromSeconds(20) && await slots.WaitAsync(0, stop.Token))
                    Track(ConnectGame(message.Id, game.Game));
            }
        }
        finally { control = null; stop.Cancel(); }
    }
    async Task<SslStream> Dial(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(10000);
        var client = new TcpClient(AddressFamily.InterNetworkV6) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await client.ConnectAsync(activeAddress!, port, timeout.Token);
            // The self-signed certificate is authenticated by the subsequent certificate-bound PIN proofs.
            tls = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "LanPin", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
            await Protocol.Authenticate(tls, pin, false, tls.RemoteCertificate!.GetRawCertData(), timeout.Token);
            return tls;
        }
        catch { tls?.Dispose(); client.Dispose(); throw; }
    }
    async Task ConnectGame(string id, Game game)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(10000);
            using var target = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            await target.ConnectAsync(IPAddress.Parse(game.Host), game.Port, deadline.Token);
            using var tls = await Dial(deadline.Token);
            await Protocol.WriteFrame(tls, JsonSerializer.SerializeToUtf8Bytes(new Message("data", id)), deadline.Token);
            await Relay(tls, target.GetStream(), stop.Token);
        }
        finally { slots.Release(); }
    }
    public static async Task Relay(Stream a, Stream b, CancellationToken ct)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task first = a.CopyToAsync(b, done.Token), second = b.CopyToAsync(a, done.Token);
        await Task.WhenAny(first, second); done.Cancel();
        try { await Task.WhenAll(first, second); } catch (OperationCanceledException) when (done.IsCancellationRequested) { }
    }
    sealed class LocalGame
    {
        readonly Game game; readonly LanInterface nic; readonly Session owner;
        public DateTime Seen = DateTime.UtcNow;
        public LocalGame(Game game, LanInterface nic, Session owner) { this.game = game; this.nic = nic; this.owner = owner; }
        public async Task Run(CancellationToken ct)
        {
            // Bind to the selected local interface so the discovery sender IP matches the game endpoint.
            var proxy = new TcpListener(nic.Address, 0); proxy.Start(8);
            int localPort = ((IPEndPoint)proxy.LocalEndpoint).Port;
            owner.gameLog($"{game.Motd} → {nic.Address}:{localPort}（备用手动连接）");
            owner.log($"已映射存档：{game.Motd} → {nic.Address}:{localPort}");
            using var ended = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var broadcast = Broadcast(localPort, ended.Token);
            _ = broadcast.ContinueWith(_ => ended.Cancel(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            using var closing = ended.Token.Register(proxy.Stop);
            try
            {
                while (true)
                {
                    var player = await proxy.AcceptTcpClientAsync(ended.Token);
                    // Only this computer may use the proxy, even though announcements use a LAN interface.
                    var remote = ((IPEndPoint)player.Client.RemoteEndPoint!).Address;
                    if (!remote.Equals(nic.Address) && !IPAddress.IsLoopback(remote)) { player.Dispose(); continue; }
                    if (!await owner.slots.WaitAsync(0, ct)) { player.Dispose(); continue; }
                    owner.Track(Forward(player, ended.Token));
                }
            }
            finally
            {
                ended.Cancel(); proxy.Stop(); owner.localGames.TryRemove(game.Key, out _);
                try { await broadcast; } catch (OperationCanceledException) { }
            }
        }
        async Task Broadcast(int port, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && DateTime.UtcNow - Seen < TimeSpan.FromSeconds(15))
            {
                await Discovery.Announce(nic, "〈LanPin〉 " + game.Motd, port, ct); await Task.Delay(1500, ct);
            }
            // Stop the listener when a game stops announcing.
            if (!ct.IsCancellationRequested) throw new IOException($"存档已停止广播：{game.Motd}");
        }
        async Task Forward(TcpClient player, CancellationToken ct)
        {
            string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var ready = new TaskCompletionSource<SslStream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.pending[id] = ready;
            try
            {
                player.NoDelay = true;
                await (owner.control ?? throw new IOException("存档接入方已断开")).Send(new("open", id, Target: game.Key), ct);
                using var tls = await ready.Task.WaitAsync(TimeSpan.FromSeconds(12), ct);
                await Relay(player.GetStream(), tls, ct);
            }
            finally
            {
                owner.pending.TryRemove(id, out _);
                if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.Dispose();
                player.Dispose(); owner.slots.Release();
            }
        }
    }
    async Task Guard(Task task)
    {
        try { await task; }
        catch (Exception e) when (!stop.IsCancellationRequested) { log(e.Message); stop.Cancel(); }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener?.Stop(); control?.Dispose();
        while (!tasks.IsEmpty) await Task.WhenAll(tasks.Values.Select(async t => { try { await t; } catch { } }));
        foreach (var item in pending.Values) if (item.Task.IsCompletedSuccessfully) item.Task.Result.Dispose();
        pending.Clear(); certificate?.Dispose(); CryptographicOperations.ZeroMemory(pin);
    }
}
