using System;  
using System.Linq;  
using Avalonia;  
using Avalonia.Media.Imaging;  
using Avalonia.Platform;  
namespace PixelcutCompact.Services.Editing.Document;  
public static class LayerCompositor  
{  
    public static WriteableBitmap Render(CanvasDocument doc)  
    {  
        int w=doc.Width,h=doc.Height;  
        var dst=new PixelBuffer(w,h);  
        foreach(var layer in doc.Layers)  
        {  
            if(!layer.Visible||layer.Pixels==null)continue;  
            ComposeLayer(dst,layer,w,h);  
        }  
        return dst.ToWriteableBitmap();  
    }  
    public static PixelBuffer Composite(CanvasDocument doc)  
    {  
        int w=doc.Width,h=doc.Height;  
        var dst=new PixelBuffer(w,h);  
        foreach(var layer in doc.Layers)  
        {  
            if(!layer.Visible||layer.Pixels==null)continue;  
            ComposeLayer(dst,layer,w,h);  
        }  
        return dst;  
    } 
    private static void ComposeLayer(PixelBuffer dst,ImageLayer layer,int canvasW,int canvasH)  
    {  
        var src=layer.Pixels!;  
        int offX=(int)layer.Transform.X,offY=(int)layer.Transform.Y;  
        for(int y=0;y<canvasH;y++)  
        {  
            int sy=y-offY;  
            if(sy<0||sy>=src.Height)continue;  
            for(int x=0;x<canvasW;x++)  
            {  
                int sx=x-offX;  
                if(sx<0||sx>=src.Width)continue;  
                int di=dst.Index(x,y),si=src.Index(sx,sy),mi=sy*src.Width+sx;  
                BlendMath.Blend(dst.Bgra,di,src.Bgra,si,layer.BlendMode,layer.Opacity,layer.Mask,mi);  
            }  
        }  
    }  
}  
