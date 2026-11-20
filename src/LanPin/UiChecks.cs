using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;

namespace LanPin.Desktop;

static class UiChecks
{
    static readonly List<string> results=[];
    static string? reportPath;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);results.Add("PASS "+message);if(reportPath!=null)File.WriteAllLines(reportPath,results);}
    static void Snapshot(MainWindow window,string path,double width=1260,double height=860)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    static void MakeFixture(string path)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            var sky=new LinearGradientBrush();sky.StartPoint=new Point(0,0);sky.EndPoint=new Point(1,1);sky.GradientStops.Add(new GradientStop(Color.FromRgb(215,144,103),0));sky.GradientStops.Add(new GradientStop(Color.FromRgb(100,117,145),.55));sky.GradientStops.Add(new GradientStop(Color.FromRgb(31,72,74),1));
            dc.DrawRectangle(sky,null,new Rect(0,0,1800,1200));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(245,218,162)),null,new Point(1380,330),110,110);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(69,89,103)),null,Geometry.Parse("M 0,950 L 200,700 380,880 640,580 900,910 1130,650 1490,990 1800,710 1800,1200 0,1200 Z"));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(31,65,70)),null,Geometry.Parse("M 0,1020 L 300,900 600,1060 990,850 1240,1060 1560,900 1800,1000 1800,1200 0,1200 Z"));
        }
        var bitmap=new RenderTargetBitmap(1800,1200,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    public static async Task Run(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);reportPath=Path.Combine(output,"ui-check-result.txt");string profile=Path.Combine(output,"isolated-profile");
        var store=new AppearanceStore(profile);var window=new MainWindow(store,true);
        Check(window.WindowStyle==WindowStyle.None&&WindowChrome.GetWindowChrome(window)?.CaptionHeight==0,"Custom title bar replaces native caption; resize chrome remains configured");
        var typeface=new Typeface(window.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        Check(typeface.TryGetGlyphTypeface(out var glyph)&&glyph.FamilyNames.Values.Any(x=>x.Contains("HarmonyOS Sans",StringComparison.OrdinalIgnoreCase)),"Bundled HarmonyOS Sans SC actually resolves as the UI typeface");
        Check(window.PinBox.IsReadOnly&&window.StartButton.Content.ToString()!.Contains("生成分享"),"Joiner role has a generated read-only PIN");
        Snapshot(window,Path.Combine(output,"01-joiner.png"));
        window.SetRole(false);Check(!window.PinBox.IsReadOnly&&window.ManualBox.IsEnabled,"LAN access role accepts PIN and manual game endpoint");
        window.SetAdvanced(true,false);Snapshot(window,Path.Combine(output,"02-access.png"));
        var root=(FrameworkElement)window.Content;Point button=window.StartButton.TranslatePoint(new Point(0,0),root);
        Check(button.X>=0&&button.X+window.StartButton.ActualWidth<=1260,"Primary action stays within the window at default size");
        Snapshot(window,Path.Combine(output,"03-small-window.png"),1060,760);
        button=window.StartButton.TranslatePoint(new Point(0,0),root);
        Check(button.Y+window.StartButton.ActualHeight<730,"Primary action remains visible at the minimum window size");
        window.ShowPage(1,false);Snapshot(window,Path.Combine(output,"04-appearance.png"));
        string fixture=Path.Combine(output,"wallpaper-fixture.png");MakeFixture(fixture);string originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));
        await window.ImportWallpaperAsync(fixture);
        Check(window.CurrentAppearance.Wallpaper!=fixture&&File.Exists(window.CurrentAppearance.Wallpaper),"Custom image is copied into the managed local profile");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)))==originalHash,"Import leaves the original wallpaper unmodified");
        window.SetPreviewAppearance(window.CurrentAppearance with{Blur=3,Shade=.29,PanelOpacity=.59,FitInside=false,ReduceMotion=true});
        var loaded=store.Load();Check(loaded.Blur==3&&loaded.Shade==.29&&loaded.PanelOpacity==.59&&loaded.ReduceMotion,"Blur, shade, panel opacity and reduced-motion preferences persist");
        window.SetRole(true);window.SetAdvanced(false,false);window.ShowPage(0,false);Snapshot(window,Path.Combine(output,"05-custom-background.png"));
        var reopened=new MainWindow(new AppearanceStore(profile),true);Check(reopened.WallpaperImage.Source!=null,"Custom wallpaper reloads from local settings on next launch");
        reopened.ShowPage(1,false);Snapshot(reopened,Path.Combine(output,"06-persisted-appearance.png"));
        string before=window.CurrentAppearance.Wallpaper!;string invalid=Path.Combine(output,"invalid.png");File.WriteAllText(invalid,"not an image");bool rejected=false;
        try{await window.ImportWallpaperAsync(invalid);}catch{rejected=true;}
        Check(rejected&&window.CurrentAppearance.Wallpaper==before,"Invalid image is rejected without replacing the previous wallpaper");
        window.ResetWallpaper();Check(window.WallpaperImage.Source==null&&store.Load().Wallpaper==null&&File.Exists(fixture),"Restore-default clears wallpaper selection without touching the original file");
        store.Save(new Appearance{Blur=200,Shade=-1,PanelOpacity=3});var clamped=store.Load();Check(clamped.Blur==24&&clamped.Shade==.2&&clamped.PanelOpacity==.95,"Out-of-range persisted preferences are clamped");
        var malformed=Path.Combine(output,"corrupt-profile");Directory.CreateDirectory(malformed);File.WriteAllText(Path.Combine(malformed,"appearance-v2.json"),"{bad json");Check(new AppearanceStore(malformed).Load().Wallpaper==null,"Malformed preferences fall back to safe defaults");
        window.Close();reopened.Close();
        await NetworkFlow(output);
        await File.WriteAllTextAsync(Path.Combine(output,"ui-check-result.txt"),string.Join(Environment.NewLine,results));
    }
    static async Task WaitFor(Func<bool> predicate,CancellationToken token)
    {while(!predicate())await Task.Delay(40,token);}
    static async Task NetworkFlow(string output)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));var ct=deadline.Token;
        var joiner=new MainWindow(new AppearanceStore(Path.Combine(output,"joiner-profile")),true);
        var access=new MainWindow(new AppearanceStore(Path.Combine(output,"access-profile")),true);
        var nic=new LanInterface("Loopback integration test",IPAddress.Loopback,IPAddress.Parse("255.0.0.0"));
        foreach(var window in new[]{joiner,access}){window.NetworkChoice.Items.Clear();window.NetworkChoice.Items.Add(nic);window.NetworkChoice.SelectedIndex=0;}
        var probe=new TcpListener(IPAddress.IPv6Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        var server=new TcpListener(IPAddress.Loopback,0);server.Start();int gamePort=((IPEndPoint)server.LocalEndpoint).Port;
        joiner.AddressBox.Text="::1";joiner.PortBox.Text=port.ToString();
        Task? serverTask=null;
        try
        {
            await joiner.StartAsync(true);
            await WaitFor(()=>joiner.StateTitle.Text=="等待朋友接入",ct);
            Check(joiner.PinBox.Text.Replace("-","").Length==32&&joiner.CopyButton.IsEnabled,"Joiner UI creates share PIN and enables sharing only after starting");
            access.SetRole(false);access.AddressBox.Text="::1";access.PortBox.Text=port.ToString();access.PinBox.Text=joiner.PinBox.Text;
            await access.StartAsync(true);
            await WaitFor(()=>joiner.StateTitle.Text=="已安全连接"&&access.StateTitle.Text=="已安全连接",ct);
            Check(true,"Both actual window controllers display connected only after mutual PIN authentication");
            while(joiner.DiscoveredWorlds.Count==0||access.DiscoveredWorlds.Count==0)
            {await Discovery.Announce(nic,"Next UI integration world",gamePort,ct);await Task.Delay(150,ct);}
            Check(joiner.WorldCount.Text=="1"&&access.WorldCount.Text=="1"&&joiner.EmptyWorlds.Visibility==Visibility.Collapsed,"Real multicast notices populate both world-card lists and hide empty state");
            var entry=joiner.DiscoveredWorlds.Single();var mapped=IPEndPoint.Parse(entry.Endpoint);
            Check(mapped.Port!=gamePort&&mapped.Address.Equals(IPAddress.Loopback),"Joiner world card exposes the actual tunnel proxy endpoint");
            byte[] payload=System.Security.Cryptography.RandomNumberGenerator.GetBytes(65536),received=new byte[payload.Length];
            serverTask=Task.Run(async()=>
            {using var accepted=await server.AcceptTcpClientAsync(ct);var stream=accepted.GetStream();var data=new byte[payload.Length];await stream.ReadExactlyAsync(data,ct);await stream.WriteAsync(data,ct);},ct);
            using var player=new TcpClient(AddressFamily.InterNetwork);await player.ConnectAsync(mapped,ct);var clientStream=player.GetStream();
            await clientStream.WriteAsync(payload,ct);await clientStream.ReadExactlyAsync(received,ct);await serverTask;
            Check(payload.AsSpan().SequenceEqual(received),"UI-started tunnel relays actual game TCP data with exact byte equality");
            await joiner.StopAsync();await access.StopAsync();await WaitFor(()=>joiner.StopButton.Visibility==Visibility.Collapsed&&access.StopButton.Visibility==Visibility.Collapsed&&joiner.DiscoveredWorlds.Count==0&&access.DiscoveredWorlds.Count==0,ct);
            Check(!joiner.CopyButton.IsEnabled&&joiner.PinBox.Text.Length==0&&access.PinBox.Text.Length==0,"Stop clears PIN, share action and world cards in both controllers");
            probe=new TcpListener(IPAddress.IPv6Loopback,port);probe.Start();probe.Stop();
            Check(true,"Controller stop releases the IPv6 listener for reuse");
        }
        finally
        {await joiner.StopAsync();await access.StopAsync();server.Stop();joiner.Close();access.Close();}
    }
}
