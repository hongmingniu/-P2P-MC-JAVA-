using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LanPin.Desktop;
internal enum DesktopMaterial { None,ModernAcrylic,HostComposition,LegacyBlur }

// The compositor samples behind the HWND; WPF text keeps its normal opacity.
internal sealed class DesktopBackdrop : IDisposable
{
    readonly Window window;
    readonly Action changed;
    readonly DispatcherTimer resizeTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    HwndSource? source;
    IntPtr host;
    (bool Enabled,Color Tint)? last;
    (int Width,int Height,int Radius,bool Visible)? size;
    bool disposed,liveMove;
    public bool LiveMove=>liveMove;
    public bool Active=>Kind!=DesktopMaterial.None;
    public DesktopMaterial Kind{get;private set;}
    public int MaterialApplyCount{get;private set;}
    public int HostResizeCount{get;private set;}
    public string Status{get;private set;}="窗口创建后启用桌面毛玻璃";
    public DesktopBackdrop(Window window,Action changed)
    {
        this.window=window;this.changed=changed;
        window.SourceInitialized+=SourceReady;window.ContentRendered+=ContentReady;
        resizeTimer.Tick+=(_,_)=>{resizeTimer.Stop();UpdateHostSize();};
    }
    void SourceReady(object? sender,EventArgs args)
    {source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(WindowMessage);last=null;changed();}
    void ContentReady(object? sender,EventArgs args){if(disposed)return;last=null;changed();}
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x0231)SetLiveMove(true);
        else if(message==0x0232)SetLiveMove(false);
        // Moving changes no backdrop geometry. Coalesce only real size / DPI changes.
        if(message is 0x0005 or 0x02E0)QueueResize();
        if(message is 0x031E or 0x031A or 0x001A)
            _=window.Dispatcher.BeginInvoke(new Action(()=>{if(disposed||source?.IsDisposed!=false)return;last=null;changed();}));
        return IntPtr.Zero;
    }
    internal void SetLiveMove(bool moving)
    {liveMove=moving;if(!moving)QueueResize();}
    internal void QueueResize(){if(!disposed&&host!=IntPtr.Zero&&!resizeTimer.IsEnabled)resizeTimer.Start();}
    internal void UpdateHostSize()
    {
        if(disposed||host==IntPtr.Zero||source?.IsDisposed!=false||!GetClientRect(source.Handle,out var rect))return;
        int width=Math.Max(0,rect.Right-rect.Left),height=Math.Max(0,rect.Bottom-rect.Top);
        int radius=window.WindowState==WindowState.Maximized?0:(int)Math.Round(18*(GetDpiForWindow(source.Handle)/96.0));
        var next=(width,height,radius,width>0&&height>0&&Kind==DesktopMaterial.HostComposition);
        if(size==next)return;
        if(LanPinUpdateGlass(host,width,height,radius,next.Item4?1:0)==0){size=next;HostResizeCount++;}
        else
        {
            ReleaseHost();Kind=DesktopMaterial.None;
            if(Accent(source.Handle,3,Colors.Transparent)){Kind=DesktopMaterial.LegacyBlur;Status="桌面毛玻璃 · 轻量兼容模式";}
            else Status="桌面合成暂不可用 · 使用普通背景";
            changed();
        }
    }
    public void Apply(bool enabled,Color tint)
    {
        if(disposed||source==null)return;
        if(last is { } previous&&previous.Enabled==enabled&&(previous.Tint==tint||Kind is DesktopMaterial.HostComposition or DesktopMaterial.ModernAcrylic))return;
        last=(enabled,tint);MaterialApplyCount++;Kind=DesktopMaterial.None;
        var hwnd=source.Handle;
        try
        {
            ReleaseHost();Disable(hwnd);
            if(!enabled){Status="已关闭 · 使用普通背景";return;}
            if(SystemParameters.HighContrast){Status="高对比度模式 · 使用普通背景";return;}
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if(key?.GetValue("EnableTransparency") is int value&&value==0){Status="Windows 透明效果已关闭 · 使用普通背景";return;}
            if(DwmIsCompositionEnabled(out bool composition)!=0||!composition){Status="桌面合成不可用 · 使用普通背景";return;}
            bool modern=OperatingSystem.IsWindowsVersionAtLeast(10,0,22621);
            WindowChrome.GetWindowChrome(window).GlassFrameThickness=modern?new Thickness(-1):new Thickness(0,1,0,0);
            source.CompositionTarget.BackgroundColor=Colors.Transparent;
            var margins=modern?new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1}:new Margins{Top=1};
            if(DwmExtendFrameIntoClientArea(hwnd,ref margins)!=0){Status="桌面材质不可用 · 使用普通背景";return;}
            int dark=1;DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));
            if(modern)
            {
                int material=3;
                if(DwmSetWindowAttribute(hwnd,38,ref material,sizeof(int))==0)
                {Kind=DesktopMaterial.ModernAcrylic;Status="桌面毛玻璃 · Windows Acrylic";return;}
            }
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,18362))
            {
                // Host-backdrop mode enables a Composition brush instead of legacy acrylic (4).
                try
                {
                    if(Accent(hwnd,5,Colors.Transparent)&&LanPinCreateGlass(hwnd,out host)==0&&host!=IntPtr.Zero)
                    {
                        Kind=DesktopMaterial.HostComposition;Status="桌面毛玻璃 · Windows Composition";
                        UpdateHostSize();return;
                    }
                }
                catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){}
                ReleaseHost();
            }
            // Never fall back to the legacy acrylic path that stalls window dragging.
            if(Accent(hwnd,3,Colors.Transparent)){Kind=DesktopMaterial.LegacyBlur;Status="桌面毛玻璃 · 轻量兼容模式";return;}
            Status="系统未提供毛玻璃材质 · 使用普通背景";
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or UnauthorizedAccessException or System.Security.SecurityException)
        {Status="系统未提供毛玻璃材质 · 使用普通背景";}
        finally
        {
            if(!Active)
            {
                ReleaseHost();Disable(hwnd);
                WindowChrome.GetWindowChrome(window).GlassFrameThickness=new Thickness(0);
                source.CompositionTarget.BackgroundColor=Color.FromRgb(16,37,29);
            }
        }
    }
    void ReleaseHost(){resizeTimer.Stop();size=null;if(host!=IntPtr.Zero){LanPinDestroyGlass(host);host=IntPtr.Zero;}}
    static void Disable(IntPtr hwnd)
    {try{int none=1;DwmSetWindowAttribute(hwnd,38,ref none,sizeof(int));Accent(hwnd,0,Colors.Transparent);}catch(EntryPointNotFoundException){}}
    static bool Accent(IntPtr hwnd,int state,Color tint)
    {
        var policy=new AccentPolicy{State=state,GradientColor=((uint)tint.A<<24)|((uint)tint.B<<16)|((uint)tint.G<<8)|tint.R};
        int bytes=Marshal.SizeOf<AccentPolicy>();var memory=Marshal.AllocHGlobal(bytes);
        try {Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=(nuint)bytes};return SetWindowCompositionAttribute(hwnd,ref data);}
        finally{Marshal.FreeHGlobal(memory);}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;window.SourceInitialized-=SourceReady;window.ContentRendered-=ContentReady;ReleaseHost();if(source?.IsDisposed==false)source.RemoveHook(WindowMessage);source=null;Kind=DesktopMaterial.None;}
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy{public int State,Flags;public uint GradientColor;public int AnimationId;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("LanPin.Backdrop.dll",ExactSpelling=true)] static extern int LanPinCreateGlass(IntPtr hwnd,out IntPtr layer);
    [DllImport("LanPin.Backdrop.dll",ExactSpelling=true)] static extern int LanPinUpdateGlass(IntPtr layer,float width,float height,float radius,int visible);
    [DllImport("LanPin.Backdrop.dll",ExactSpelling=true)] static extern void LanPinDestroyGlass(IntPtr layer);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
}
