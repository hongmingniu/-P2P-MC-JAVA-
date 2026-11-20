using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;

namespace LanPin.Desktop;
public sealed record WorldItem(string Key,string Title,string Endpoint){public DateTime Seen{get;set;}=DateTime.UtcNow;}
public sealed class MainWindow:Window
{
    internal readonly Grid Root=new(){RequestedTheme=ElementTheme.Light};
    internal readonly TextBox AddressBox=new(){Header="加入方 IPv6",PlaceholderText="输入加入方 IPv6，不含端口"};
    internal readonly TextBox PinBox=new(){Header="分享 PIN",PlaceholderText="开始后生成一次性 PIN",IsReadOnly=true};
    internal readonly TextBox PortBox=new(){Header="TCP 端口",Text="38443"};
    internal readonly TextBox ManualBox=new(){Header="手动游戏地址（可选）",PlaceholderText="127.0.0.1:5222",IsEnabled=false};
    internal readonly ComboBox NetworkChoice=new(){Header="本机网络",HorizontalAlignment=HorizontalAlignment.Stretch};
    internal readonly RadioButtons RoleChoice=new(){Header="你的角色",MaxColumns=2};
    internal readonly Button StartButton=new(){Content="生成分享信息  ↗",HorizontalAlignment=HorizontalAlignment.Stretch};
    internal readonly Button CopyButton=new(){Content="复制分享信息",IsEnabled=false};
    internal readonly Button StopButton=new(){Content="断开",Visibility=Visibility.Collapsed};
    internal readonly Button HomeButton=new(){Content="开始联机  ↗",MinWidth=172};
    internal readonly Button HomeStop=new(){Content="断开",Visibility=Visibility.Collapsed};
    internal readonly Button WorldsButton=new(){Content="查看存档  0  ↗",Visibility=Visibility.Collapsed};
    internal readonly Button DetectButton=new(){Content="检测 IPv6"};
    internal readonly Button RefreshButton=new(){Content="刷新网卡"};
    internal readonly TextBlock StateTitle=Text("准备就绪",22),StateHint=Text("分享信息，等待朋友接入",12),TopStatus=Text("未连接",11);
    internal readonly TextBlock HomeRole=Text("加入朋友的世界",12),RoleHelp=Text("",11),MaterialStatus=Text("",12),WallpaperName=Text("默认背景",12);
    internal readonly SplitView Drawer=new(){DisplayMode=SplitViewDisplayMode.Overlay,PanePlacement=SplitViewPanePlacement.Right,OpenPaneLength=520,CompactPaneLength=0,IsPaneOpen=false,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),PaneBackground=Brush("#F0EBE5")};
    internal readonly Grid ConnectionPane=new(),WorldPane=new(){Visibility=Visibility.Collapsed};
    internal readonly StackPanel WorldRows=new(){Spacing=14};
    internal readonly TextBlock EmptyWorlds=Text("等待局域网存档\n请让存档电脑对局域网开放。",15);
    internal readonly Image WallpaperImage=new(){Stretch=Stretch.UniformToFill};
    internal readonly ToggleSwitch GlassToggle=new(){Header="桌面毛玻璃",OnContent="开启",OffContent="关闭"};
    internal readonly CheckBox FitCheck=new(){Content="完整显示背景图片"},MotionCheck=new(){Content="减少动效"};
    internal readonly Slider BlurSlider=new(){Header="背景图模糊",Minimum=0,Maximum=24,StepFrequency=1};
    internal readonly Slider ShadeSlider=new(){Header="磨砂遮罩",Minimum=.2,Maximum=.8,StepFrequency=.01};
    internal readonly Slider BlendSlider=new(){Header="毛玻璃中的背景图混合",Minimum=0,Maximum=.8,StepFrequency=.01};
    internal readonly Slider PanelSlider=new(){Header="抽屉不透明度",Minimum=.4,Maximum=.95,StepFrequency=.01};
    internal readonly ComboBox PaletteChoice=new(){Header="默认色调",HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly Grid pages=new(),home=new(),settings=new(),guide=new();
    readonly Border baseLayer=new(),veil=new(),homeVeil=new();
    readonly AcrylicBrush paneBrush=new(){TintColor=Color.FromArgb(255,240,235,229),FallbackColor=Color.FromArgb(255,240,235,229)};
    readonly TextBox logs=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    readonly Border logOverlay=new(){Visibility=Visibility.Collapsed,Background=Brush("#F5EFE9"),Padding=new Thickness(28),Margin=new Thickness(20),CornerRadius=new CornerRadius(14)};
    readonly InfoBar toast=new(){IsOpen=false,IsClosable=true,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=700,Margin=new Thickness(24,0,24,86)};
    readonly List<WorldItem> worlds=[];readonly Dictionary<string,UIElement> rows=[];
    readonly AppearanceStore store;Appearance appearance;PhotoData? photo;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer,blurTimer,worldTimer;
    readonly TaskCompletionSource loaded=new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool ready,closed,running,stopping,closing,allowClose,joiner=true;bool? backdrop;
    int wallpaperGeneration;
    Session? session;Task? run;
    internal bool Preview{get;}
    internal Appearance CurrentAppearance=>appearance;
    internal IReadOnlyList<WorldItem> DiscoveredWorlds=>worlds.ToArray();
    internal Task Loaded=>loaded.Task;
    internal NativeFrame Frame{get;}
    public MainWindow():this(new AppearanceStore(),false){}
    internal MainWindow(AppearanceStore store,bool preview)
    {
        this.store=store;Preview=preview;appearance=store.Load();Title="LanPin · WinUI 3";
        saveTimer=DispatcherQueue.CreateTimer();saveTimer.Interval=TimeSpan.FromMilliseconds(400);saveTimer.IsRepeating=false;saveTimer.Tick+=(_,_)=>SaveAppearance();
        blurTimer=DispatcherQueue.CreateTimer();blurTimer.Interval=TimeSpan.FromMilliseconds(200);blurTimer.IsRepeating=false;blurTimer.Tick+=async(_,_)=>await RefreshWallpaperAsync();
        worldTimer=DispatcherQueue.CreateTimer();worldTimer.Interval=TimeSpan.FromSeconds(3);worldTimer.Tick+=(_,_)=>{foreach(var old in worlds.Where(x=>DateTime.UtcNow-x.Seen>TimeSpan.FromSeconds(20)).ToArray())RemoveWorld(old);};
        foreach(var key in new[]{"AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush"})Root.Resources[key]=Brush("#AF633E");
        Root.Resources["AccentTextFillColorPrimaryBrush"]=Brush("#9B512F");
        BuildView();Content=Root;
        ExtendsContentIntoTitleBar=true;SetTitleBar(titleRegion);
        AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Tall;
        double scale=NativeFrame.Scale(this);var work=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
        int width=Math.Min((int)(1200*scale),work.Width-24),height=Math.Min((int)(800*scale),work.Height-24);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X+(work.Width-width)/2,work.Y+(work.Height-height)/2,width,height));
        if(AppWindow.Presenter is OverlappedPresenter presenter){presenter.PreferredMinimumWidth=Math.Min((int)(980*scale),work.Width-24);presenter.PreferredMinimumHeight=Math.Min((int)(680*scale),work.Height-24);}
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","LanPin.ico"));
        Frame=new NativeFrame(this,Root);
        AppWindow.Closing+=async(_,e)=>
        {
            if(allowClose)return;
            if(!running){SaveAppearance();return;}
            e.Cancel=true;if(closing)return;closing=true;await StopAsync();SaveAppearance();allowClose=true;DispatcherQueue.TryEnqueue(Close);
        };
        Closed+=(_,_)=>{closed=true;wallpaperGeneration++;worldTimer.Stop();saveTimer.Stop();blurTimer.Stop();Frame.Dispose();};
        Root.Loaded+=async(_,_)=>
        {
            try{if(appearance.Wallpaper is { } path){photo=await PhotoData.ReadAsync(path);await RefreshWallpaperAsync();}}
            catch{appearance=appearance with{Wallpaper=null};Notify("已保存的背景无法读取，已恢复默认背景。");}
            finally{loaded.TrySetResult();}
        };
        SetAppearanceControls();ApplyAppearance();RefreshNetworks();SetRole(true);ShowPage(0);ready=true;worldTimer.Start();
    }
    readonly Grid titleRegion=new(){Height=60,Margin=new Thickness(44,0,150,0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
    static readonly Color Ink=Color.FromArgb(255,44,39,35),Muted=Color.FromArgb(255,104,97,90);
    static SolidColorBrush Brush(string hex)
    {hex=hex.TrimStart('#');uint n=Convert.ToUInt32(hex,16);return new(Color.FromArgb(hex.Length==8?(byte)(n>>24):(byte)255,(byte)(n>>16),(byte)(n>>8),(byte)n));}
    internal static TextBlock Text(string text,double size=13)=>new(){Text=text,FontSize=size,Foreground=new SolidColorBrush(Ink),TextWrapping=TextWrapping.Wrap};
    static StackPanel Stack(params UIElement[] elements){var s=new StackPanel{Spacing=14};foreach(var e in elements)s.Children.Add(e);return s;}
    static Button Action(string label,RoutedEventHandler click,bool primary=false)
    {var b=new Button{Content=label,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new Thickness(0)};if(primary){b.Style=(Style)Application.Current.Resources["AccentButtonStyle"];b.Background=Brush("#AF633E");b.Foreground=Brush("#FFF9F4");}b.Click+=click;return b;}
    static void Row(Grid grid,FrameworkElement item,int row){Grid.SetRow(item,row);grid.Children.Add(item);}
    static Grid Rows(params GridLength[] lengths){var g=new Grid();foreach(var length in lengths)g.RowDefinitions.Add(new(){Height=length});return g;}
    static GridLength Star=>new(1,GridUnitType.Star);
    void BuildView()
    {
        Root.Children.Add(baseLayer);Root.Children.Add(WallpaperImage);Root.Children.Add(veil);Root.Children.Add(Drawer);Root.Children.Add(logOverlay);Root.Children.Add(toast);
        var content=Rows(new GridLength(60),Star,new GridLength(80));Drawer.Content=content;
        titleRegion.Children.Add(Text("LanPin",21));titleRegion.Children[0].SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        TopStatus.HorizontalAlignment=HorizontalAlignment.Right;TopStatus.VerticalAlignment=VerticalAlignment.Center;TopStatus.Foreground=new SolidColorBrush(Muted);titleRegion.Children.Add(TopStatus);Row(content,titleRegion,0);
        pages.Margin=new Thickness(44,12,44,0);pages.Children.Add(home);pages.Children.Add(settings);pages.Children.Add(guide);Row(content,pages,1);
        home.ColumnDefinitions.Add(new(){Width=Star});home.ColumnDefinitions.Add(new(){Width=new GridLength(340)});
        homeVeil.Background=new LinearGradientBrush{StartPoint=new Point(0,.35),EndPoint=new Point(0,1),GradientStops={new(){Color=Color.FromArgb(0,238,229,221),Offset=0},new(){Color=Color.FromArgb(225,238,229,221),Offset=1}}};homeVeil.Margin=new Thickness(-44,-12,-44,-80);Grid.SetColumnSpan(homeVeil,2);home.Children.Add(homeVeil);
        var hero=Stack(Text("MINECRAFT JAVA / P2P",11),Text("一起玩。",84),Text("把朋友的世界，连到一起。",15));hero.Spacing=5;hero.VerticalAlignment=VerticalAlignment.Bottom;hero.Margin=new Thickness(0,0,20,36);home.Children.Add(hero);
        HomeButton.MinHeight=44;HomeButton.Padding=new Thickness(22,10,22,10);HomeButton.Style=(Style)Application.Current.Resources["AccentButtonStyle"];HomeButton.Background=Brush("#AF633E");HomeButton.Foreground=Brush("#FFF9F4");HomeButton.Click+=(_,_)=>OpenConnectionDrawer();HomeStop.Click+=async(_,_)=>await StopAsync();WorldsButton.Click+=(_,_)=>OpenWorlds();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};actions.Children.Add(HomeButton);actions.Children.Add(HomeStop);
        var state=Stack(StateTitle,StateHint,HomeRole,actions,WorldsButton);state.VerticalAlignment=VerticalAlignment.Bottom;state.Margin=new Thickness(0,0,0,36);state.Width=310;state.HorizontalAlignment=HorizontalAlignment.Right;HomeRole.Margin=new Thickness(0,10,0,0);Grid.SetColumn(state,1);home.Children.Add(state);
        var footer=new Grid{Margin=new Thickness(25,0,44,0)};var tabs=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4,VerticalAlignment=VerticalAlignment.Center};
        string[] labels=["联机","外观","指南"];for(int i=0;i<labels.Length;i++){int page=i;tabs.Children.Add(Action(labels[i],(_,_)=>ShowPage(page)));}footer.Children.Add(tabs);
        var version=Text("WINUI 3 / 0.4.0",10);version.HorizontalAlignment=HorizontalAlignment.Right;version.VerticalAlignment=VerticalAlignment.Center;version.Foreground=new SolidColorBrush(Muted);footer.Children.Add(version);Row(content,footer,2);
        BuildSettings();BuildGuide();BuildDrawer();
        var logGrid=Rows(GridLength.Auto,Star,GridLength.Auto);Row(logGrid,Action("关闭日志",(_,_)=>logOverlay.Visibility=Visibility.Collapsed),0);logs.Margin=new Thickness(0,18,0,18);Row(logGrid,logs,1);Row(logGrid,Text("日志仅保留在内存，分享前请检查地址等个人信息。",11),2);logOverlay.Child=logGrid;
        Root.AllowDrop=true;Root.DragOver+=(_,e)=>{if(e.DataView.Contains(StandardDataFormats.StorageItems))e.AcceptedOperation=DataPackageOperation.Copy;};
        Root.Drop+=async(_,e)=>{if(!e.DataView.Contains(StandardDataFormats.StorageItems))return;var items=await e.DataView.GetStorageItemsAsync();if(items.Count==1&&items[0] is Windows.Storage.StorageFile file)await ImportWallpaperAsync(file.Path);};
        Root.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape){if(logOverlay.Visibility==Visibility.Visible)logOverlay.Visibility=Visibility.Collapsed;else Drawer.IsPaneOpen=false;}};
    }
    void BuildDrawer()
    {
        var pane=new Grid{Margin=new Thickness(28,64,28,24)};pane.Children.Add(ConnectionPane);pane.Children.Add(WorldPane);Drawer.Pane=pane;
        ConnectionPane.RowDefinitions.Add(new(){Height=GridLength.Auto});ConnectionPane.RowDefinitions.Add(new(){Height=Star});ConnectionPane.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new Grid();heading.Children.Add(Text("开始一次联机",23));var close=Action("关闭",(_,_)=>Drawer.IsPaneOpen=false);close.HorizontalAlignment=HorizontalAlignment.Right;heading.Children.Add(close);Row(ConnectionPane,heading,0);
        RoleChoice.Items.Add("加入方");RoleChoice.Items.Add("存档接入方");RoleChoice.SelectionChanged+=(_,_)=>{if(ready&&!running)SetRole(RoleChoice.SelectedIndex==0);};
        DetectButton.Click+=(_,_)=>{AddressBox.Text=FindIpv6();if(AddressBox.Text.Length==0)Notify("未检测到公网 IPv6，可手动填写可达地址。");};CopyButton.Click+=(_,_)=>CopyShare();RefreshButton.Click+=(_,_)=>RefreshNetworks();
        var advanced=Stack(NetworkChoice,RefreshButton,PortBox,ManualBox);advanced.Spacing=12;
        var expander=new Expander{Header="网络选项",Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch};
        var fields=Stack(RoleChoice,AddressBox,DetectButton,PinBox,CopyButton,expander);fields.Margin=new Thickness(0,20,0,16);
        Row(ConnectionPane,new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},1);
        StartButton.MinHeight=44;StartButton.Style=(Style)Application.Current.Resources["AccentButtonStyle"];StartButton.Background=Brush("#AF633E");StartButton.Foreground=Brush("#FFF9F4");StartButton.Click+=async(_,_)=>await StartAsync();StopButton.Click+=async(_,_)=>await StopAsync();
        var actions=Stack(RoleHelp,StartButton,StopButton);actions.Margin=new Thickness(0,16,0,0);Row(ConnectionPane,actions,2);
        WorldPane.RowDefinitions.Add(new(){Height=GridLength.Auto});WorldPane.RowDefinitions.Add(new(){Height=Star});WorldPane.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var worldHead=new Grid();worldHead.Children.Add(Text("发现的存档",23));var wc=Action("关闭",(_,_)=>Drawer.IsPaneOpen=false);wc.HorizontalAlignment=HorizontalAlignment.Right;worldHead.Children.Add(wc);Row(WorldPane,worldHead,0);
        var results=new Grid{Margin=new Thickness(0,24,0,18)};EmptyWorlds.VerticalAlignment=VerticalAlignment.Center;results.Children.Add(EmptyWorlds);results.Children.Add(new ScrollViewer{Content=WorldRows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Row(WorldPane,results,1);
        Row(WorldPane,Stack(Text("在 Minecraft 多人游戏中寻找〈LanPin〉前缀。",12),Action("查看日志",(_,_)=>logOverlay.Visibility=Visibility.Visible)),2);
    }
    void BuildSettings()
    {
        PaletteChoice.Items.Add("暖雾");PaletteChoice.Items.Add("暖沙");PaletteChoice.Items.Add("雾蓝");
        var choose=Action("选择背景图片",async(_,_)=>{var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));foreach(var type in new[]{".png",".jpg",".jpeg",".bmp"})picker.FileTypeFilter.Add(type);var file=await picker.PickSingleFileAsync();if(file!=null)await ImportWallpaperAsync(file.Path);});
        var reset=Action("恢复默认背景",(_,_)=>ResetWallpaper());
        var options=Stack(Text("让这里，更像你。",32),Text("一整层磨砂，留给你喜欢的风景。",13),GlassToggle,MaterialStatus,choose,reset,WallpaperName,PaletteChoice,BlendSlider,BlurSlider,ShadeSlider,PanelSlider,FitCheck,MotionCheck,Text("本应用使用 HarmonyOS Sans 字体。\nCopyright 2021 Huawei Device Co., Ltd.\n字体许可见 Licenses 目录。",11));
        options.Width=540;options.MaxWidth=580;options.HorizontalAlignment=HorizontalAlignment.Left;options.Margin=new Thickness(0,12,0,28);settings.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        GlassToggle.Toggled+=(_,_)=>ReadAppearance();PaletteChoice.SelectionChanged+=(_,_)=>ReadAppearance();FitCheck.Click+=(_,_)=>ReadAppearance();MotionCheck.Click+=(_,_)=>ReadAppearance();
        foreach(var slider in new[]{BlendSlider,BlurSlider,ShadeSlider,PanelSlider})slider.ValueChanged+=(_,_)=>ReadAppearance();
    }
    void BuildGuide()
    {
        var help=Stack(Text("三步，连上朋友的世界。",32),Text("01   加入方生成分享信息",21),Text("想加入朋友世界的人选择“加入方”，生成分享信息，将 IPv6、端口和 PIN 私下发给存档接入方。"),Text("02   存档接入方连接",21),Text("在存档所在内网运行，填写加入方的信息并连接。实际存档可以在本机，也可以在内网另一台电脑。"),Text("03   对局域网开放，然后加入",21),Text("开服电脑在 Minecraft Java 中对局域网开放并保持运行。加入方选择带〈LanPin〉前缀的世界；列表不显示时，从软件中复制备用地址直接连接。"),Text("连接条件",21),Text("双方 IPv6 需互通，加入方监听端口默认 38443。双方游戏版本、模组和身份须兼容。没有中继、自动重连或通用虚拟 IP。"));
        help.MaxWidth=760;help.HorizontalAlignment=HorizontalAlignment.Left;help.Margin=new Thickness(0,12,0,30);guide.Children.Add(new ScrollViewer{Content=help,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    internal void ShowPage(int index)
    {
        Drawer.IsPaneOpen=false;home.Visibility=index==0?Visibility.Visible:Visibility.Collapsed;settings.Visibility=index==1?Visibility.Visible:Visibility.Collapsed;guide.Visibility=index==2?Visibility.Visible:Visibility.Collapsed;
        if(ready&&!appearance.ReduceMotion){var visual=ElementCompositionPreview.GetElementVisual(pages);var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.InsertKeyFrame(0,.85f);animation.InsertKeyFrame(1,1);animation.Duration=TimeSpan.FromMilliseconds(167);visual.StartAnimation("Opacity",animation);}
    }
    internal void OpenConnectionDrawer(){ConnectionPane.Visibility=Visibility.Visible;WorldPane.Visibility=Visibility.Collapsed;Drawer.IsPaneOpen=true;}
    internal void OpenWorlds(){ConnectionPane.Visibility=Visibility.Collapsed;WorldPane.Visibility=Visibility.Visible;Drawer.IsPaneOpen=true;}
    internal void SetRole(bool value)
    {
        if(running)return;joiner=value;RoleChoice.SelectedIndex=value?0:1;
        PinBox.Text="";PinBox.IsReadOnly=value;PinBox.PlaceholderText=value?"开始后生成一次性 PIN":"粘贴加入方给出的完整 PIN";
        AddressBox.Text=value?FindIpv6():"";ManualBox.IsEnabled=!value;DetectButton.Visibility=CopyButton.Visibility=value?Visibility.Visible:Visibility.Collapsed;CopyButton.IsEnabled=false;
        HomeRole.Text=value?"加入朋友的世界":"接入本机或内网存档";StartButton.Content=value?"生成分享信息  ↗":"连接加入方  ↗";
        RoleHelp.Text=value?"将 IPv6、端口和 PIN 私下发给存档接入方。":"实际存档电脑需对局域网开放，并保持世界运行。";SetState("准备就绪",value?"分享信息，等待朋友接入":"接入本机或内网存档","未连接");
    }
    void RefreshNetworks()
    {
        if(running)return;string? old=(NetworkChoice.SelectedItem as LanInterface)?.Address.ToString();NetworkChoice.Items.Clear();
        if(Preview)NetworkChoice.Items.Add(new LanInterface("示例以太网",IPAddress.Parse("192.168.10.8"),IPAddress.Parse("255.255.255.0")));
        else
        {
            var preferred=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up&&x.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any))).SelectMany(x=>x.GetIPProperties().UnicastAddresses).Select(x=>x.Address.ToString()).ToHashSet();
            foreach(var nic in LanInterface.All().OrderByDescending(x=>preferred.Contains(x.Address.ToString())))NetworkChoice.Items.Add(nic);
        }
        NetworkChoice.SelectedItem=NetworkChoice.Items.Cast<LanInterface>().FirstOrDefault(x=>x.Address.ToString()==old);if(NetworkChoice.SelectedIndex<0&&NetworkChoice.Items.Count>0)NetworkChoice.SelectedIndex=0;
    }
    string FindIpv6()
    {
        if(Preview)return "";
        var local=(NetworkChoice.SelectedItem as LanInterface)?.Address;
        return NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up).OrderByDescending(x=>local!=null&&x.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(local))).SelectMany(x=>x.GetIPProperties().UnicastAddresses).Select(x=>x.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetworkV6&&!a.IsIPv6LinkLocal&&!a.IsIPv6Multicast&&!IPAddress.IsLoopback(a)&&(a.GetAddressBytes()[0]&0xe0)==0x20)?.ToString()??"";
    }
    void SetBusy(bool busy)
    {
        RoleChoice.IsEnabled=NetworkChoice.IsEnabled=RefreshButton.IsEnabled=PortBox.IsEnabled=StartButton.IsEnabled=DetectButton.IsEnabled=!busy;
        AddressBox.IsReadOnly=busy;PinBox.IsReadOnly=busy||joiner;ManualBox.IsEnabled=!busy&&!joiner;
        StopButton.Visibility=HomeStop.Visibility=WorldsButton.Visibility=busy?Visibility.Visible:Visibility.Collapsed;StopButton.IsEnabled=HomeStop.IsEnabled=busy;HomeButton.Content=busy?"连接详情  ↗":"开始联机  ↗";CopyButton.IsEnabled=busy&&joiner&&PinBox.Text.Length>0;
    }
    internal async Task StartAsync(bool allowPreviewNetworking=false)
    {
        if(running||(Preview&&!allowPreviewNetworking))return;
        try
        {
            if(NetworkChoice.SelectedItem is not LanInterface nic)throw new ArgumentException("请选择本机内网接口。");
            string address=AddressBox.Text.Trim().Trim('[',']');if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetworkV6||ip.Equals(IPAddress.IPv6Any)||ip.IsIPv6Multicast)throw new ArgumentException("请输入可供对方访问的加入方 IPv6 地址，不含端口。");
            if(!int.TryParse(PortBox.Text,out int port)||port is <1024 or >65535)throw new ArgumentException("TCP 端口应在 1024 至 65535 之间。");
            Game? manual=null;
            if(!joiner&&!string.IsNullOrWhiteSpace(ManualBox.Text))
            {if(!IPEndPoint.TryParse(ManualBox.Text.Trim(),out var ep)||ep.AddressFamily!=AddressFamily.InterNetwork||ep.Port<1||(!nic.Contains(ep.Address)&&!IPAddress.IsLoopback(ep.Address)))throw new ArgumentException("游戏地址应位于所选内网或本机。");manual=new(ep.ToString(),"手动指定的 Java 存档",ep.Address.ToString(),ep.Port);}
            string pin=joiner?Protocol.NewPin():PinBox.Text;Protocol.Pin(pin);PinBox.Text=pin;logs.Text="";ClearWorlds();running=true;stopping=false;SetBusy(true);
            SetState(joiner?"正在生成分享":"正在连接","验证分享信息与加密连接","连接中");var current=new Session(OnLog,_=>{},OnWorld);session=current;bool share=joiner;
            run=Task.Run(async()=>
            {
                try{if(share)await current.Share(pin,port,nic);else await current.Join(address,pin,port,nic,manual);}
                catch(Exception ex){if(!stopping)await UiAsync(()=>{OnLog("连接结束："+ex.Message);Notify(ex.Message);});}
                finally
                {
                    await current.DisposeAsync();await UiAsync(()=>{if(session!=current)return;session=null;running=false;stopping=false;SetBusy(false);PinBox.Text="";CopyButton.IsEnabled=false;ClearWorlds();SetState("连接已结束","可以重新分享或连接朋友","未连接");});
                }
            });await Task.Yield();
        }
        catch(Exception ex){Notify(ex.Message);}
    }
    internal async Task StopAsync()
    {if(!running)return;stopping=true;StopButton.IsEnabled=HomeStop.IsEnabled=false;SetState("正在断开","释放当前会话与游戏连接","断开中");session?.RequestStop();if(run!=null)await run;}
    Task UiAsync(System.Action action)
    {
        if(closed)return Task.CompletedTask;if(DispatcherQueue.HasThreadAccess){action();return Task.CompletedTask;}
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!DispatcherQueue.TryEnqueue(()=>{try{if(!closed)action();completion.SetResult();}catch(Exception ex){completion.SetException(ex);}}))completion.TrySetResult();return completion.Task;
    }
    void OnLog(string message)=>_=UiAsync(()=>
    {
        if(logs.Text.Length>60000)logs.Text="";logs.Text+=$"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}";
        if(message.StartsWith("等待存档接入方"))SetState("等待朋友接入","分享信息，等待朋友接入","等待配对");
        else if(message.StartsWith("PIN 验证成功"))SetState("已安全连接",joiner?"等待远端存档公告":"请让存档电脑对局域网开放","已连接");
    });
    void SetState(string title,string hint,string compact){StateTitle.Text=title;StateHint.Text=hint;TopStatus.Text=compact;}
    void OnWorld(WorldNotice notice)=>_=UiAsync(()=>
    {
        var old=worlds.FirstOrDefault(x=>x.Key==notice.Key);if(!notice.Available){if(old!=null)RemoveWorld(old);return;}if(old!=null){old.Seen=DateTime.UtcNow;return;}
        var world=new WorldItem(notice.Key,notice.Title,$"{notice.Host}:{notice.Port}");worlds.Add(world);var item=Stack(Text(world.Title,17),Text(world.Endpoint,12),Action("复制备用地址  ↗",(_,_)=>Copy(world.Endpoint)));rows[world.Key]=item;WorldRows.Children.Add(item);UpdateWorldCount();
    });
    void RemoveWorld(WorldItem world){worlds.Remove(world);if(rows.Remove(world.Key,out var element))WorldRows.Children.Remove(element);UpdateWorldCount();}
    void ClearWorlds(){worlds.Clear();rows.Clear();WorldRows.Children.Clear();UpdateWorldCount();}
    void UpdateWorldCount(){WorldsButton.Content=$"查看存档  {worlds.Count}  ↗";EmptyWorlds.Visibility=worlds.Count==0?Visibility.Visible:Visibility.Collapsed;}
    void CopyShare(){if(running&&joiner)Copy($"IPv6: {AddressBox.Text}\n端口: {PortBox.Text}\nPIN: {PinBox.Text}");}
    void Copy(string text){try{var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);Notify("已复制，请私下发送给朋友。",false);}catch{Notify("剪贴板暂时不可用，请稍后重试。");}}
    internal void Notify(string message,bool error=true){if(closed)return;toast.Message=message;toast.Severity=error?InfoBarSeverity.Warning:InfoBarSeverity.Informational;toast.IsOpen=true;}
    void SetAppearanceControls()
    {GlassToggle.IsOn=appearance.DesktopGlass;BlurSlider.Value=appearance.Blur;ShadeSlider.Value=appearance.Shade;BlendSlider.Value=appearance.WallpaperBlend;PanelSlider.Value=appearance.PanelOpacity;FitCheck.IsChecked=appearance.FitInside;MotionCheck.IsChecked=appearance.ReduceMotion;PaletteChoice.SelectedIndex=appearance.Palette=="Dusk"?1:appearance.Palette=="Slate"?2:0;}
    void ReadAppearance()
    {
        if(!ready)return;bool blurChanged=appearance.Blur!=BlurSlider.Value;
        appearance=appearance with{DesktopGlass=GlassToggle.IsOn,Blur=BlurSlider.Value,Shade=ShadeSlider.Value,WallpaperBlend=BlendSlider.Value,PanelOpacity=PanelSlider.Value,FitInside=FitCheck.IsChecked==true,ReduceMotion=MotionCheck.IsChecked==true,Palette=PaletteChoice.SelectedIndex==1?"Dusk":PaletteChoice.SelectedIndex==2?"Slate":"Forest"};
        ApplyAppearance();saveTimer.Stop();saveTimer.Start();if(blurChanged){blurTimer.Stop();blurTimer.Start();}
    }
    void ApplyAppearance()
    {
        if(backdrop!=appearance.DesktopGlass)
        {
            backdrop=appearance.DesktopGlass;
            SystemBackdrop=appearance.DesktopGlass?new DesktopAcrylicBackdrop():null;
        }
        string[] colors=appearance.Palette switch{"Dusk"=>["#EAD8C9","#C7B6A8"],"Slate"=>["#DBE2E4","#B9C6CD"],_=>["#E7DFD7","#CBC7C3"]};
        baseLayer.Background=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1),GradientStops={new(){Color=Brush(colors[0]).Color,Offset=0},new(){Color=Brush(colors[1]).Color,Offset=1}}};baseLayer.Opacity=appearance.DesktopGlass?.12:1;
        veil.Background=Brush("#EEE5DD");veil.Opacity=appearance.DesktopGlass?.12+appearance.Shade*.22:appearance.Shade*.08;
        WallpaperImage.Opacity=appearance.DesktopGlass?appearance.WallpaperBlend:1;WallpaperImage.Stretch=appearance.FitInside?Stretch.Uniform:Stretch.UniformToFill;
        MaterialStatus.Text=appearance.DesktopGlass?"WinUI Desktop Acrylic · 显示由系统透明策略决定":"普通背景";WallpaperName.Text=appearance.Wallpaper==null?"当前：默认背景":"当前：已保存的自定义背景";
        paneBrush.TintOpacity=appearance.PanelOpacity;Drawer.PaneBackground=paneBrush;
        if(appearance.ReduceMotion)pages.Transitions=null;
    }
    internal async Task ImportWallpaperAsync(string path)
    {
        try{var imported=await store.ImportAsync(path);photo=imported.Photo;appearance=appearance with{Wallpaper=imported.Path};await RefreshWallpaperAsync();ApplyAppearance();SaveAppearance();}
        catch(Exception ex){if(Preview)throw;Notify("无法使用这张图片："+ex.Message);}
    }
    internal void ResetWallpaper(){wallpaperGeneration++;photo=null;WallpaperImage.Source=null;appearance=appearance with{Wallpaper=null};ApplyAppearance();SaveAppearance();}
    internal async Task RefreshWallpaperAsync()
    {int generation=++wallpaperGeneration;var current=photo;if(current==null)return;try{var bitmap=await current.ToBitmapAsync(appearance.Blur);if(!closed&&generation==wallpaperGeneration)WallpaperImage.Source=bitmap;}catch(Exception ex){if(!closed)Notify("背景处理失败："+ex.Message);}}
    internal void SetPreviewAppearance(Appearance value)
    {bool wasReady=ready;ready=false;appearance=value.Validated();SetAppearanceControls();ready=wasReady;ApplyAppearance();SaveAppearance();}
    void SaveAppearance(){try{store.Save(appearance);}catch{Notify("外观设置暂时无法保存，请检查本机数据目录。");}}
}

