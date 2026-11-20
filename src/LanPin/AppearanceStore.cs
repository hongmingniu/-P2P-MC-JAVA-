using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace LanPin.Desktop;
public sealed record Appearance
{
    public string? Wallpaper{get;init;}
    public string Palette{get;init;}="Forest";
    public double Blur{get;init;}=7;
    public double Shade{get;init;}=.45;
    public double PanelOpacity{get;init;}=.73;
    public bool DesktopGlass{get;init;}=true;
    public double WallpaperBlend{get;init;}=.22;
    public bool FitInside{get;init;}
    public bool ReduceMotion{get;init;}
    static double Clamp(double value,double min,double max,double fallback)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
    public Appearance Validated()=>this with{Blur=Clamp(Blur,0,24,7),Shade=Clamp(Shade,.2,.8,.45),PanelOpacity=Clamp(PanelOpacity,.4,.95,.73),WallpaperBlend=Clamp(WallpaperBlend,0,.8,.22),Palette=Palette is "Dusk" or "Slate"?Palette:"Forest"};
}
public sealed class AppearanceStore
{
    public string Root{get;}
    public string ConfigPath=>Path.Combine(Root,"appearance-v2.json");
    public AppearanceStore(string? root=null)=>Root=Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LanPin"));
    public Appearance Load()
    {
        try
        {
            var value=JsonSerializer.Deserialize<Appearance>(File.ReadAllText(ConfigPath))?.Validated()??new();
            string folder=Path.GetFullPath(Path.Combine(Root,"wallpapers"))+Path.DirectorySeparatorChar;
            bool valid=value.Wallpaper is { } path&&Path.GetFullPath(path).StartsWith(folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(path);
            return value with{Wallpaper=valid?value.Wallpaper:null};
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException){return new();}
    }
    public void Save(Appearance value)
    {Directory.CreateDirectory(Root);string temp=ConfigPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value.Validated(),new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,ConfigPath,true);}
    public async Task<(string Path,PhotoData Photo)> ImportAsync(string path)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not(".png" or ".jpg" or ".jpeg" or ".bmp"))throw new ArgumentException("请选择 PNG、JPG 或 BMP 图片");
        var info=new FileInfo(path);if(!info.Exists||info.Length>24*1024*1024)throw new ArgumentException("图片不存在或超过 24 MB");
        var photo=await PhotoData.ReadAsync(path);string folder=Path.Combine(Root,"wallpapers");Directory.CreateDirectory(folder);
        string destination=Path.Combine(folder,Guid.NewGuid().ToString("N")+extension);await Task.Run(()=>File.Copy(path,destination,false));
        return(destination,photo);
    }
}
public sealed record PhotoData(int Width,int Height,byte[] Pixels)
{
    public static async Task<PhotoData> ReadAsync(string path)
    {
        var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));using var stream=await file.OpenReadAsync();
        var decoder=await BitmapDecoder.CreateAsync(stream);
        if((ulong)decoder.PixelWidth*decoder.PixelHeight>120_000_000)throw new ArgumentException("图片分辨率过大，请缩小后再试");
        double scale=Math.Min(1,1600.0/Math.Max(decoder.PixelWidth,decoder.PixelHeight));
        var transform=new BitmapTransform{ScaledWidth=(uint)Math.Max(1,Math.Round(decoder.PixelWidth*scale)),ScaledHeight=(uint)Math.Max(1,Math.Round(decoder.PixelHeight*scale))};
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,transform,ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
        return new((int)Math.Max(1,Math.Round(decoder.OrientedPixelWidth*scale)),(int)Math.Max(1,Math.Round(decoder.OrientedPixelHeight*scale)),data.DetachPixelData());
    }
    public async Task<WriteableBitmap> ToBitmapAsync(double blur)
    {
        var pixels=await Task.Run(()=>Blur(blur));var bitmap=new WriteableBitmap(Width,Height);
        using(var buffer=bitmap.PixelBuffer.AsStream())await buffer.WriteAsync(pixels);bitmap.Invalidate();return bitmap;
    }
    internal byte[] Blur(double amount)
    {
        var a=(byte[])Pixels.Clone();int radius=(int)Math.Round(amount*Math.Max(Width,Height)/1600.0/1.6);if(radius<1)return a;
        var b=new byte[a.Length];
        for(int pass=0;pass<3;pass++){Box(a,b,Width,Height,radius,true);Box(b,a,Width,Height,radius,false);}return a;
    }
    static void Box(byte[] input,byte[] output,int width,int height,int radius,bool horizontal)
    {
        int lines=horizontal?height:width,length=horizontal?width:height,step=horizontal?4:width*4,div=radius*2+1;
        for(int line=0;line<lines;line++)
        {
            int origin=horizontal?line*width*4:line*4;
            for(int c=0;c<4;c++)
            {
                int sum=0;for(int p=-radius;p<=radius;p++)sum+=input[origin+Math.Clamp(p,0,length-1)*step+c];
                for(int p=0;p<length;p++){output[origin+p*step+c]=(byte)(sum/div);sum-=input[origin+Math.Max(0,p-radius)*step+c];sum+=input[origin+Math.Min(length-1,p+radius+1)*step+c];}
            }
        }
    }
}
