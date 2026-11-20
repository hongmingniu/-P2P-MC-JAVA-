using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace LanPin.Desktop;
static class UiChecks
{
    static readonly List<string> results=[];static string report="";
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add("PASS "+name);File.WriteAllLines(report,results);}
    static async Task Snapshot(MainWindow window,string path)
    {
        await Task.Delay(550);var render=new RenderTargetBitmap();await render.RenderAsync(window.Root,1200,800);
        var buffer=await render.GetPixelsAsync();var pixels=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)render.PixelWidth,(uint)render.PixelHeight,96,96,pixels);await encoder.FlushAsync();stream.Seek(0);
        using var input=stream.GetInputStreamAt(0);using var data=new DataReader(input);await data.LoadAsync((uint)stream.Size);var png=new byte[stream.Size];data.ReadBytes(png);await File.WriteAllBytesAsync(path,png);
    }
    static async Task Fixture(string path)
    {
        const int width=1200,height=800;var bytes=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {int at=(y*width+x)*4;double u=x/(double)width,v=y/(double)height;bytes[at]=(byte)(95+60*u);bytes[at+1]=(byte)(105+50*v);bytes[at+2]=(byte)(200-90*v);bytes[at+3]=255;}
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,width,height,96,96,bytes);await encoder.FlushAsync();
        using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);byte[] png=new byte[stream.Size];reader.ReadBytes(png);await File.WriteAllBytesAsync(path,png);
    }
    public static async Task Run(MainWindow window,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);report=Path.Combine(output,"ui-check-result.txt");
        Check(typeof(Window).Assembly.GetName().Name=="Microsoft.WinUI"&&window is Window,"Window and controls run on actual WinUI 3");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(x=>x.GetName().Name is "PresentationFramework" or "PresentationCore"),"WPF assemblies are not loaded");
        Check(window.ExtendsContentIntoTitleBar,"Content extends into the native WinUI title bar");
        Check(window.Frame.Material is "host" or "acrylic","Window uses the adopted ChatAIBox glass renderer");
        Check(window.StartButton is Button&&window.Drawer is SplitView,"Connections use native WinUI controls and SplitView");
        Check(!window.Drawer.IsPaneOpen,"Home starts with connection parameters hidden");
        window.SetPreviewAppearance(window.CurrentAppearance with{DesktopGlass=false});await Snapshot(window,Path.Combine(output,"01-home.png"));
        window.OpenConnectionDrawer();await Snapshot(window,Path.Combine(output,"02-connection.png"));
        Check(window.Drawer.IsPaneOpen&&window.ConnectionPane.Visibility==Visibility.Visible,"Primary action opens the connection pane");
        Check(window.PinBox.IsReadOnly&&window.StartButton.Content.ToString()!.Contains("生成分享"),"Joining player has a generated read-only PIN");
        window.SetRole(false);Check(!window.PinBox.IsReadOnly&&window.ManualBox.IsEnabled,"LAN access role accepts a PIN and a manual endpoint");
        window.ShowPage(1);Check(!window.Drawer.IsPaneOpen,"Page changes dismiss the native pane");await Snapshot(window,Path.Combine(output,"03-appearance.png"));
        string fixture=Path.Combine(output,"fixture.png");await Fixture(fixture);string hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture)));
        await window.ImportWallpaperAsync(fixture);Check(window.CurrentAppearance.Wallpaper!=fixture&&File.Exists(window.CurrentAppearance.Wallpaper),"Wallpaper import creates a managed copy");
        Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture)))==hash,"Wallpaper import leaves the original file unchanged");
        window.SetPreviewAppearance(window.CurrentAppearance with{Blur=12,Shade=.4,WallpaperBlend=.28,PanelOpacity=.8,ReduceMotion=true});await window.RefreshWallpaperAsync();
        var profile=new AppearanceStore(Path.Combine(output,"profile"));var saved=profile.Load();Check(saved.Blur==12&&saved.Shade==.4&&saved.ReduceMotion&&saved.WallpaperBlend==.28,"Appearance and reduced-motion preferences persist");
        var photo=await PhotoData.ReadAsync(fixture);var before=(byte[])photo.Pixels.Clone();var blurred=await Task.Run(()=>photo.Blur(12));Check(photo.Pixels.AsSpan().SequenceEqual(before)&&!blurred.AsSpan().SequenceEqual(before),"Background blur changes a separate pixel buffer and preserves the original");
        window.SetRole(true);window.ShowPage(0);await Snapshot(window,Path.Combine(output,"04-wallpaper.png"));
        var reopened=new MainWindow(profile,true);reopened.Activate();await reopened.Loaded;Check(reopened.WallpaperImage.Source!=null,"Managed wallpaper reloads in a new WinUI window");reopened.Close();
        string invalid=Path.Combine(output,"invalid.png");await File.WriteAllTextAsync(invalid,"not an image");string? previous=window.CurrentAppearance.Wallpaper;bool rejected=false;try{await window.ImportWallpaperAsync(invalid);}catch{rejected=true;}
        Check(rejected&&window.CurrentAppearance.Wallpaper==previous,"Invalid image is rejected without replacing the previous wallpaper");
        window.ResetWallpaper();Check(window.WallpaperImage.Source==null&&File.Exists(fixture),"Reset clears the wallpaper without deleting the source image");
        window.SetPreviewAppearance(new Appearance{Blur=999,Shade=-1,WallpaperBlend=9,PanelOpacity=3});Check(window.CurrentAppearance is{Blur:24,Shade:.2,WallpaperBlend:.8,PanelOpacity:.95},"Persisted appearance values are bounded");
        window.SetPreviewAppearance(new Appearance{DesktopGlass=false});Check(window.Frame.Material=="none","Turning glass off hides the native backdrop");
        window.SetPreviewAppearance(new Appearance{DesktopGlass=true});Check(window.Frame.Material is "host" or "acrylic","Turning glass on restores the adopted native backdrop");
        int material=window.Frame.MaterialChanges;for(int i=0;i<100;i++)window.SetPreviewAppearance(window.CurrentAppearance with{Shade=.2+(i%60)/100.0});Check(material==window.Frame.MaterialChanges,"One hundred mask changes do not recreate the native backdrop");
        var border=window.Frame.Inspect();Check(!border.Caption&&border.Left==0&&border.Top==0,"Caption is removed and client content reaches the top and left window edges");
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))Check(window.Frame.HostActive,"Windows 10 uses the validated independent host-backdrop layer");
        window.SetPreviewAppearance(window.CurrentAppearance with{Theme="dark",DesktopGlass=false,ReduceMotion=false});await Task.Delay(200);
        Check(window.IsDark&&window.StateTitle.Foreground is SolidColorBrush darkText&&darkText.Color.R>160,"Dark theme updates the root and readable text resources");
        await Snapshot(window,Path.Combine(output,"07-dark-home.png"));
        window.ShowPage(1);await Snapshot(window,Path.Combine(output,"08-dark-settings.png"));
        Check(new AppearanceStore(Path.Combine(output,"profile")).Load().Theme=="dark","Theme selection persists in the existing appearance profile");
        window.SetPreviewAppearance(window.CurrentAppearance with{Theme="light"});await Task.Delay(200);
        Check(!window.IsDark&&window.StateTitle.Foreground is SolidColorBrush lightText&&lightText.Color.R<100,"Light theme restores readable light-surface text resources");
        int moves=window.EntranceAnimations;window.ShowPage(0);Check(window.EntranceAnimations>moves&&window.LastMotionMilliseconds==250,"Page changes run a native 250 ms eased entrance animation");
        Check(window.MotionButtonCount>=10,"Shell and connection buttons have pointer and press motion hooks");
        window.SetPreviewAppearance(window.CurrentAppearance with{ReduceMotion=true});moves=window.EntranceAnimations;window.ShowPage(1);Check(window.EntranceAnimations==moves&&window.LastMotionMilliseconds==0,"Reduced-motion preference suppresses custom entrance animations");
        window.SetPreviewAppearance(new Appearance{Theme="dark",DesktopGlass=true});window.ShowPage(0);await Snapshot(window,Path.Combine(output,"09-dark-glass.png"));
        window.Frame.Update();if(window.Frame.Mode=="region")Check(window.Frame.CornersClipped(),"Native region excludes all four corners and retains the center");
        int updates=window.Frame.UpdateCount;for(int i=0;i<1000;i++)window.Frame.Update();Check(updates==window.Frame.UpdateCount,"Unchanged geometry avoids one thousand redundant native region updates");
        double scale=NativeFrame.Scale(window);window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(980*scale),(int)(680*scale)));await Task.Delay(200);window.OpenConnectionDrawer();
        await Task.Delay(550);var point=window.StartButton.TransformToVisual(window.Root).TransformPoint(new Windows.Foundation.Point(0,0));
        Check(point.X>=0&&point.Y>=0&&point.X+window.StartButton.ActualWidth<=window.Root.ActualWidth+1&&point.Y+window.StartButton.ActualHeight<=window.Root.ActualHeight+1,"Primary action remains fully inside the smallest supported window");await Snapshot(window,Path.Combine(output,"05-small.png"));
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1200*scale),(int)(800*scale)));window.SetPreviewAppearance(new Appearance{DesktopGlass=false});window.ShowPage(0);
        await NetworkFlow(output);
        await Snapshot(window,Path.Combine(output,"06-final-home.png"));
        await File.WriteAllLinesAsync(report,results);
    }
    static async Task WaitFor(Func<bool> predicate,CancellationToken token){while(!predicate())await Task.Delay(40,token);}
    static async Task NetworkFlow(string output)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));var ct=deadline.Token;
        var joiner=new MainWindow(new AppearanceStore(Path.Combine(output,"joiner-profile")),true);var access=new MainWindow(new AppearanceStore(Path.Combine(output,"access-profile")),true);
        joiner.Activate();access.Activate();await Task.WhenAll(joiner.Loaded,access.Loaded);
        var nic=new LanInterface("Loopback test",IPAddress.Loopback,IPAddress.Parse("255.0.0.0"));foreach(var window in new[]{joiner,access}){window.NetworkChoice.Items.Clear();window.NetworkChoice.Items.Add(nic);window.NetworkChoice.SelectedIndex=0;}
        var probe=new TcpListener(IPAddress.IPv6Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();var game=new TcpListener(IPAddress.Loopback,0);game.Start();int gamePort=((IPEndPoint)game.LocalEndpoint).Port;
        try
        {
            joiner.AddressBox.Text="::1";joiner.PortBox.Text=port.ToString();await joiner.StartAsync(true);await WaitFor(()=>joiner.StateTitle.Text=="等待朋友接入",ct);
            Check(joiner.PinBox.Text.Replace("-","").Length==32&&joiner.CopyButton.IsEnabled,"WinUI share action generates a 128-bit PIN and enables copying");
            access.SetRole(false);access.AddressBox.Text="::1";access.PortBox.Text=port.ToString();access.PinBox.Text=joiner.PinBox.Text;await access.StartAsync(true);
            await WaitFor(()=>joiner.StateTitle.Text=="已安全连接"&&access.StateTitle.Text=="已安全连接",ct);Check(true,"Two WinUI window controllers pair through real IPv6 TLS");
            while(joiner.DiscoveredWorlds.Count==0||access.DiscoveredWorlds.Count==0){await Discovery.Announce(nic,"WinUI integration world",gamePort,ct);await Task.Delay(150,ct);}
            Check(joiner.DiscoveredWorlds.Count==1&&access.DiscoveredWorlds.Count==1,"Real Minecraft-format multicast populates both native world lists");
            var mapped=IPEndPoint.Parse(joiner.DiscoveredWorlds.Single().Endpoint);Check(mapped.Port!=gamePort&&mapped.Address.Equals(IPAddress.Loopback),"World entry exposes the actual local tunnel endpoint");
            byte[] payload=RandomNumberGenerator.GetBytes(65536),received=new byte[payload.Length];var echo=Task.Run(async()=>{using var accepted=await game.AcceptTcpClientAsync(ct);var data=new byte[payload.Length];var stream=accepted.GetStream();await stream.ReadExactlyAsync(data,ct);await stream.WriteAsync(data,ct);},ct);
            using(var client=new TcpClient(AddressFamily.InterNetwork)){await client.ConnectAsync(mapped,ct);var stream=client.GetStream();await stream.WriteAsync(payload,ct);await stream.ReadExactlyAsync(received,ct);}await echo;Check(payload.AsSpan().SequenceEqual(received),"WinUI-started tunnel forwards exact game TCP payload bytes");
            await joiner.StopAsync();await access.StopAsync();Check(joiner.PinBox.Text.Length==0&&access.PinBox.Text.Length==0&&!joiner.CopyButton.IsEnabled&&joiner.DiscoveredWorlds.Count==0&&access.DiscoveredWorlds.Count==0,"Stop clears PIN, sharing actions and world lists in both WinUI windows");
            probe=new TcpListener(IPAddress.IPv6Loopback,port);probe.Start();probe.Stop();Check(true,"Stop releases the IPv6 listening port");
        }
        finally{await joiner.StopAsync();await access.StopAsync();game.Stop();joiner.Close();access.Close();}
    }
}
