using System;  
using Avalonia;  
namespace PixelcutCompact.Services.Editing.Document;  
public sealed class LayerTransform  
{  
    public double X{get;init;}  
    public double Y{get;init;}  
    public double Width{get;init;}  
    public double Height{get;init;}  
    public double Rotation{get;init;}  
    public bool FlipX{get;init;}  
    public bool FlipY{get;init;}  
    public static LayerTransform Default(int w,int h)=>new(){X=0,Y=0,Width=w,Height=h};  
    public LayerTransform Clone()=>new(){  
        X=X,Y=Y,Width=Width,Height=Height,Rotation=Rotation,FlipX=FlipX,FlipY=FlipY};  
    public Rect Bounds=>new(X,Y,Width,Height);  
    public Point Center=>new(X+Width/2,Y+Height/2);  
}  
