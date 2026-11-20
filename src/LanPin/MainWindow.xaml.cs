using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LanPin.Desktop;

public sealed class WorldItem
{
    public string Key { get; init; }="";
    public string Title { get; init; }="";
    public string Endpoint { get; init; }="";
    public DateTime Seen { get; set; }
}

public partial class MainWindow : Window
{
    readonly AppearanceStore store;
    readonly DesktopBackdrop backdrop;
    readonly bool preview;
    readonly ObservableCollection<WorldItem> worlds=[];
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    readonly DispatcherTimer toastTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    readonly DispatcherTimer worldTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    Appearance appearance;
    bool ready,joiner=true,advanced,running,stopping,closeApproved,closing,windowClosed;
    Session? session;
    Task? run;
    string? displayImageName;
    int activePage;

    public MainWindow():this(new AppearanceStore(),false){}
    internal MainWindow(AppearanceStore settings,bool previewMode)
    {
        store=settings;preview=previewMode;appearance=store.Load();
        InitializeComponent();backdrop=new DesktopBackdrop(this,ApplyAppearance);WorldList.ItemsSource=worlds;
        if(!preview){Width=Math.Max(MinWidth,Math.Min(Width,SystemParameters.WorkArea.Width-28));Height=Math.Max(MinHeight,Math.Min(Height,SystemParameters.WorkArea.Height-28));}
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();SaveAppearance();};
        toastTimer.Tick+=(_,_)=>{toastTimer.Stop();Toast.Visibility=Visibility.Collapsed;};
        worldTimer.Tick+=(_,_)=>{foreach(var item in worlds.Where(x=>DateTime.UtcNow-x.Seen>TimeSpan.FromSeconds(20)).ToArray())worlds.Remove(item);UpdateWorldCount();};worldTimer.Start();
        BlurSlider.Value=appearance.Blur;ShadeSlider.Value=appearance.Shade;PanelSlider.Value=appearance.PanelOpacity;
        GlassCheck.IsChecked=appearance.DesktopGlass;BlendSlider.Value=appearance.WallpaperBlend;FitCheck.IsChecked=appearance.FitInside;MotionCheck.IsChecked=appearance.ReduceMotion;
        if(appearance.Wallpaper is { } path)
        {
            try {WallpaperImage.Source=AppearanceStore.ReadImage(path);displayImageName="已保存的自定义背景";}
            catch {appearance=appearance with{Wallpaper=null};}
        }
        ApplyAppearance();RefreshNetworks();SetRole(true);ShowPage(0,false);ready=true;
        Closed+=(_,_)=>{windowClosed=true;backdrop.Dispose();saveTimer.Stop();toastTimer.Stop();worldTimer.Stop();};
    }
    void Ui(Action action)
    {
        if(windowClosed||Dispatcher.HasShutdownStarted)return;
        if(Dispatcher.CheckAccess())action();else _=Dispatcher.InvokeAsync(()=>{if(!windowClosed)action();});
    }
    bool Animate => !appearance.ReduceMotion&&SystemParameters.ClientAreaAnimation&&!preview;
    static DoubleAnimationUsingKeyFrames Motion(double from,double to,int milliseconds)
    {
        var value=new DoubleAnimationUsingKeyFrames();value.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        value.KeyFrames.Add(new SplineDoubleKeyFrame(to,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),new KeySpline(0,0,0,1)));return value;
    }
    internal void ShowPage(int page,bool animate=true)
    {
        activePage=page;ConnectPage.Visibility=page==0?Visibility.Visible:Visibility.Collapsed;AppearancePage.Visibility=page==1?Visibility.Visible:Visibility.Collapsed;GuidePage.Visibility=page==2?Visibility.Visible:Visibility.Collapsed;
        foreach(var (button,index) in new[]{(ConnectTab,0),(AppearanceTab,1),(GuideTab,2)})
        {button.Background=new SolidColorBrush(Color.FromArgb((byte)(index==page?35:0),210,238,216));button.FontWeight=index==page?FontWeights.Bold:FontWeights.Normal;}
        var transform=(TranslateTransform)PageHost.RenderTransform;
        transform.BeginAnimation(TranslateTransform.YProperty,null);PageHost.BeginAnimation(OpacityProperty,null);transform.Y=0;PageHost.Opacity=1;
        if(animate&&Animate){transform.BeginAnimation(TranslateTransform.YProperty,Motion(9,0,250));PageHost.BeginAnimation(OpacityProperty,new DoubleAnimation(.72,1,TimeSpan.FromMilliseconds(167)));}
    }
    void ConnectTabClick(object sender,RoutedEventArgs e)=>ShowPage(0);
    void AppearanceTabClick(object sender,RoutedEventArgs e)=>ShowPage(1);
    void GuideTabClick(object sender,RoutedEventArgs e)=>ShowPage(2);
    void TitleBarMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source&&FindParent<Button>(source)!=null)return;
        if(e.ChangedButton!=MouseButton.Left)return;
        if(e.ClickCount==2){ToggleMaximize();return;}
        try {DragMove();}catch(InvalidOperationException){}
    }
    static T? FindParent<T>(DependencyObject? item) where T:DependencyObject
    {while(item!=null){if(item is T target)return target;item=item is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);}return null;}
    void TitleBarRightClick(object sender,MouseButtonEventArgs e)=>SystemCommands.ShowSystemMenu(this,PointToScreen(e.GetPosition(this)));
    void MinimizeClick(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    void MaximizeClick(object sender,RoutedEventArgs e)=>ToggleMaximize();
    void ToggleMaximize(){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    void CloseClick(object sender,RoutedEventArgs e)=>Close();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(e.Property==WindowStateProperty&&MaximizeButton!=null)WindowStateChanged(this,EventArgs.Empty);
    }
    void WindowStateChanged(object? sender,EventArgs e)
    {if(MaximizeButton!=null){MaximizeButton.Content=WindowState==WindowState.Maximized?"\uE923":"\uE922";WindowFrame.CornerRadius=new CornerRadius(WindowState==WindowState.Maximized?0:18);UpdateSurfaceClip();}}

    void SurfaceSizeChanged(object sender,SizeChangedEventArgs e)=>UpdateSurfaceClip();
    void UpdateSurfaceClip()
    {
        if(SurfaceClip==null||SurfaceRoot==null)return;
        SurfaceClip.Rect=new Rect(0,0,Math.Max(0,SurfaceRoot.ActualWidth),Math.Max(0,SurfaceRoot.ActualHeight));
        SurfaceClip.RadiusX=SurfaceClip.RadiusY=WindowState==WindowState.Maximized?0:17;
    }
    internal void SetRole(bool isJoiner)
    {
        if(running)return;joiner=isJoiner;
        JoinerRoleBorder.BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(joiner?"#A7DDB5":"#38C4DAC6"));
        AccessRoleBorder.BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(!joiner?"#A7DDB5":"#38C4DAC6"));
        JoinerRoleBorder.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(joiner?"#35487E56":"#1225381F"));
        AccessRoleBorder.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(!joiner?"#35487E56":"#1225381F"));
        JoinerCheck.Visibility=joiner?Visibility.Visible:Visibility.Hidden;AccessCheck.Visibility=joiner?Visibility.Hidden:Visibility.Visible;
        PinBox.Text="";PinBox.IsReadOnly=joiner;PinBox.Tag=joiner?"开始分享后生成一次性 PIN":"粘贴加入方给出的完整 PIN";
        AddressBox.Text=joiner?FindIpv6():"";AddressBox.Tag=joiner?"自动检测，也可手动填写":"输入加入方给出的 IPv6 地址";
        DetectButton.Visibility=joiner?Visibility.Visible:Visibility.Collapsed;CopyButton.Visibility=joiner?Visibility.Visible:Visibility.Collapsed;CopyButton.IsEnabled=false;
        ManualBox.IsEnabled=!joiner;StartButton.Content=joiner?"生成分享信息  ↗":"连接加入方  ↗";
        RoleHelp.Text=joiner?"将 IPv6、端口和 PIN 私下发送给存档接入方。双方 IPv6 需互通。":"填写加入方的信息；实际存档电脑需对局域网开放并保持运行。";
        worlds.Clear();UpdateWorldCount();SetState("准备就绪",joiner?"分享信息，等待朋友接入":"连接后发现本机或内网存档","未连接",false);
    }
    void JoinerRoleClick(object sender,RoutedEventArgs e)=>SetRole(true);
    void AccessRoleClick(object sender,RoutedEventArgs e)=>SetRole(false);
    void RefreshNetworks()
    {
        if(running)return;
        string? previous=(NetworkChoice.SelectedItem as LanInterface)?.Address.ToString();NetworkChoice.Items.Clear();
        if(preview){NetworkChoice.Items.Add(new LanInterface("以太网",IPAddress.Parse("192.168.10.8"),IPAddress.Parse("255.255.255.0")));}
        else
        {
            var preferred=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up&&x.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork)).SelectMany(x=>x.GetIPProperties().UnicastAddresses).Select(x=>x.Address.ToString()).ToHashSet();
            foreach(var nic in LanInterface.All().OrderByDescending(x=>preferred.Contains(x.Address.ToString())))NetworkChoice.Items.Add(nic);
        }
        NetworkChoice.SelectedItem=NetworkChoice.Items.Cast<LanInterface>().FirstOrDefault(x=>x.Address.ToString()==previous);
        if(NetworkChoice.SelectedIndex<0&&NetworkChoice.Items.Count>0)NetworkChoice.SelectedIndex=0;
        if(NetworkChoice.Items.Count==0)ToastMessage("没有找到可用的私有 IPv4 网卡，请先连接本地网络。");
    }
    string FindIpv6()
    {
        if(preview)return "";
        var local=(NetworkChoice.SelectedItem as LanInterface)?.Address;
        var nics=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up).OrderByDescending(x=>local!=null&&x.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(local)));
        return nics.SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(x=>x.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetworkV6&&!a.IsIPv6LinkLocal&&!a.IsIPv6Multicast&&!IPAddress.IsLoopback(a)&&(a.GetAddressBytes()[0]&0xe0)==0x20)?.ToString()??"";
    }
    void RefreshNetworkClick(object sender,RoutedEventArgs e){RefreshNetworks();if(joiner)AddressBox.Text=FindIpv6();}
    void DetectIpv6Click(object sender,RoutedEventArgs e){AddressBox.Text=FindIpv6();ToastMessage(AddressBox.Text.Length==0?"未检测到公网 IPv6，可手动填写能被对方访问的 IPv6 地址。":"已更新 IPv6 候选地址，请确认对方可达。");}
    internal void SetAdvanced(bool value,bool animate=true)
    {
        advanced=value;AdvancedToggle.Content=value?"收起网络选项  −":"网络选项  +";double target=value?94:0;
        double current=AdvancedHost.ActualHeight;AdvancedHost.BeginAnimation(HeightProperty,null);AdvancedHost.BeginAnimation(OpacityProperty,null);
        if(animate&&Animate){AdvancedHost.BeginAnimation(HeightProperty,Motion(current,target,250));AdvancedHost.BeginAnimation(OpacityProperty,new DoubleAnimation(value?1:0,TimeSpan.FromMilliseconds(167)));}
        else {AdvancedHost.Height=target;AdvancedHost.Opacity=value?1:0;}
    }
    void AdvancedClick(object sender,RoutedEventArgs e)=>SetAdvanced(!advanced);
    async void StartClick(object sender,RoutedEventArgs e)=>await StartAsync();
    internal async Task StartAsync(bool allowPreviewNetworking=false)
    {
        if(running||(preview&&!allowPreviewNetworking))return;
        try
        {
            if(NetworkChoice.SelectedItem is not LanInterface nic)throw new ArgumentException("请选择游戏所在的内网接口。");
            string address=AddressBox.Text.Trim().Trim('[',']');
            if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetworkV6||ip.Equals(IPAddress.IPv6Any)||ip.IsIPv6Multicast)
                throw new ArgumentException("请输入可供对方连接的加入方 IPv6 地址；不要填写端口。");
            if(!int.TryParse(PortBox.Text,out int port)||port is <1024 or >65535)throw new ArgumentException("TCP 端口应在 1024 至 65535 之间。");
            Game? manual=null;
            if(!joiner&&!string.IsNullOrWhiteSpace(ManualBox.Text))
            {
                if(!IPEndPoint.TryParse(ManualBox.Text.Trim(),out var ep)||ep.AddressFamily!=AddressFamily.InterNetwork||ep.Port<1||(!nic.Contains(ep.Address)&&!IPAddress.IsLoopback(ep.Address)))
                    throw new ArgumentException("游戏地址应位于所选内网或本机，例如 127.0.0.1:5222。");
                manual=new(ep.ToString(),"手动指定的 Java 存档",ep.Address.ToString(),ep.Port);
            }
            string pin=joiner?Protocol.NewPin():PinBox.Text;Protocol.Pin(pin);PinBox.Text=pin;
            LogBox.Clear();worlds.Clear();UpdateWorldCount();running=true;stopping=false;SetBusy(true);
            SetState(joiner?"正在生成分享":"正在连接",joiner?"准备 IPv6 监听端口":"验证分享信息与加密连接",joiner?"准备分享":"连接中",false);
            var current=new Session(OnLog,_=>{},OnWorld);session=current;bool share=joiner;
            run=Task.Run(async()=>
            {
                try {if(share)await current.Share(pin,port,nic);else await current.Join(address,pin,port,nic,manual);}
                catch(Exception ex){if(!stopping)Ui(()=>{OnLog("连接结束："+ex.Message);ToastMessage(ex is OperationCanceledException?"连接已超时，请重新分享或连接。":ex.Message);});}
                finally
                {
                    await current.DisposeAsync();
                    if(!windowClosed&&!Dispatcher.HasShutdownStarted)
                        await Dispatcher.InvokeAsync(()=>{if(session==current){session=null;running=false;stopping=false;SetBusy(false);PinBox.Text="";CopyButton.IsEnabled=false;worlds.Clear();UpdateWorldCount();SetState("连接已结束","可重新生成分享信息或连接朋友","未连接",false);}});
                }
            });
            await Task.Yield();
        }
        catch(Exception ex){ToastMessage(ex.Message);}
    }
    void SetBusy(bool busy)
    {
        JoinerRoleButton.IsEnabled=AccessRoleButton.IsEnabled=NetworkChoice.IsEnabled=RefreshNetworkButton.IsEnabled=PortBox.IsEnabled=StartButton.IsEnabled=DetectButton.IsEnabled=!busy;
        AddressBox.IsReadOnly=busy;PinBox.IsReadOnly=busy||joiner;ManualBox.IsEnabled=!busy&&!joiner;
        StopButton.Visibility=busy?Visibility.Visible:Visibility.Collapsed;StopButton.IsEnabled=busy;
        CopyButton.IsEnabled=busy&&joiner&&PinBox.Text.Length>0;
    }
    async void StopClick(object sender,RoutedEventArgs e)=>await StopAsync();
    internal async Task StopAsync()
    {
        if(!running)return;stopping=true;StopButton.IsEnabled=false;SetState("正在断开","释放当前会话与游戏连接","断开中",false);session?.RequestStop();
        if(run!=null)await run;
    }
    void OnLog(string message)=>Ui(()=>
    {
        if(LogBox.Text.Length>60000)LogBox.Clear();LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");LogBox.ScrollToEnd();LastActivity.Text=message;
        if(message.StartsWith("等待存档接入方"))SetState("等待朋友接入","复制分享信息，私下发给存档接入方","等待配对",false);
        else if(message.StartsWith("PIN 验证成功"))SetState("已安全连接",joiner?"等待远端存档公告":"请让存档电脑对局域网开放","已连接",true);
    });
    void SetState(string title,string hint,string compact,bool connected)
    {StateTitle.Text=title;StateHint.Text=hint;TopStatusText.Text=compact;TopStatusDot.Fill=new SolidColorBrush((Color)ColorConverter.ConvertFromString(connected?"#ADEAC0":"#95AB9B"));StateGlyph.Text=connected?"✓":"⌁";}
    void OnWorld(WorldNotice notice)=>Ui(()=>
    {
        var previous=worlds.FirstOrDefault(x=>x.Key==notice.Key);
        if(!notice.Available){if(previous!=null)worlds.Remove(previous);}
        else if(previous!=null)previous.Seen=DateTime.UtcNow;
        else worlds.Add(new(){Key=notice.Key,Title=notice.Title,Endpoint=$"{notice.Host}:{notice.Port}",Seen=DateTime.UtcNow});
        UpdateWorldCount();
    });
    void UpdateWorldCount(){WorldCount.Text=worlds.Count.ToString();EmptyWorlds.Visibility=worlds.Count==0?Visibility.Visible:Visibility.Collapsed;}
    void CopyShareClick(object sender,RoutedEventArgs e)
    {
        if(!running||!joiner||PinBox.Text.Length==0)return;
        Copy($"IPv6: {AddressBox.Text}\n端口: {PortBox.Text}\nPIN: {PinBox.Text}","已复制分享信息，请私下发送给存档接入方。");
    }
    void CopyWorldClick(object sender,RoutedEventArgs e){if(sender is Button {Tag:string endpoint})Copy(endpoint,"已复制备用地址，可粘贴到 Minecraft 直接连接。");}
    void Copy(string text,string message){try{Clipboard.SetText(text);ToastMessage(message);}catch{ToastMessage("剪贴板正被其他程序占用，请稍后重试。");}}
    void ShowLogsClick(object sender,RoutedEventArgs e)=>LogsOverlay.Visibility=Visibility.Visible;
    void HideLogsClick(object sender,RoutedEventArgs e)=>LogsOverlay.Visibility=Visibility.Collapsed;
    void ToastMessage(string text){if(windowClosed)return;ToastText.Text=text;Toast.Visibility=Visibility.Visible;toastTimer.Stop();toastTimer.Start();}

    internal Appearance CurrentAppearance=>appearance;
    internal IReadOnlyList<WorldItem> DiscoveredWorlds=>worlds.ToArray();
    void ApplyAppearance()
    {
        var (a,b,glow)=appearance.Palette switch
        {"Dusk"=>("#6E5249","#282E2D","#B78D65"),"Slate"=>("#43586A","#1C2C3D","#679B9F"),_=>("#213B32","#10251E","#7E604E")};
        BaseA.Color=(Color)ColorConverter.ConvertFromString(a);BaseB.Color=(Color)ColorConverter.ConvertFromString(b);var glowColor=(Color)ColorConverter.ConvertFromString(glow);glowColor.A=114;GlowStop.Color=glowColor;
        var tint=(Color)ColorConverter.ConvertFromString(b);tint.A=76;
        backdrop.Apply(appearance.DesktopGlass,tint);
        ApplyMaterialLayers(backdrop.Active);
        MaterialStatus.Text=backdrop.Status;
        BlendSlider.IsEnabled=appearance.DesktopGlass;
        BlendValue.Text=$"{appearance.WallpaperBlend:P0}";
        WallpaperBlur.Radius=appearance.Blur;WallpaperImage.Stretch=appearance.FitInside?Stretch.Uniform:Stretch.UniformToFill;
        Resources["Glass"]=new SolidColorBrush(Color.FromArgb((byte)Math.Round(appearance.PanelOpacity*255),24,48,39));
        WallpaperName.Text=appearance.Wallpaper==null?"当前：默认渐变":"当前："+(displayImageName??"已保存的自定义背景");
        BlurValue.Text=$"{appearance.Blur:0}";ShadeValue.Text=$"{appearance.Shade:P0}";PanelValue.Text=$"{appearance.PanelOpacity:P0}";
    }
    internal bool NativeGlassActive=>backdrop.Active;
    internal DesktopBackdrop NativeBackdrop=>backdrop;
    internal void ApplyMaterialLayers(bool active)
    {
        Background=active?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(16,37,29));
        DefaultBackdrop.Opacity=active?.10:1;
        ShadeLayer.Opacity=active?appearance.Shade*.3:appearance.Shade;
        WallpaperImage.Opacity=active?appearance.WallpaperBlend:1;
    }
    void ReadAppearanceControls()
    {appearance=appearance with{DesktopGlass=GlassCheck.IsChecked==true,WallpaperBlend=BlendSlider.Value,Blur=BlurSlider.Value,Shade=ShadeSlider.Value,PanelOpacity=PanelSlider.Value,FitInside=FitCheck.IsChecked==true,ReduceMotion=MotionCheck.IsChecked==true};ApplyAppearance();saveTimer.Stop();saveTimer.Start();}
    void AppearanceSliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(ready)ReadAppearanceControls();}
    void AppearanceCheckChanged(object sender,RoutedEventArgs e){if(ready)ReadAppearanceControls();}
    void SaveAppearance(){try{store.Save(appearance);}catch{ToastMessage("外观设置暂时无法保存，请检查本机数据目录的访问权限。");}}
    internal async Task ImportWallpaperAsync(string path)
    {
        try
        {
            var imported=await Task.Run(()=>store.Import(path));
            appearance=appearance with{Wallpaper=imported.Path};WallpaperImage.Source=imported.Image;displayImageName=Path.GetFileName(path);ApplyAppearance();SaveAppearance();
            if(!preview)ToastMessage("背景已保存在本机。");
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or NotSupportedException or System.IO.FileFormatException or UnauthorizedAccessException)
        {if(preview)throw;ToastMessage("无法使用这张图片：请选择有效的 PNG、JPG 或 BMP（不超过 24 MB）。");}
    }
    async void BrowseWallpaperClick(object sender,RoutedEventArgs e)
    {var dialog=new OpenFileDialog{Title="选择 LanPin 背景",Filter="背景图片|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true};if(dialog.ShowDialog(this)==true)await ImportWallpaperAsync(dialog.FileName);}
    internal void ResetWallpaper(){appearance=appearance with{Wallpaper=null};WallpaperImage.Source=null;displayImageName=null;ApplyAppearance();SaveAppearance();}
    void ResetBackgroundClick(object sender,RoutedEventArgs e){ResetWallpaper();ToastMessage("已恢复默认渐变背景，原始图片未改动。");}
    void PaletteClick(object sender,RoutedEventArgs e){if(sender is Button {Tag:string palette}){appearance=appearance with{Palette=palette};ApplyAppearance();SaveAppearance();if(appearance.Wallpaper!=null)ToastMessage("已设置默认渐变；恢复默认背景后可见。");}}
    void WindowDragOver(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    async void WindowDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.Length==1)await ImportWallpaperAsync(files[0]);e.Handled=true;}
    void OpenProjectClick(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("https://github.com/hongmingniu/-P2P-MC-JAVA-"){UseShellExecute=true});}catch{ToastMessage("无法打开浏览器，请手动访问项目仓库。");}}
    async void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(closeApproved)return;
        if(!running){SaveAppearance();return;}
        e.Cancel=true;if(closing)return;closing=true;await StopAsync();SaveAppearance();closeApproved=true;
        _=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!windowClosed)Close();}));
    }
    internal void SetPreviewAppearance(Appearance value)
    {
        bool wasReady=ready;ready=false;
        appearance=value.Validated();BlurSlider.Value=appearance.Blur;ShadeSlider.Value=appearance.Shade;PanelSlider.Value=appearance.PanelOpacity;GlassCheck.IsChecked=appearance.DesktopGlass;BlendSlider.Value=appearance.WallpaperBlend;FitCheck.IsChecked=appearance.FitInside;MotionCheck.IsChecked=appearance.ReduceMotion;
        ready=wasReady;ApplyAppearance();SaveAppearance();
    }
}
