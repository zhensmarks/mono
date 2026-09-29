using System;  
using System.Collections.Generic;  
using System.Linq;  
namespace PixelcutCompact.Services.Editing.Document;  
public sealed class CanvasDocument  
{  
    public int Width{get;init;}  
    public int Height{get;init;}  
    public IReadOnlyList<ImageLayer> Layers{get;init;}=Array.Empty<ImageLayer>();  
    public Guid ActiveLayerId{get;init;}  
    public CanvasDocument Clone()=>new(){  
        Width=Width,Height=Height,ActiveLayerId=ActiveLayerId,  
        Layers=Layers.Select(l=>l.Clone()).ToList()  
    };  
    public ImageLayer? ActiveLayer=>Layers.FirstOrDefault(l=>l.Id==ActiveLayerId);  
    public CanvasDocument WithLayers(IReadOnlyList<ImageLayer> layers)=>new(){  
        Width=Width,Height=Height,ActiveLayerId=ActiveLayerId,Layers=layers  
    };  
    public CanvasDocument WithActiveLayer(Guid id)=>new(){  
        Width=Width,Height=Height,ActiveLayerId=id,Layers=Layers  
    }; 
    public CanvasDocument ReplaceLayer(ImageLayer updated)  
    {  
        var list=Layers.Select(l=>l.Id==updated.Id?updated:l).ToList();  
        return WithLayers(list);  
    }  
    public CanvasDocument AddLayer(ImageLayer layer,int index=-1)  
    {  
        var list=Layers.ToList();  
        if(index<0||index>=list.Count)list.Add(layer);else list.Insert(index,layer);  
        return WithLayers(list);  
    }  
    public CanvasDocument RemoveLayer(Guid id)=>WithLayers(Layers.Where(l=>l.Id!=id).ToList());  
    public static CanvasDocument FromSingle(PixelBuffer pixels,string name="Layer 1")=>new(){  
        Width=pixels.Width,Height=pixels.Height,  
        Layers=new List<ImageLayer>{new ImageLayer{Name=name,Pixels=pixels,Transform=LayerTransform.Default(pixels.Width,pixels.Height)}},  
        ActiveLayerId=Guid.Empty  
    };  
}