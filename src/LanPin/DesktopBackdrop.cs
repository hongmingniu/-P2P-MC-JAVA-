using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace LanPin.Desktop;

// Native compositor material. No screen capture, wallpaper sampling, or whole-window opacity.
internal sealed class DesktopBackdrop : IDisposable
{
    readonly Window window;
    readonly Action changed;
    HwndSource? source;
    (bool Enabled, Color Tint)? last;
    public bool Active { get; private set; }
    public string Status { get; private set; }="窗口创建后启用桌面毛玻璃";
    public DesktopBackdrop(Window window,Action changed)
    {this.window=window;this.changed=changed;window.SourceInitialized+=SourceReady;window.ContentRendered+=ContentReady;}
    void SourceReady(object? sender,EventArgs args)
    {source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(WindowMessage);last=null;changed();}
    void ContentReady(object? sender,EventArgs args){last=null;changed();}
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message is 0x031E or 0x031A or 0x001A)
            _=window.Dispatcher.BeginInvoke(new Action(()=>{if(source?.IsDisposed!=false)return;last=null;changed();}));
        return IntPtr.Zero;
    }
    public void Apply(bool enabled,Color tint)
    {
        if(source==null)return;
        if(last==(enabled,tint))return;
        last=(enabled,tint);Active=false;
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
            // Full-client DWM glass obscures the sampled backdrop for the legacy accent API.
            // Keep just a 1-DIP frame on Windows 10; full extension is for modern DWM backdrops.
            WindowChrome.GetWindowChrome(window).GlassFrameThickness=modern?new Thickness(-1):new Thickness(0,1,0,0);
            source.CompositionTarget.BackgroundColor=Colors.Transparent;
            var margins=modern?new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1}:new Margins{Top=1};
            if(DwmExtendFrameIntoClientArea(hwnd,ref margins)!=0){Status="桌面材质不可用 · 使用普通背景";return;}
            int dark=1;DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));
            // Windows 11 22H2+: documented Desktop Acrylic, not wallpaper-only Mica.
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
            {
                int material=3;
                if(DwmSetWindowAttribute(hwnd,38,ref material,sizeof(int))==0)
                {Active=true;Status="桌面毛玻璃 · Windows Acrylic";return;}
            }
            // Windows 10 compatibility API. Accent policy is unsupported on some systems.
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,17134)&&Accent(hwnd,4,tint))
            {Active=true;Status="桌面毛玻璃 · Windows 10 兼容材质";return;}
            Status="系统未提供毛玻璃材质 · 使用普通背景";
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or UnauthorizedAccessException or System.Security.SecurityException)
        {Status="系统未提供毛玻璃材质 · 使用普通背景";}
        finally
        {
            if(!Active)
            {
                Disable(hwnd);
                WindowChrome.GetWindowChrome(window).GlassFrameThickness=new Thickness(0);
                source.CompositionTarget.BackgroundColor=Color.FromRgb(16,37,29);
            }
        }
    }
    static void Disable(IntPtr hwnd)
    {
        try{int none=1;DwmSetWindowAttribute(hwnd,38,ref none,sizeof(int));Accent(hwnd,0,Colors.Transparent);}
        catch(EntryPointNotFoundException){}
    }
    static bool Accent(IntPtr hwnd,int state,Color tint)
    {
        var policy=new AccentPolicy{State=state,Flags=0,GradientColor=((uint)tint.A<<24)|((uint)tint.B<<16)|((uint)tint.G<<8)|tint.R};
        int size=Marshal.SizeOf<AccentPolicy>();var memory=Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy,memory,false);
            var data=new CompositionData{Attribute=19,Data=memory,Size=(nuint)size};
            return SetWindowCompositionAttribute(hwnd,ref data);
        }
        finally{Marshal.FreeHGlobal(memory);}
    }
    public void Dispose(){window.SourceInitialized-=SourceReady;window.ContentRendered-=ContentReady;if(source?.IsDisposed==false)source.RemoveHook(WindowMessage);source=null;}
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy{public int State,Flags;public uint GradientColor;public int AnimationId;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
}
