using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LanPin;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test")
        {
            try { SelfTest.Run().GetAwaiter().GetResult(); return 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "FAIL\n" + e); return 1; }
        }
        ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); return 0;
    }
}

sealed class MainForm : Form
{
    readonly ComboBox role = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox network = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox address = new();
    readonly TextBox pin = new() { PlaceholderText = "存档接入方填写加入方给出的 PIN" };
    readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 38443 };
    readonly TextBox manual = new() { PlaceholderText = "可选：192.168.1.10:游戏端口 或 127.0.0.1:端口" };
    readonly Button start = new() { Text = "开始分享 / 连接", AutoSize = true };
    readonly Button end = new() { Text = "停止", Enabled = false, AutoSize = true };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly ListBox games = new() { Dock = DockStyle.Fill };
    readonly Label status = new() { Text = "未连接", AutoSize = true, ForeColor = Color.FromArgb(35, 100, 150) };
    Session? session;
    Task? run;
    bool closing;
    public MainForm()
    {
        Text = "LanPin v0.1.1 · Minecraft Java 局域网联机"; MinimumSize = new(780, 700); Size = new(880, 800);
        Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 13, Padding = new Padding(24) };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        void Row(int i, string title, Control value) { layout.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, i); value.Dock = DockStyle.Fill; value.Margin = new Padding(3, 4, 3, 4); layout.Controls.Add(value, 1, i); }
        var intro = new Label { Text = "IPv6 点对点 · PIN 加密配对 · 自动发现 Java 版 LAN 存档", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 2);
        role.Items.AddRange(["加入方：发现并加入远端存档", "存档接入方：接入本机或内网存档"]); role.SelectedIndex = 0;
        Row(1, "本机角色", role); Row(2, "本机内网接口", network); Row(3, "加入方 IPv6", address); Row(4, "加入方 TCP 端口", port); Row(5, "PIN（请私下传递）", pin); Row(6, "手动游戏地址", manual);
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var copy = new Button { Text = "复制分享信息", AutoSize = true };
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(pin.Text)) Clipboard.SetText($"IPv6: {address.Text}\n端口: {port.Value}\nPIN: {pin.Text}"); };
        var refresh = new Button { Text = "刷新网卡", AutoSize = true }; refresh.Click += (_, _) => RefreshNetworks();
        bar.Controls.AddRange([start, end, copy, refresh]); layout.Controls.Add(bar, 0, 7); layout.SetColumnSpan(bar, 2);
        layout.Controls.Add(status, 0, 8); layout.SetColumnSpan(status, 2);
        var help = new Label { Text = "存档接入方：连接存档所在内网；存档电脑需对局域网开放并保持运行。\n加入方：在多人游戏中选择〈LanPin〉存档加入。\n两端须有可互通的 IPv6；只需加入方放行入站 TCP 端口。此版不创建虚拟 IP，不支持基岩版。", AutoSize = true };
        layout.Controls.Add(help, 0, 9); layout.SetColumnSpan(help, 2);
        layout.Controls.Add(games, 0, 10); layout.SetColumnSpan(games, 2);
        layout.Controls.Add(log, 0, 11); layout.SetColumnSpan(log, 2);
        var note = new Label { Text = "双击存档条目可复制备用连接地址。退出会关闭分享；软件不会修改系统代理、路由或防火墙。", AutoSize = true };
        layout.Controls.Add(note, 0, 12); layout.SetColumnSpan(note, 2);
        for (int i = 0; i < 13; i++) layout.RowStyles.Add(new(i == 10 || i == 11 ? SizeType.Percent : SizeType.AutoSize, i == 10 ? 35 : i == 11 ? 65 : 0));
        games.DoubleClick += (_, _) => { if (games.SelectedItem is string s && s.Contains('→')) Clipboard.SetText(s.Split('→')[1].Split('（')[0].Trim()); };
        role.SelectedIndexChanged += (_, _) => Mode();
        start.Click += async (_, _) => await Start();
        end.Click += async (_, _) => await Stop();
        FormClosing += async (_, e) =>
        {
            if (session == null || closing) return;
            e.Cancel = true; closing = true; await Stop(); Close();
        };
        RefreshNetworks(); Mode();
    }
    void RefreshNetworks()
    {
        if (session != null) return;
        network.Items.Clear(); foreach (var n in LanInterface.All()) network.Items.Add(n);
        if (network.Items.Count > 0) network.SelectedIndex = 0;
        if (role.SelectedIndex == 0) address.Text = Ipv6();
    }
    static string Ipv6() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(x => x.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !a.IsIPv6Multicast && !IPAddress.IsLoopback(a) && (a.GetAddressBytes()[0] & 0xe0) == 0x20)
        .Select(a => a.ToString()).FirstOrDefault() ?? "";
    void Mode()
    {
        bool active = role.SelectedIndex == 0; address.ReadOnly = false; pin.ReadOnly = active; manual.Enabled = !active;
        address.Text = active ? Ipv6() : ""; pin.Clear();
    }
    void Log(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        if (log.TextLength > 50000) log.Clear();
        log.AppendText($"{DateTime.Now:HH:mm:ss}  {text}{Environment.NewLine}");
        status.Text = text.Length > 90 ? text[..90] : text;
    }
    void GameLog(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => GameLog(text)); return; }
        if (!games.Items.Contains(text)) games.Items.Add(text);
    }
    async Task Start()
    {
        try
        {
            if (network.SelectedItem is not LanInterface nic) throw new ArgumentException("没有找到私有 IPv4 网卡，请先连接本地网络");
            Game? target = null;
            if (role.SelectedIndex == 1 && !string.IsNullOrWhiteSpace(manual.Text))
            {
                if (!IPEndPoint.TryParse(manual.Text.Trim(), out var endpoint) || endpoint.AddressFamily != AddressFamily.InterNetwork || endpoint.Port == 0)
                    throw new ArgumentException("手动地址格式：192.168.1.10:端口");
                target = new(endpoint.ToString(), "手动指定的 Java 存档", endpoint.Address.ToString(), endpoint.Port);
            }
            if (role.SelectedIndex == 0) pin.Text = Protocol.NewPin(); else Protocol.Pin(pin.Text);
            games.Items.Clear(); session = new Session(Log, GameLog);
            role.Enabled = network.Enabled = port.Enabled = start.Enabled = manual.Enabled = false;
            address.ReadOnly = pin.ReadOnly = true; end.Enabled = true;
            var current = session;
            run = Run(current, role.SelectedIndex == 0, nic, target);
            await Task.Yield();
        }
        catch (Exception e) { Log(e.Message); await Stop(); }
    }
    async Task Run(Session current, bool active, LanInterface nic, Game? target)
    {
        try
        {
            if (active) await current.Share(pin.Text, (int)port.Value, nic);
            else await current.Join(address.Text, pin.Text, (int)port.Value, nic, target);
        }
        catch (Exception e) { if (session == current) Log(e is OperationCanceledException ? "连接已结束或超时" : e.Message); }
        finally
        {
            await current.DisposeAsync();
            if (session == current) { session = null; Reset(); }
        }
    }
    async Task Stop()
    {
        var current = session; session = null;
        if (current != null) await current.DisposeAsync();
        if (run != null) await run;
        Reset();
    }
    void Reset()
    {
        role.Enabled = network.Enabled = port.Enabled = start.Enabled = true;
        end.Enabled = false; address.ReadOnly = false; pin.ReadOnly = role.SelectedIndex == 0;
        manual.Enabled = role.SelectedIndex == 1;
        if (role.SelectedIndex == 0) pin.Clear();
        status.Text = "未连接；可以重新开始";
    }
}
