using System;
using System.Threading.Tasks;
using Avalonia;
using SharpHook;
using SharpHook.Native;
using SharpHook.Native;

namespace BMachine.UI.Services;

public class GlobalInputHookService : IDisposable
{
    // Events
    public event Action<Point>? OnTriggerDown;
    public event Action<Point>? OnTriggerUp;
    public event Action<Point>? OnMouseMove;
    
    // Config
    private Models.TriggerConfig _config = new Models.TriggerConfig(); // Default: Shift + Middle Mouse
    
    // Hook
    private readonly TaskPoolGlobalHook _hook;
    private bool _isInteracting = false;
    private ModifierMask _currentModifiers = ModifierMask.None;

    // Recording Mode
    public bool IsRecording { get; set; } = false;
    public event Action<Models.TriggerConfig>? OnRecorded;

    public GlobalInputHookService()
    {
        // Initialize TaskPoolGlobalHook (runs callbacks on background threads)
        _hook = new TaskPoolGlobalHook();

        // Subscribe to Events
        _hook.MouseMoved += OnHookMouseMove;
        _hook.MouseDragged += OnHookMouseMove; // Handle drag too
        _hook.MousePressed += OnHookMouseDown;
        _hook.MouseReleased += OnHookMouseUp;
        _hook.KeyPressed += OnHookKeyPressed;
        _hook.KeyReleased += OnHookKeyReleased;

        // Start Hook
        _ = _hook.RunAsync();
    }

    public void UpdateConfig(Models.TriggerConfig config)
    {
        _config = config;
        _isInteracting = false;
    }

    private void OnHookMouseMove(object? sender, MouseHookEventArgs e)
    {
        var pos = new Point(e.Data.X, e.Data.Y);
        
        if (_isInteracting)
        {
             OnMouseMove?.Invoke(pos);
             // Should we suppress mouse move? Probably not needed for UI interaction usually, 
             // but if we are "capturing" input for resizing/moving, maybe?
             // Original implementation didn't suppress move.
        }
    }

    private void UpdateModifiers(KeyCode key, bool down)
    {
        var mask = ModifierMask.None;
        if (key == KeyCode.VcLeftShift || key == KeyCode.VcRightShift) mask = ModifierMask.Shift;
        else if (key == KeyCode.VcLeftControl || key == KeyCode.VcRightControl) mask = ModifierMask.Ctrl;
        else if (key == KeyCode.VcLeftAlt || key == KeyCode.VcRightAlt) mask = ModifierMask.Alt;
        else if (key == KeyCode.VcLeftMeta || key == KeyCode.VcRightMeta) mask = ModifierMask.Meta;

        if (mask != ModifierMask.None)
        {
            if (down) _currentModifiers |= mask;
            else _currentModifiers &= ~mask;
        }
    }

    private void OnHookMouseDown(object? sender, MouseHookEventArgs e)
    {
        var pos = new Point(e.Data.X, e.Data.Y);
        var button = e.Data.Button; 

        // Recording Logic
        if (IsRecording)
        {
            var btnIndex = MapSharpHookButton(button);
            if (btnIndex != -1)
            {
                var config = new Models.TriggerConfig
                {
                    Type = Models.TriggerType.Mouse,
                    MouseButton = btnIndex,
                    Modifiers = MapStepModifiers(_currentModifiers)
                };
                OnRecorded?.Invoke(config);
                e.SuppressEvent = true;
                return;
            }
        }

        // Trigger Logic
        if (_config.Type == Models.TriggerType.Mouse)
        {
            var btnIndex = MapSharpHookButton(button);
            
            if (btnIndex == _config.MouseButton)
            {
                if (CheckModifiers(_currentModifiers))
                {
                    _isInteracting = true;
                    OnTriggerDown?.Invoke(pos);
                    e.SuppressEvent = true;
                }
            }
        }
    }

    private void OnHookMouseUp(object? sender, MouseHookEventArgs e)
    {
        var pos = new Point(e.Data.X, e.Data.Y);
        var button = e.Data.Button;
        var btnIndex = MapSharpHookButton(button);

        if (_config.Type == Models.TriggerType.Mouse && btnIndex == _config.MouseButton)
        {
            if (_isInteracting)
            {
                _isInteracting = false;
                OnTriggerUp?.Invoke(pos);
                e.SuppressEvent = true;
            }
        }
    }

    private void OnHookKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var vkCode = (int)e.Data.KeyCode; // SharpHook KeyCode

        UpdateModifiers(e.Data.KeyCode, true);

        // Recording
        if (IsRecording)
        {
            if (!IsModifierKey(e.Data.KeyCode))
            {
                var config = new Models.TriggerConfig
                {
                    Type = Models.TriggerType.Keyboard,
                    Key = vkCode, // Store SharpHook KeyCode
                    Modifiers = MapStepModifiers(_currentModifiers)
                };
                OnRecorded?.Invoke(config);
                e.SuppressEvent = true;
                return;
            }
        }

        // Trigger
        if (_config.Type == Models.TriggerType.Keyboard && vkCode == _config.Key)
        {
            if (!_isInteracting && CheckModifiers(_currentModifiers))
            {
                _isInteracting = true;
                // Use current mouse pos?
                // SharpHook doesn't give mouse pos in Key event data directly used here, 
                // but we can track it or ignore pos for Init.
                // Original used GetCursorPos. We can't easily get it here without tracking in Move.
                // Let's rely on UI to query position if needed, or pass 0,0?
                // Actually OnTriggerDown expects Point.
                // We'll pass (0,0) or last known?
                // Better: UI usually needs screen pos.
                // Let's ignore pos for Keyboard trigger or fetch via Avalonia if possible?
                // Or just use (0,0) since it's keyboard.
                OnTriggerDown?.Invoke(new Point(0, 0));
                e.SuppressEvent = true;
            }
        }
    }

    private void OnHookKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        UpdateModifiers(e.Data.KeyCode, false);
        
        var vkCode = (int)e.Data.KeyCode;

        if (_config.Type == Models.TriggerType.Keyboard && vkCode == _config.Key)
        {
            if (_isInteracting)
            {
                _isInteracting = false;
                OnTriggerUp?.Invoke(new Point(0, 0));
                e.SuppressEvent = true;
            }
        }
    }

    private int MapSharpHookButton(MouseButton btn)
    {
        // 0=Left, 1=Right, 2=Middle, 3=X1, 4=X2
        return btn switch
        {
            MouseButton.Button1 => 0, // Left
            MouseButton.Button2 => 1, // Right
            MouseButton.Button3 => 2, // Middle
            MouseButton.Button4 => 3, // X1
            MouseButton.Button5 => 4, // X2
            _ => -1
        };
    }

    private int MapStepModifiers(ModifierMask mask)
    {
        // Map SharpHook mask to our Int bitmask
        // 1=Alt, 2=Ctrl, 4=Shift, 8=Win
        int mods = 0;
        if (mask.HasFlag(ModifierMask.Alt)) mods |= 1;
        if (mask.HasFlag(ModifierMask.Ctrl)) mods |= 2;
        if (mask.HasFlag(ModifierMask.Shift)) mods |= 4;
        if (mask.HasFlag(ModifierMask.Meta)) mods |= 8;
        return mods;
    }

    private bool CheckModifiers(ModifierMask mask)
    {
        // Check if current mask matches config
        var current = MapStepModifiers(mask);
        return current == _config.Modifiers;
    }

    private bool IsModifierKey(KeyCode key)
    {
        return key == KeyCode.VcLeftShift || key == KeyCode.VcRightShift ||
               key == KeyCode.VcLeftControl || key == KeyCode.VcRightControl ||
               key == KeyCode.VcLeftAlt || key == KeyCode.VcRightAlt ||
               key == KeyCode.VcLeftMeta || key == KeyCode.VcRightMeta;
    }

    public void Dispose()
    {
        _hook.Dispose();
    }
}
