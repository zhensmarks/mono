using System;  
namespace PixelcutCompact.Services.Editing.Document;  
public enum LayerBlendMode  
{  
    Normal=0,Multiply=1,Screen=2,Overlay=3,  
    SoftLight=4,HardLight=5,ColorDodge=6,ColorBurn=7,  
    Darken=8,Lighten=9,Difference=10,Exclusion=11,  
    Hue=12,Saturation=13,Color=14,Luminosity=15  
} 
public static class BlendMath  
{  
    public static void Blend(byte[] dst,int di,byte[] src,int si,LayerBlendMode mode,byte opacity,byte[]? mask,int mi)  
    {  
        double a=(opacity/255.0)*(mask!=null&&mi<mask.Length?mask[mi]/255.0:1.0);  
        if(a<=0)return;  
        double sr=src[si+2],sg=src[si+1],sb=src[si],sa=src[si+3]*a;  
        double dr=dst[di+2],dg=dst[di+1],db=dst[di],da=dst[di+3];  
        double fr,fg,fb;  
        switch(mode){  
            case LayerBlendMode.Multiply:fr=sr*dr/255;fg=sg*dg/255;fb=sb*db/255;break;  
            case LayerBlendMode.Screen:fr=255-(255-sr)*(255-dr)/255;fg=255-(255-sg)*(255-dg)/255;fb=255-(255-sb)*(255-db)/255;break;  
            case LayerBlendMode.Overlay:fr=Ovl(dr,sr);fg=Ovl(dg,sg);fb=Ovl(db,sb);break;  
            case LayerBlendMode.SoftLight:fr=SL(dr,sr);fg=SL(dg,sg);fb=SL(db,sb);break;  
            case LayerBlendMode.HardLight:fr=Ovl(sr,dr);fg=Ovl(sg,dg);fb=Ovl(sb,db);break;  
            case LayerBlendMode.ColorDodge:fr=Dg(dr,sr);fg=Dg(dg,sg);fb=Dg(db,sb);break;  
            case LayerBlendMode.ColorBurn:fr=Bn(dr,sr);fg=Bn(dg,sg);fb=Bn(db,sb);break;  
            case LayerBlendMode.Darken:fr=Math.Min(dr,sr);fg=Math.Min(dg,sg);fb=Math.Min(db,sb);break;  
            case LayerBlendMode.Lighten:fr=Math.Max(dr,sr);fg=Math.Max(dg,sg);fb=Math.Max(db,sb);break;  
            case LayerBlendMode.Difference:fr=Math.Abs(dr-sr);fg=Math.Abs(dg-sg);fb=Math.Abs(db-sb);break;  
            case LayerBlendMode.Exclusion:fr=dr+sr-2*dr*sr/255;fg=dg+sg-2*dg*sg/255;fb=db+sb-2*db*sb/255;break;  
            default:fr=sr;fg=sg;fb=sb;break;  
        }  
        double as2=sa/255.0,ad=da/255.0,ao=as2+ad*(1-as2);  
        if(ao<=0)return;  
        dst[di+2]=Cl((fr*as2+dr*ad*(1-as2))/ao);  
        dst[di+1]=Cl((fg*as2+dg*ad*(1-as2))/ao);  
        dst[di]=Cl((fb*as2+db*ad*(1-as2))/ao);  
        dst[di+3]=Cl(ao*255); 
    }  
    static double Ovl(double a,double b)=>a<128?2*a*b/255:255-2*(255-a)*(255-b)/255;  
    static double SL(double a,double b){double an=a/255,bn=b/255;if(bn<=0.5)return(2*an*bn+an*an*(1-2*bn))*255;double d=an<=0.25?((16*an-12)*an+4)*an:Math.Sqrt(an);return(2*an*(1-bn)+d*(2*bn-1))*255;}  
    static double Dg(double a,double b)=>b>=255?255:Math.Min(255,a*255/(255-b));  
    static double Bn(double a,double b)=>b<=0?0:Math.Max(0,255-(255-a)*255/b);  
    static byte Cl(double v)=>(byte)Math.Clamp((int)(v+0.5),0,255);  
}  
