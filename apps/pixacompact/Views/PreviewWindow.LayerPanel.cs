using System;  
using System.Collections.Generic;  
using System.Linq;  
using Avalonia.Controls;  
using Avalonia.Interactivity;  
using Avalonia.Media.Imaging;  
using PixelcutCompact.Services.Editing;  
using PixelcutCompact.Services.Editing.Document;  
namespace PixelcutCompact.Views;  
public partial class PreviewWindow  
{  
    internal DocEditorSession? _docSession;  
    private bool _layerPanelWired;  
    private void EnsureLayerPanelWired()  
    {  
        if(_layerPanelWired)return;  
        _layerPanelWired=true;  
        var btnAdd=this.FindControl<Button>("BtnLayerAdd");if(btnAdd is{} b) b.Click+=OnLayerAdd;  
        if(this.FindControl<Button>("BtnLayerDelete") is{} bd) bd.Click+=OnLayerDelete;  
        if(this.FindControl<Button>("BtnLayerMerge") is{} bm) bm.Click+=OnLayerMerge;  
        if(this.FindControl<Button>("BtnLayerDup") is{} dup) dup.Click+=OnLayerDuplicate;  
    }  
    internal void InitDocSession(DocEditorSession session)  
    {  
        _docSession?.Dispose();  
        _docSession=session;  
        _docSession.DocumentChanged+=()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{  
            RefreshLayerPanel();RefreshDocCanvas();  
        });  
        EnsureLayerPanelWired();RefreshLayerPanel();  
    }  
    private void RefreshLayerPanel()  
    {  
        if(_docSession==null)return;  
        var list=this.FindControl<ItemsControl>("LayerList");  
        if(list==null)return;  
        var items=_docSession.Layers.Select(l=>new LayerViewModel(l,_docSession.ActiveLayerId,this)).ToList();  
        list.ItemsSource=items;  
    }  
    private void RefreshDocCanvas()  
    {  
        if(_docSession==null)return;  
        var img=this.FindControl<Image>("ImgResult");  
        if(img!=null)img.Source=_docSession.GetComposite();  
    }  
    private void OnLayerAdd(object? s,RoutedEventArgs e)  
    { 
        if(_docSession==null)return;  
        var doc=_docSession.Document;  
        var layer=new ImageLayer{Name="New Layer",Pixels=new PixelBuffer(doc.Width,doc.Height),  
            Transform=LayerTransform.Default(doc.Width,doc.Height)};  
        _docSession.AddLayer(layer);_docSession.SetActiveLayer(layer.Id);  
    }  
    private void OnLayerDelete(object? s,RoutedEventArgs e)=>_docSession?.RemoveLayer(_docSession.ActiveLayerId);  
    private void OnLayerMerge(object? s,RoutedEventArgs e)=>_docSession?.MergeDown();  
    private void OnLayerDuplicate(object? s,RoutedEventArgs e)  
    {  
        if(_docSession?.ActiveLayer is not{} src)return;  
        var copy=src.Clone();  
        _docSession.AddLayer(copy,_docSession.Document.Layers.Count,"Duplicate Layer");  
    }  
}  
public class LayerViewModel  
{  
    public ImageLayer Layer{get;}  
    public bool IsActive{get;}  
    public string Name=>Layer.Name;  
    public bool Visible=>Layer.Visible;  
    public double Opacity=>Layer.Opacity/255.0*100;  
    public Bitmap? Thumbnail=>Layer.GetThumbnail(64);  
    private readonly PreviewWindow _win;  
    public LayerViewModel(ImageLayer layer,Guid activeId,PreviewWindow win)  
    {Layer=layer;IsActive=layer.Id==activeId;_win=win;}  
    public void Select()=>_win._docSession?.SetActiveLayer(Layer.Id);  
    public void ToggleVisible()=>_win._docSession?.SetLayerVisibility(Layer.Id,!Layer.Visible);  
}