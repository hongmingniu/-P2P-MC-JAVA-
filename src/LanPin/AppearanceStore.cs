using System.Text.Json;
using System.Windows.Media.Imaging;

namespace LanPin.Desktop;
public sealed record Appearance
{
    public string? Wallpaper { get; init; }
    public string Palette { get; init; } = "Forest";
    public double Blur { get; init; } = 7;
    public double Shade { get; init; } = .45;
    public double PanelOpacity { get; init; } = .73;
    public bool DesktopGlass { get; init; } = true;
    public double WallpaperBlend { get; init; } = .22;
    public bool FitInside { get; init; }
    public bool ReduceMotion { get; init; }
    public Appearance Validated() => this with { Blur=Math.Clamp(Blur,0,24),WallpaperBlend=Math.Clamp(WallpaperBlend,0,.8),Shade=Math.Clamp(Shade,.2,.8),PanelOpacity=Math.Clamp(PanelOpacity,.4,.95),Palette=Palette is "Dusk" or "Slate" ? Palette : "Forest" };
}
public sealed class AppearanceStore
{
    public string Root { get; }
    public string ConfigPath => Path.Combine(Root,"appearance-v2.json");
    public AppearanceStore(string? root=null) => Root=Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LanPin"));
    public Appearance Load()
    {
        try
        {
            var value=JsonSerializer.Deserialize<Appearance>(File.ReadAllText(ConfigPath))?.Validated()??new();
            return value with {Wallpaper=ManagedImage(value.Wallpaper)?value.Wallpaper:null};
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) {return new();}
    }
    bool ManagedImage(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return false;
        var folder=Path.GetFullPath(Path.Combine(Root,"wallpapers"))+Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(folder,StringComparison.OrdinalIgnoreCase)&&File.Exists(path);
    }
    public void Save(Appearance appearance)
    {
        Directory.CreateDirectory(Root);
        string temp=ConfigPath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(appearance.Validated(),new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,ConfigPath,true);
    }
    public (string Path,BitmapSource Image) Import(string path)
    {
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))throw new ArgumentException("请选择 PNG、JPG 或 BMP 图片");
        var file=new FileInfo(path); if(!file.Exists||file.Length>24*1024*1024)throw new ArgumentException("图片不存在，或文件大于 24 MB");
        var image=ReadImage(path);
        var folder=System.IO.Path.Combine(Root,"wallpapers");Directory.CreateDirectory(folder);
        string destination=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+extension);
        File.Copy(path,destination,false);
        return(destination,image);
    }
    public static BitmapSource ReadImage(string path)
    {
        using var input=File.OpenRead(path);
        var header=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None).Frames[0];
        int width=header.PixelWidth,height=header.PixelHeight;
        if((long)width*height>120_000_000)throw new ArgumentException("图片分辨率过大，请缩小后再试");
        input.Position=0;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        if(width>=height)bitmap.DecodePixelWidth=Math.Min(width,2048);else bitmap.DecodePixelHeight=Math.Min(height,2048);
        bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();
        if(bitmap.PixelHeight<1||bitmap.PixelWidth<1)throw new ArgumentException("无法读取图片");
        return bitmap;
    }
}
