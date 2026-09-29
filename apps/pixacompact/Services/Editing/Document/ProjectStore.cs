using System;  
using System.Collections.Generic;  
using System.IO;  
using System.Linq;  
using System.Text.Json;  
using Avalonia.Media.Imaging;  
namespace PixelcutCompact.Services.Editing.Document;  
public static class ProjectStore  
{  
    private record LayerMeta(string Id,string Name,bool Visible,byte Opacity,int BlendMode,  
        double X,double Y,double Width,double Height,double Rotation,bool FlipX,bool FlipY,  
        bool IsBackground,bool HasMask);  
    private record DocMeta(int Width,int Height,string ActiveLayerId,LayerMeta[] Layers);  
    public static void Save(DocEditorSession session,string path)  
    {  
        var tmp=path+".tmp_"+Guid.NewGuid().ToString("N").Substring(0,8);  
        Directory.CreateDirectory(tmp);  
        var doc=session.Document;  
        var metas=doc.Layers.Select((l,i)=>{  
            if(l.Pixels!=null){  
                using var ms=new MemoryStream();  
                l.Pixels.SavePng(ms);  
                File.WriteAllBytes(Path.Combine(tmp,"layer_"+i+".png"),ms.ToArray());  
            }  
            if(l.Mask!=null){  
                var pw=l.Pixels?.Width??doc.Width;  
                var ph=l.Pixels?.Height??doc.Height;  
                var mb=PixelBuffer.FromAlpha(l.Mask,pw,ph);  
                File.WriteAllBytes(Path.Combine(tmp,"mask_"+i+".png"),mb.ToPngBytes());  
            }  
            var t=l.Transform;  
            return new LayerMeta(l.Id.ToString(),l.Name,l.Visible,l.Opacity,(int)l.BlendMode,  
                t.X,t.Y,t.Width,t.Height,t.Rotation,t.FlipX,t.FlipY,l.IsBackground,l.Mask!=null);  
        }).ToArray();  
        var meta=new DocMeta(doc.Width,doc.Height,doc.ActiveLayerId.ToString(),metas);  
        File.WriteAllText(Path.Combine(tmp,"manifest.json"),JsonSerializer.Serialize(meta));  
        if(Directory.Exists(path))Directory.Delete(path,true);  
        Directory.Move(tmp,path);  
        session.MarkSaved();  
    } 
    public static DocEditorSession Load(string path)  
    {  
        var json=File.ReadAllText(Path.Combine(path,"manifest.json"));  
        var meta=JsonSerializer.Deserialize<DocMeta>(json)??throw new InvalidDataException();  
        var layers=new List<ImageLayer>();  
        for(int i=0;i<meta.Layers.Length;i++)  
        {  
            var lm=meta.Layers[i];  
            PixelBuffer? pix=null;  
            var pp=Path.Combine(path,"layer_"+i+".png");  
            if(File.Exists(pp)){using var bmp=new Bitmap(pp);pix=PixelBuffer.FromBitmap(bmp);}  
            byte[]? mask=null;  
            var mp=Path.Combine(path,"mask_"+i+".png");  
            if(lm.HasMask&&File.Exists(mp)){using var mb=new Bitmap(mp);mask=PixelBuffer.FromBitmap(mb).ExtractAlphaMask();}  
            layers.Add(new ImageLayer{  
                Id=Guid.Parse(lm.Id),Name=lm.Name,Visible=lm.Visible,Opacity=lm.Opacity,  
                BlendMode=(LayerBlendMode)lm.BlendMode,IsBackground=lm.IsBackground,  
                Transform=new LayerTransform{X=lm.X,Y=lm.Y,Width=lm.Width,Height=lm.Height,  
                    Rotation=lm.Rotation,FlipX=lm.FlipX,FlipY=lm.FlipY},  
                Pixels=pix,Mask=mask  
            });  
        }  
        var doc=new CanvasDocument{Width=meta.Width,Height=meta.Height,  
            ActiveLayerId=Guid.Parse(meta.ActiveLayerId),Layers=layers};  
        return new DocEditorSession(doc);  
    }  
}