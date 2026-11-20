using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LanPin.Desktop;
internal enum DesktopMaterial { None,ModernAcrylic,LegacyBlur }

// Native background composition only; no independent visual may cover WPF content.
internal sealed class DesktopBackdrop : IDisposable
{
    readonly Window window;
    readonly Action changed;
    readonly DispatcherTimer frameTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    HwndSource? source;
    (bool Enabled,Color Tint)? last;
    (int Width,int Height,uint Dpi,bool Maximized)? frame;
    bool disposed,liveMove;
    public bool LiveMove=>liveMove;
    public bool Active=>Kind!=DesktopMaterial.None;
    public DesktopMaterial Kind{get;private set;}
    public int MaterialApplyCount{get;private set;}
    public int FrameUpdateCount{get;private set;}
    public string CornerMode{get;private set;}="pending";
    public string Status{get;private set;}="窗口创建后启用桌面毛玻璃";
    public DesktopBackdrop(Window window,Action changed)
    {
        this.window=window;this.changed=changed;
        window.SourceInitialized+=SourceReady;window.ContentRendered+=ContentReady;
        frameTimer.Tick+=(_,_)=>{frameTimer.Stop();UpdateFrame();};
    }
    void SourceReady(object? sender,EventArgs args)
    {source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(WindowMessage);last=null;changed();UpdateFrame();}
    void ContentReady(object? sender,EventArgs args){if(disposed)return;last=null;changed();frame=null;UpdateFrame();}
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x0231)SetLiveMove(true);
        else if(message==0x0232)SetLiveMove(false);
        if(message is 0x0005 or 0x02E0)QueueFrame();
        if(message is 0x031E or 0x031A or 0x001A)
            _=window.Dispatcher.BeginInvoke(new Action(()=>{if(disposed||source?.IsDisposed!=false)return;last=null;changed();frame=null;UpdateFrame();}));
        return IntPtr.Zero;
    }
    internal void SetLiveMove(bool moving){liveMove=moving;if(!moving)QueueFrame();}
    void QueueFrame(){if(!disposed&&source!=null&&!frameTimer.IsEnabled)frameTimer.Start();}
    internal void UpdateFrame()
    {
        if(disposed||source?.IsDisposed!=false||!GetWindowRect(source.Handle,out var rect))return;
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;if(width<=0||height<=0)return;
        uint dpi=GetDpiForWindow(source.Handle);if(dpi==0)dpi=96;
        bool max=window.WindowState==WindowState.Maximized;
        var next=(width,height,dpi,max);if(frame==next)return;
        int preference=max?1:2;
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)&&DwmSetWindowAttribute(source.Handle,33,ref preference,sizeof(int))==0)
        {
            if(SetWindowRgn(source.Handle,IntPtr.Zero,true)!=0){frame=next;FrameUpdateCount++;CornerMode=max?"square":"native";}
            return;
        }
        if(max)
        {if(SetWindowRgn(source.Handle,IntPtr.Zero,true)!=0){frame=next;FrameUpdateCount++;CornerMode="square";}return;}
        int diameter=Math.Max(2,(int)Math.Round(36*dpi/96.0));
        var region=CreateRoundRectRgn(0,0,width,height,diameter,diameter);
        if(region==IntPtr.Zero)return;
        // SetWindowRgn takes ownership only on success.
        if(SetWindowRgn(source.Handle,region,true)!=0){frame=next;FrameUpdateCount++;CornerMode="region";}
        else DeleteObject(region);
    }
    internal bool HasCleanRoundedRegion()
    {
        if(source==null||!GetWindowRect(source.Handle,out var rect))return false;
        var region=CreateRoundRectRgn(0,0,0,0,0,0);if(region==IntPtr.Zero)return false;
        try
        {
            if(GetWindowRgn(source.Handle,region)<=0)return false;
            int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;
            return !PtInRegion(region,0,0)&&!PtInRegion(region,w-1,0)&&!PtInRegion(region,0,h-1)&&!PtInRegion(region,w-1,h-1)&&PtInRegion(region,w/2,h/2);
        }
        finally{DeleteObject(region);}
    }
    public void Apply(bool enabled,Color tint)
    {
        if(disposed||source==null)return;
        if(last is { } previous&&previous.Enabled==enabled&&(previous.Tint==tint||Active))return;
        last=(enabled,tint);MaterialApplyCount++;Kind=DesktopMaterial.None;
        var hwnd=source.Handle;
        try
        {
            Disable(hwnd);
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
            // Legacy blur (3), not the drag-stalling acrylic (4) or an overlay target (5).
            if(Accent(hwnd,3))
            {Kind=DesktopMaterial.LegacyBlur;Status="桌面毛玻璃 · Windows 10 轻量模糊";return;}
            Status="系统未提供毛玻璃材质 · 使用普通背景";
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or UnauthorizedAccessException or System.Security.SecurityException)
        {Status="系统未提供毛玻璃材质 · 使用普通背景";}
        finally
        {
            if(!Active)
            {
                Disable(hwnd);WindowChrome.GetWindowChrome(window).GlassFrameThickness=new Thickness(0);
                source.CompositionTarget.BackgroundColor=Color.FromRgb(16,37,29);
            }
        }
    }
    static void Disable(IntPtr hwnd)
    {try{int none=1;DwmSetWindowAttribute(hwnd,38,ref none,sizeof(int));Accent(hwnd,0);}catch(EntryPointNotFoundException){}}
    static bool Accent(IntPtr hwnd,int state)
    {
        var policy=new AccentPolicy{State=state};int bytes=Marshal.SizeOf<AccentPolicy>();var memory=Marshal.AllocHGlobal(bytes);
        try{Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData{Attribute=19,Data=memory,Size=(nuint)bytes};return SetWindowCompositionAttribute(hwnd,ref data);}
        finally{Marshal.FreeHGlobal(memory);}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;frameTimer.Stop();window.SourceInitialized-=SourceReady;window.ContentRendered-=ContentReady;if(source?.IsDisposed==false)source.RemoveHook(WindowMessage);source=null;Kind=DesktopMaterial.None;}
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy{public int State,Flags;public uint GradientColor;public int AnimationId;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,[MarshalAs(UnmanagedType.Bool)]bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
}
