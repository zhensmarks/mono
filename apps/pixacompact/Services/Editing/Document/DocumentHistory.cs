using System;  
using System.Collections.Generic;  
namespace PixelcutCompact.Services.Editing.Document;  
public sealed class HistoryEntry  
{  
    public required CanvasDocument Document{get;init;}  
    public required string Label{get;init;}  
    public DateTime Timestamp{get;init;}=DateTime.UtcNow;  
}  
public sealed class DocumentHistory  
{  
    private readonly int _maxSteps;  
    private readonly List<HistoryEntry> _undo=new();  
    private readonly List<HistoryEntry> _redo=new();  
    public DocumentHistory(int maxSteps=50)=>_maxSteps=maxSteps;  
    public bool CanUndo=>_undo.Count>0;  
    public bool CanRedo=>_redo.Count>0;  
    public IReadOnlyList<HistoryEntry> UndoStack=>_undo;  
    public void Push(CanvasDocument doc,string label)  
    {  
        _undo.Add(new HistoryEntry{Document=doc.Clone(),Label=label});  
        while(_undo.Count>_maxSteps)_undo.RemoveAt(0);  
        _redo.Clear();  
    }  
    public CanvasDocument? Undo(CanvasDocument current)  
    {  
        if(_undo.Count==0)return null;  
        var e=_undo[_undo.Count-1];_undo.RemoveAt(_undo.Count-1);  
        _redo.Add(new HistoryEntry{Document=current.Clone(),Label=e.Label});  
        return e.Document.Clone();  
    }  
    public CanvasDocument? Redo(CanvasDocument current)  
    {  
        if(_redo.Count==0)return null;  
        var e=_redo[_redo.Count-1];_redo.RemoveAt(_redo.Count-1);  
        _undo.Add(new HistoryEntry{Document=current.Clone(),Label=e.Label});  
        return e.Document.Clone();  
    }  
    public CanvasDocument JumpTo(int idx,CanvasDocument current)  
    {  
        if(idx<0||idx>=_undo.Count)return current;  
        return _undo[idx].Document.Clone();  
    }  
    public void Clear(){_undo.Clear();_redo.Clear();}  
}  
