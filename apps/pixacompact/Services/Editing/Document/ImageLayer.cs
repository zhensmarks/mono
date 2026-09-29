using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
namespace PixelcutCompact.Services.Editing.Document;
public sealed class ImageLayer
{
    public Guid Id{get;init;}=Guid.NewGuid();
    public string Name{get;init;}="Layer";
    public bool Visible{get;init;}=true;
    public byte Opacity{get;init;}=255;
    public LayerBlendMode BlendMode{get;init;}=LayerBlendMode.Normal;
    public LayerTransform Transform{get;init;}=new();
    public PixelBuffer? Pixels{get;init;}
    public byte[]? Mask{get;init;}
    public bool IsBackground{get;init;}

    // ── Thumbnail cache: keyed by (Id, maxSize) ──────────────────────────
    // ImageLayer adalah immutable; cache aman selama layer tidak diganti.
    private static readonly ConcurrentDictionary<(Guid,int),Bitmap> _thumbCache = new();

    public static void InvalidateThumbnail(Guid id)
    {
        foreach (var key in _thumbCache.Keys)
            if (key.Item1 == id) _thumbCache.TryRemove(key, out _);
    }

    /// <summary>
    /// Kembalikan thumbnail dengan cache. Bitmap disimpan selama layer belum diganti.
    /// Aman dipanggil dari UI thread.
    /// </summary>
    public Bitmap? GetThumbnail(int maxSize=64)
    {
        if(Pixels==null)return null;
        var key=(Id,maxSize);
        if(_thumbCache.TryGetValue(key,out var cached)&&cached!=null)
            return cached;
        int w=Pixels.Width,h=Pixels.Height;
        double scale=Math.Min((double)maxSize/w,(double)maxSize/h);
        Bitmap bmp;
        if(scale>=1) bmp=Pixels.ToAvaloniaBitmap();
        else
        {
            int tw=(int)(w*scale),th=(int)(h*scale);
            bmp=Pixels.Resize(Math.Max(1,tw),Math.Max(1,th)).ToAvaloniaBitmap();
        }
        _thumbCache[key]=bmp;
        return bmp;
    }

    public ImageLayer Clone()=>new(){
        Id=Id,Name=Name,Visible=Visible,Opacity=Opacity,BlendMode=BlendMode,
        Transform=Transform.Clone(),Pixels=Pixels?.Clone(),
        Mask=Mask!=null?(byte[])Mask.Clone():null,IsBackground=IsBackground
    };
    public ImageLayer With(bool? visible=null,byte? opacity=null,LayerBlendMode? blend=null,
        string? name=null,LayerTransform? transform=null,PixelBuffer? pixels=null,byte[]? mask=null)=>new(){
        Id=Id,IsBackground=IsBackground,
        Name=name??Name,Visible=visible??Visible,Opacity=opacity??Opacity,
        BlendMode=blend??BlendMode,Transform=transform??Transform,
        Pixels=pixels??Pixels,Mask=mask??Mask
    };
}
