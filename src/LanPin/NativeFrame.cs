using System.Runtime.InteropServices;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Windowing;

namespace LanPin.Desktop;
// Only window geometry compatibility on Windows 10. Materials are entirely WinUI SystemBackdrop.
internal sealed class NativeFrame:IDisposable
{
    readonly Window window;readonly FrameworkElement root;readonly IntPtr hwnd;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    (int Width,int Height,int Radius)? last;bool closed;
    public string Mode{get;private set;}="pending";
    public int UpdateCount{get;private set;}
    public static double Scale(Window window)=>Math.Max(96,GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)))/96.0;
    public NativeFrame(Window window,FrameworkElement root)
    {
        this.window=window;this.root=root;hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);
        timer=window.DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(16);timer.IsRepeating=false;timer.Tick+=(_,_)=>Update();
        window.AppWindow.Changed+=Changed;root.SizeChanged+=SizeChanged;Update();
    }
    void Changed(AppWindow sender,AppWindowChangedEventArgs args){if(args.DidSizeChange||args.DidPresenterChange)Queue();}
    void SizeChanged(object sender,SizeChangedEventArgs args){ClipContent();Queue();}
    void Queue(){if(!closed&&!timer.IsRunning)timer.Start();}
    void ClipContent()
    {
        if(closed||root.ActualWidth<=0||root.ActualHeight<=0)return;
        bool max=window.AppWindow.Presenter is OverlappedPresenter p&&p.State==OverlappedPresenterState.Maximized;
        var visual=ElementCompositionPreview.GetElementVisual(root);var clip=visual.Compositor.CreateRectangleClip();
        clip.Right=(float)root.ActualWidth;clip.Bottom=(float)root.ActualHeight;
        clip.TopLeftRadius=clip.TopRightRadius=clip.BottomLeftRadius=clip.BottomRightRadius=new Vector2(max?0:16);
        visual.Clip=clip;
    }
    internal void Update()
    {
        if(closed||!GetWindowRect(hwnd,out var rect))return;
        bool max=window.AppWindow.Presenter is OverlappedPresenter p&&p.State==OverlappedPresenterState.Maximized;
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top,radius=max?0:(int)Math.Round(16*Scale(window));
        if(width<=0||height<=0)return;var key=(width,height,radius);if(last==key)return;
        int preference=max?1:2;
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)&&DwmSetWindowAttribute(hwnd,33,ref preference,4)==0)
        {SetWindowRgn(hwnd,IntPtr.Zero,true);Mode=max?"square":"native";}
        else if(max){SetWindowRgn(hwnd,IntPtr.Zero,true);Mode="square";}
        else
        {
            var region=CreateRoundRectRgn(0,0,width,height,radius*2,radius*2);if(region==IntPtr.Zero)return;
            if(SetWindowRgn(hwnd,region,true)==0){DeleteObject(region);return;}Mode="region";
        }
        last=key;UpdateCount++;ClipContent();
    }
    internal bool CornersClipped()
    {
        if(!GetWindowRect(hwnd,out var rect))return false;var region=CreateRoundRectRgn(0,0,0,0,0,0);if(region==IntPtr.Zero)return false;
        try{if(GetWindowRgn(hwnd,region)<=0)return false;int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;return !PtInRegion(region,0,0)&&!PtInRegion(region,w-1,0)&&!PtInRegion(region,0,h-1)&&!PtInRegion(region,w-1,h-1)&&PtInRegion(region,w/2,h/2);}finally{DeleteObject(region);}
    }
    public void Dispose(){closed=true;timer.Stop();window.AppWindow.Changed-=Changed;root.SizeChanged-=SizeChanged;}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,[MarshalAs(UnmanagedType.Bool)]bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr region);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int length);
}
