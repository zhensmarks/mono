using System;  
using System.Collections.Generic;  
using System.Linq;  
using Avalonia.Media.Imaging;  
namespace PixelcutCompact.Services.Editing.Document;  
public sealed class DocEditorSession:IDisposable  
{  
    private CanvasDocument _doc;  
    private readonly DocumentHistory _history;  
    private WriteableBitmap? _compositeCache;  
    private bool _compositeDirty=true;  
    public bool IsDirty{get;private set;}  
    public CanvasDocument Document=>_doc;  
    public IReadOnlyList<ImageLayer> Layers=>_doc.Layers;  
    public ImageLayer? ActiveLayer=>_doc.ActiveLayer;  
    public Guid ActiveLayerId=>_doc.ActiveLayerId;  
    public bool CanUndo=>_history.CanUndo;  
    public bool CanRedo=>_history.CanRedo;  
    public IReadOnlyList<HistoryEntry> UndoStack=>_history.UndoStack;  
    public event Action? DocumentChanged;  
    public DocEditorSession(CanvasDocument doc,int maxSteps=50)  
    {  
        _doc=doc??throw new ArgumentNullException(nameof(doc));  
        _history=new DocumentHistory(maxSteps);  
    }  
    public static DocEditorSession FromPixelBuffer(PixelBuffer pixels,string name="Layer 1")  
        =>new(CanvasDocument.FromSingle(pixels,name)); 
    private void Mutate(CanvasDocument newDoc,string label,bool pushHistory=true)  
    {  
        if(pushHistory)_history.Push(_doc,label);  
        _doc=newDoc;_compositeDirty=true;IsDirty=true;  
        DocumentChanged?.Invoke();  
    }  
    public void SetActiveLayer(Guid id)=>Mutate(_doc.WithActiveLayer(id),"Select Layer",pushHistory:false);  
    public void UpdateLayer(ImageLayer updated,string label)=>Mutate(_doc.ReplaceLayer(updated),label);  
    public void SetLayerVisibility(Guid id,bool visible)  
    {  
        var l=_doc.Layers.FirstOrDefault(x=>x.Id==id);  
        if(l==null)return;  
        Mutate(_doc.ReplaceLayer(l.With(visible:visible)),"Toggle Visibility");  
    }  
    public void SetLayerOpacity(Guid id,byte opacity)  
    {  
        var l=_doc.Layers.FirstOrDefault(x=>x.Id==id);  
        if(l==null)return;  
        Mutate(_doc.ReplaceLayer(l.With(opacity:opacity)),"Set Opacity");  
    }  
    public void SetLayerBlendMode(Guid id,LayerBlendMode mode)  
    {  
        var l=_doc.Layers.FirstOrDefault(x=>x.Id==id);  
        if(l==null)return;  
        Mutate(_doc.ReplaceLayer(l.With(blend:mode)),"Set Blend Mode");  
    }  
    public void AddLayer(ImageLayer layer,int index=-1,string label="Add Layer")=>Mutate(_doc.AddLayer(layer,index),label); 
    public void RemoveLayer(Guid id,string label="Delete Layer")  
    {  
        if(_doc.Layers.Count<=1)return;  
        Mutate(_doc.RemoveLayer(id),label);  
    }  
    public void ReorderLayers(IReadOnlyList<ImageLayer> newOrder,string label="Reorder")=>Mutate(_doc.WithLayers(newOrder),label);  
    public void MergeDown()  
    {  
        int idx=_doc.Layers.ToList().FindIndex(l=>l.Id==_doc.ActiveLayerId);  
        if(idx<=0)return;  
        var comp=LayerCompositor.Composite(_doc);  
        var bot=_doc.Layers[idx-1];  
        var merged=new ImageLayer{Name=bot.Name,Pixels=comp,Transform=LayerTransform.Default(comp.Width,comp.Height)};  
        var list=_doc.Layers.ToList();  
        list.RemoveAt(idx);list[idx-1]=merged;  
        Mutate(_doc.WithLayers(list),"Merge Down");  
    }  
    public void Undo(){var r=_history.Undo(_doc);if(r!=null)Mutate(r,"Undo",pushHistory:false);}  
    public void Redo(){var r=_history.Redo(_doc);if(r!=null)Mutate(r,"Redo",pushHistory:false);}  
    public void JumpTo(int idx){var r=_history.JumpTo(idx,_doc);if(r!=null)Mutate(r,"Jump",pushHistory:false);}  
    public WriteableBitmap GetComposite()
    {
        if(!_compositeDirty&&_compositeCache!=null)return _compositeCache;
        // Jangan Dispose cache lama di sini: bitmap bisa masih terpasang sebagai
        // Image.Source dan Dispose akan memicu NullReferenceException di get_Size().
        // Biarkan GC mengambilnya; Dispose() tetap membersihkan saat sesi berakhir.
        _compositeCache=LayerCompositor.Render(_doc);
        _compositeDirty=false;return _compositeCache;
    }
    public void MarkSaved()=>IsDirty=false;  
    public void Dispose(){_compositeCache?.Dispose();_history.Clear();}  
}