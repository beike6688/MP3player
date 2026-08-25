using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Mp3Player.Services;

public enum HotkeyAction
{
    PlayPause,
    Next,
    Prev
}

/// <summary>
/// 注册全局快捷键（媒体键 + Ctrl+Alt 组合键）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint VK_P = 0x50;
    private const uint VK_N = 0x4E;
    private const uint VK_B = 0x42;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private HwndSource? _source;
    private IntPtr _handle;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private int _nextId = 1;
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _hookProc;

    public event Action<HotkeyAction>? Pressed;

    public void Register(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        TryRegister(VK_P, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, HotkeyAction.PlayPause);
        TryRegister(VK_N, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, HotkeyAction.Next);
        TryRegister(VK_B, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, HotkeyAction.Prev);
        InstallKeyboardHook();
    }

    private void InstallKeyboardHook()
    {
        _hookProc = HookCallback;
        using var proc = Process.GetCurrentProcess();
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                HotkeyAction? action = data.vkCode switch
                {
                    VK_MEDIA_PLAY_PAUSE => HotkeyAction.PlayPause,
                    VK_MEDIA_NEXT_TRACK => HotkeyAction.Next,
                    VK_MEDIA_PREV_TRACK => HotkeyAction.Prev,
                    _ => null
                };
                if (action != null)
                {
                    Pressed?.Invoke(action.Value);
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void TryRegister(uint vk, uint mods, HotkeyAction action)
    {
        int id = _nextId++;
        if (RegisterHotKey(_handle, id, mods, vk))
            _registered[id] = action;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            if (_registered.TryGetValue(wParam.ToInt32(), out var action))
                Pressed?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            foreach (var id in _registered.Keys)
                UnregisterHotKey(_handle, id);
            _registered.Clear();
            _source = null;
        }
    }
}
