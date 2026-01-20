using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using BMachine.UI.Models;

namespace BMachine.UI.Services;

public class GlobalInputHookService : IDisposable
{
    // Events
    public event Action<Point>? OnTriggerDown;
    public event Action<Point>? OnTriggerUp;
    public event Action<Point>? OnMouseMove;
    
    // Config
    private TriggerConfig _config = new TriggerConfig(); // Default: Shift + Middle Mouse

    // Hooks
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    
    // Mouse Messages
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    // Keyboard Messages
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private LowLevelProc _mouseProc;
    private IntPtr _mouseHookID = IntPtr.Zero;
    
    private LowLevelProc _keyboardProc;
    private IntPtr _keyboardHookID = IntPtr.Zero;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private bool _isInteracting = false;

    // Recording Mode
    public bool IsRecording { get; set; } = false;
    public event Action<TriggerConfig>? OnRecorded;

    public GlobalInputHookService()
    {
        _mouseProc = MouseHookCallback;
        _mouseHookID = SetHook(WH_MOUSE_LL, _mouseProc);
        
        _keyboardProc = KeyboardHookCallback;
        _keyboardHookID = SetHook(WH_KEYBOARD_LL, _keyboardProc);
    }

    public void UpdateConfig(TriggerConfig config)
    {
        _config = config;
        // Reset state
        _isInteracting = false;
    }

    private IntPtr SetHook(int idHook, LowLevelProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(idHook, proc, GetModuleHandle(curModule?.ModuleName), 0);
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            Point currentPos = new Point(hookStruct.pt.x, hookStruct.pt.y);

            // 1. Mouse Move (Always pass through, but notify for Radial Menu UI)
            if (msg == WM_MOUSEMOVE)
            {
                if (_isInteracting)
                {
                    OnMouseMove?.Invoke(currentPos);
                }
            }
            // 2. Trigger Check
            else
            {
                // Recording Logic
                if (IsRecording && IsMouseDownMessage(msg))
                {
                    // Identify Button
                    int button = GetMouseClickButton(msg, hookStruct.mouseData);
                    if (button != -1)
                    {
                        var config = new TriggerConfig
                        {
                            Type = TriggerType.Mouse,
                            MouseButton = button,
                            Modifiers = GetCurrentModifiers()
                        };
                        OnRecorded?.Invoke(config);
                        return (IntPtr)1; // Swallow
                    }
                }
                
                // Normal Trigger Logic
                if (_config.Type == TriggerType.Mouse)
                {
                    int targetDown = GetMouseDownMessageFor(_config.MouseButton);
                    int targetUp = GetMouseUpMessageFor(_config.MouseButton);
                    
                    if (msg == targetDown)
                    {
                        // Check XButton Data if needed
                        if (_config.MouseButton >= 3) // X1 or X2
                        {
                            int xButtonId = (int)(hookStruct.mouseData >> 16);
                            if ((_config.MouseButton == 3 && xButtonId != 1) || 
                                (_config.MouseButton == 4 && xButtonId != 2))
                            {
                                return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
                            }
                        }

                        if (CheckModifiers())
                        {
                            _isInteracting = true;
                            OnTriggerDown?.Invoke(currentPos);
                            return (IntPtr)1; // Swallow
                        }
                    }
                    else if (msg == targetUp)
                    {
                        if (_isInteracting)
                        {
                            // Check XButton Data if needed
                             if (_config.MouseButton >= 3) // X1 or X2
                            {
                                int xButtonId = (int)(hookStruct.mouseData >> 16);
                                if ((_config.MouseButton == 3 && xButtonId != 1) || 
                                    (_config.MouseButton == 4 && xButtonId != 2))
                                {
                                    return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
                                }
                            }
                            
                            _isInteracting = false;
                            OnTriggerUp?.Invoke(currentPos);
                            return (IntPtr)1; // Swallow
                        }
                    }
                }
            }
        }
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vkCode = hookStruct.vkCode;
            
            bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
            bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

            // Skip modifier keys themselves from triggering recording/action as main key
            if (IsModifierKey(vkCode)) return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);

            // Recording Logic
            if (IsRecording && isDown)
            {
                var config = new TriggerConfig
                {
                    Type = TriggerType.Keyboard,
                    Key = vkCode,
                    Modifiers = GetCurrentModifiers()
                };
                OnRecorded?.Invoke(config);
                return (IntPtr)1; // Swallow
            }

            // Normal Trigger Logic
            if (_config.Type == TriggerType.Keyboard)
            {
                 if (vkCode == _config.Key)
                 {
                     if (isDown)
                     {
                         if (!_isInteracting && CheckModifiers())
                         {
                             _isInteracting = true;
                             // For keyboard trigger, we use Cursor Position
                             GetCursorPos(out POINT p);
                             OnTriggerDown?.Invoke(new Point(p.x, p.y));
                             return (IntPtr)1; 
                         }
                         else if (_isInteracting)
                         {
                             // Repeat key, swallow
                             return (IntPtr)1;
                         }
                     }
                     else if (isUp)
                     {
                         if (_isInteracting)
                         {
                             _isInteracting = false;
                             GetCursorPos(out POINT p);
                             OnTriggerUp?.Invoke(new Point(p.x, p.y));
                             return (IntPtr)1;
                         }
                     }
                 }
            }
        }
        return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }
    
    private bool CheckModifiers()
    {
        int currentMods = GetCurrentModifiers();
        // Exact match required? Or assume subset? 
        // Typically modifiers must match config.
        return currentMods == _config.Modifiers;
    }

    private int GetCurrentModifiers()
    {
        int mods = 0;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) mods |= 1; // Alt
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) mods |= 2; // Ctrl
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) mods |= 4; // Shift
        if ((GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0) mods |= 8; // Win
        return mods;
    }

    private bool IsModifierKey(int vkCode)
    {
        return vkCode == VK_SHIFT || vkCode == 0xA0 || vkCode == 0xA1 || // Shift L/R
               vkCode == VK_CONTROL || vkCode == 0xA2 || vkCode == 0xA3 || // Ctrl L/R
               vkCode == VK_MENU || vkCode == 0xA4 || vkCode == 0xA5 || // Alt L/R
               vkCode == VK_LWIN || vkCode == VK_RWIN;
    }

    private int GetMouseDownMessageFor(int button)
    {
        return button switch
        {
            0 => WM_LBUTTONDOWN, // Left
            1 => WM_RBUTTONDOWN, // Right
            2 => WM_MBUTTONDOWN, // Middle
            3 => WM_XBUTTONDOWN,
            4 => WM_XBUTTONDOWN,
            _ => WM_MBUTTONDOWN
        };
    }
    
    private int GetMouseUpMessageFor(int button)
    {
        return button switch
        {
            0 => WM_LBUTTONUP,
            1 => WM_RBUTTONUP,
            2 => WM_MBUTTONUP,
            3 => WM_XBUTTONUP,
            4 => WM_XBUTTONUP,
            _ => WM_MBUTTONUP
        };
    }
    
    private bool IsMouseDownMessage(int msg)
    {
        return msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || 
               msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN;
    }
    
    private int GetMouseClickButton(int msg, uint mouseData)
    {
        if (msg == WM_LBUTTONDOWN) return 0;
        if (msg == WM_RBUTTONDOWN) return 1;
        if (msg == WM_MBUTTONDOWN) return 2;
        if (msg == WM_XBUTTONDOWN)
        {
             int xButtonId = (int)(mouseData >> 16);
             if (xButtonId == 1) return 3;
             if (xButtonId == 2) return 4;
        }
        return -1;
    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_mouseHookID);
        UnhookWindowsHookEx(_keyboardHookID);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern short GetKeyState(int nVirtKey);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
