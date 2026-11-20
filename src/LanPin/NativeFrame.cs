using System.Runtime.InteropServices;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Windows.UI;
namespace LanPin.Desktop;
// Adapted from the ChatAIBox Windows shell: full client area, custom chrome, host backdrop.
internal sealed class NativeFrame:IDisposable
{
    readonly Window window;readonly FrameworkElement root;readonly IntPtr hwnd;
    readonly SubclassCallback callback;readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    IntPtr host;bool disposed,hostVisible;bool? appliedGlass,appliedDark;
    (int Width,int Height,int Radius,bool Visible)? last;
    public string Mode{get;private set;}="pending";
    public string Material{get;private set;}="none";
    public int UpdateCount{get;private set;}
    public int MaterialChanges{get;private set;}
    public bool HostActive=>host!=IntPtr.Zero&&hostVisible;
    public static double Scale(Window w)=>Math.Max(96,GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(w)))/96d;
    public NativeFrame(Window window,FrameworkElement root)
    {
        this.window=window;this.root=root;hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);callback=WindowMessage;
        timer=window.DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(16);timer.IsRepeating=false;timer.Tick+=(_,_)=>Update();
        SetWindowSubclass(hwnd,callback,1,0);
        long style=GetWindowLongPtr(hwnd,-16).ToInt64();SetWindowLongPtr(hwnd,-16,new IntPtr((style&~0x00c00000L)|0x000f0000L));
        long extended=GetWindowLongPtr(hwnd,-20).ToInt64();SetWindowLongPtr(hwnd,-20,new IntPtr(extended&~0x00020201L));
        SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x37);
        window.AppWindow.Changed+=Changed;root.SizeChanged+=SizeChanged;Update();
    }
    internal bool Apply(bool enabled,bool dark)
    {
        if(!root.IsLoaded)return false;
        bool allowed=enabled&&!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        if(appliedGlass==allowed&&appliedDark==dark)return Material!="none";
        bool materialChanged=appliedGlass!=allowed;appliedGlass=allowed;appliedDark=dark;
        if(materialChanged)
        {
            MaterialChanges++;hostVisible=false;Material="none";
            if(allowed&&!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
            {
                try
                {
                    if(host==IntPtr.Zero)host=CreateBackdrop(hwnd);
                    if(host!=IntPtr.Zero&&Accent(5)){hostVisible=true;Material="host";}
                }
                catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){}
            }
            if(hostVisible)window.SystemBackdrop=null;
            else if(allowed&&DesktopAcrylicController.IsSupported()){Accent(0);window.SystemBackdrop=new DesktopAcrylicBackdrop();Material="acrylic";}
            else{window.SystemBackdrop=null;Accent(0);}
            last=null;
        }
        var foreground=dark?Color.FromArgb(255,238,232,224):Color.FromArgb(255,37,42,48);
        window.AppWindow.TitleBar.ButtonForegroundColor=foreground;window.AppWindow.TitleBar.ButtonBackgroundColor=Microsoft.UI.Colors.Transparent;window.AppWindow.TitleBar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
        Update();return Material!="none";
    }
    bool Accent(int state)
    {
        var policy=new AccentPolicy{State=state};var ptr=Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try{Marshal.StructureToPtr(policy,ptr,false);var data=new CompositionData{Attribute=19,Data=ptr,Size=(nuint)Marshal.SizeOf<AccentPolicy>()};return SetWindowCompositionAttribute(hwnd,ref data);}finally{Marshal.FreeHGlobal(ptr);}
    }
    void Changed(AppWindow sender,AppWindowChangedEventArgs args){if(args.DidSizeChange||args.DidPresenterChange)Queue();}
    void SizeChanged(object sender,SizeChangedEventArgs args){ClipContent();Queue();}
    void Queue(){if(!disposed&&!timer.IsRunning)timer.Start();}
    bool Maximized=>window.AppWindow.Presenter is OverlappedPresenter p&&p.State==OverlappedPresenterState.Maximized;
    void ClipContent()
    {
        if(disposed||root.ActualWidth<=0||root.ActualHeight<=0)return;
        var visual=ElementCompositionPreview.GetElementVisual(root);var clip=visual.Compositor.CreateRectangleClip();clip.Right=(float)root.ActualWidth;clip.Bottom=(float)root.ActualHeight;
        clip.TopLeftRadius=clip.TopRightRadius=clip.BottomLeftRadius=clip.BottomRightRadius=new Vector2(Maximized?0:12);visual.Clip=clip;
    }
    internal void Update()
    {
        if(disposed||!GetWindowRect(hwnd,out var rect))return;
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top,radius=Maximized?0:(int)Math.Round(12*Scale(window));
        if(width<=0||height<=0)return;var key=(width,height,radius,hostVisible);if(last==key)return;
        if(host!=IntPtr.Zero)ResizeBackdrop(host,width,height,radius,hostVisible?1:0);
        int preference=Maximized?1:2;
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)&&DwmSetWindowAttribute(hwnd,33,ref preference,4)==0){SetWindowRgn(hwnd,IntPtr.Zero,true);Mode=Maximized?"square":"native";}
        else if(Maximized){SetWindowRgn(hwnd,IntPtr.Zero,true);Mode="square";}
        else{var region=CreateRoundRectRgn(0,0,width,height,radius*2,radius*2);if(region==IntPtr.Zero)return;if(SetWindowRgn(hwnd,region,true)==0){DeleteObject(region);return;}Mode="region";}
        last=key;UpdateCount++;ClipContent();
    }
    IntPtr WindowMessage(IntPtr h,uint message,IntPtr wp,IntPtr lp,nuint id,nuint data)
    {
        if(message==0x0083&&wp!=IntPtr.Zero)
        {if(IsZoomed(h)){var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(GetMonitorInfo(MonitorFromWindow(h,2),ref monitor))Marshal.StructureToPtr(monitor.Work,lp,false);}return IntPtr.Zero;}
        if(message==0x0084&&GetWindowRect(h,out var rect))
        {
            long packed=lp.ToInt64();int x=(short)(packed&0xffff),y=(short)((packed>>16)&0xffff);double scale=Scale(window);int edge=(int)Math.Ceiling(7*scale);
            if(!IsZoomed(h)){bool left=x<rect.Left+edge,right=x>=rect.Right-edge,top=y<rect.Top+edge,bottom=y>=rect.Bottom-edge;int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;if(hit!=0)return new IntPtr(hit);}
            if(y<rect.Top+(int)Math.Round(44*scale)&&x<rect.Right-(int)Math.Round(138*scale))return new IntPtr(2);
        }
        if(message==0x0082)RemoveWindowSubclass(h,callback,id);
        return DefSubclassProc(h,message,wp,lp);
    }
    internal (int Left,int Top,bool Caption) Inspect()
    {GetWindowRect(hwnd,out var rect);var point=new NativePoint();ClientToScreen(hwnd,ref point);return(point.X-rect.Left,point.Y-rect.Top,(GetWindowLongPtr(hwnd,-16).ToInt64()&0x00c00000L)!=0);}
    internal bool CornersClipped()
    {
        if(!GetWindowRect(hwnd,out var rect))return false;var region=CreateRoundRectRgn(0,0,0,0,0,0);if(region==IntPtr.Zero)return false;
        try{if(GetWindowRgn(hwnd,region)<=0)return false;int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;return !PtInRegion(region,0,0)&&!PtInRegion(region,w-1,0)&&!PtInRegion(region,0,h-1)&&!PtInRegion(region,w-1,h-1)&&PtInRegion(region,w/2,h/2);}finally{DeleteObject(region);}
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();window.AppWindow.Changed-=Changed;root.SizeChanged-=SizeChanged;RemoveWindowSubclass(hwnd,callback,1);if(host!=IntPtr.Zero){DestroyBackdrop(host);host=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy{public int State;public uint Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)] struct CompositionData{public int Attribute;public IntPtr Data;public nuint Size;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr SubclassCallback(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp,nuint id,nuint data);
    [DllImport("LanPin.Backdrop.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr CreateBackdrop(IntPtr hwnd);
    [DllImport("LanPin.Backdrop.dll",CallingConvention=CallingConvention.Cdecl)] static extern void ResizeBackdrop(IntPtr layer,float width,float height,float radius,int visible);
    [DllImport("LanPin.Backdrop.dll",CallingConvention=CallingConvention.Cdecl)] static extern void DestroyBackdrop(IntPtr layer);
    [DllImport("user32.dll")] static extern bool SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,[MarshalAs(UnmanagedType.Bool)]bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr region);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int length);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd,SubclassCallback callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hwnd,SubclassCallback callback,nuint id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
}
