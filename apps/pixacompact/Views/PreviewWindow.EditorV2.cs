using System;  
using Avalonia.Controls;  
using Avalonia.Input;  
using Avalonia.Interactivity;  
using Avalonia.Media.Imaging;  
using PixelcutCompact.Services.Editing;  
using PixelcutCompact.Services.Editing.Document;  
namespace PixelcutCompact.Views;  
public partial class PreviewWindow  
{  
    private bool _editorV2Active;  
    private bool _editorV2Wired;  
    internal void EnterDocEditMode(PixelBuffer pixels)  
    {  
        var session=DocEditorSession.FromPixelBuffer(pixels);  
        InitDocSession(session);  
        _editorV2Active=true;  
        WireDocEditorEvents();  
        // Jangan paksa panel Layers tampil: ikuti pilihan menu Window (Tab + toggle).
        ApplyEditorDockVisibility();  
    }  
    internal void ExitDocEditMode()  
    {  
        _editorV2Active=false;  
        _docSession?.Dispose();  
        _docSession=null;  
        // Visibilitas panel dikembalikan lewat ApplyEditorDockVisibility (jangan paksa).
        ApplyEditorDockVisibility();  
    }  
    private void WireDocEditorEvents(){if(_editorV2Wired)return;_editorV2Wired=true;}  
    internal bool DocEditorHandleKey(KeyEventArgs e)  
    {  
        if(!_editorV2Active||_docSession==null)return false;  
        bool ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);  
        if(ctrl&&e.Key==Key.Z){_docSession.Undo();return true;}  
        if(ctrl&&e.Key==Key.Y){_docSession.Redo();return true;}  
        if(ctrl&&e.Key==Key.S){SaveDocProject();return true;}  
        return false;  
    }  
    private void SaveDocProject()  
    {  
        if(_docSession==null)return;  
        var dlg=new Avalonia.Platform.Storage.FilePickerSaveOptions  
        {Title="Save Project",DefaultExtension="pixedit"};  
        _=StorageProvider.SaveFilePickerAsync(dlg).ContinueWith(t=>{  
            if(t.Status==System.Threading.Tasks.TaskStatus.RanToCompletion&&t.Result!=null)  
                ProjectStore.Save(_docSession,t.Result.Path.LocalPath);  
        },System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());  
    }  
}