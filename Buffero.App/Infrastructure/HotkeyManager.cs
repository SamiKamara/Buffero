using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public sealed class HotkeyManager : IDisposable
{
    private const int PrimaryHotkeyId = 0xB00F;
    private const int AltGrHotkeyId = 0xB010;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private HwndSource? _source;
    private IntPtr _handle;
    private HotkeyBinding _binding = HotkeyBinding.Default;

    public event Action? SaveReplayPressed;

    public event Action<bool, string>? RegistrationChanged;

    public bool IsRegistered { get; private set; }

    public string RegistrationMessage { get; private set; } = "Save hotkey is not registered.";

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public void UpdateBinding(HotkeyBinding binding)
    {
        _binding = new HotkeyBinding
        {
            Ctrl = binding.Ctrl,
            Alt = binding.Alt,
            Shift = binding.Shift,
            Key = binding.Key
        };

        _binding.Normalize();
        TryRegisterCurrentHotkey();
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, PrimaryHotkeyId);
            UnregisterHotKey(_handle, AltGrHotkeyId);
        }

        _source?.RemoveHook(WndProc);
    }

    private void TryRegisterCurrentHotkey()
    {
        if (_handle == IntPtr.Zero)
        {
            IsRegistered = false;
            RegistrationMessage = "Save hotkey could not be registered because the window handle is unavailable.";
            RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
            return;
        }

        UnregisterHotKey(_handle, PrimaryHotkeyId);
        UnregisterHotKey(_handle, AltGrHotkeyId);

        var modifiers = (_binding.Ctrl ? ModControl : 0)
                        | (_binding.Alt ? ModAlt : 0)
                        | (_binding.Shift ? ModShift : 0);
        var virtualKey = GetVirtualKey(_binding.Key);

        if (!RegisterHotKey(_handle, PrimaryHotkeyId, modifiers, virtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            IsRegistered = false;
            RegistrationMessage = error == 1409
                ? $"Save hotkey unavailable: {_binding.ToDisplayString()} is already in use."
                : $"Save hotkey unavailable: failed to register {_binding.ToDisplayString()} (Win32 {error}).";
            RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
            return;
        }

        var altGrRegistered = TryRegisterAltGrVariant(modifiers, virtualKey);
        IsRegistered = true;
        RegistrationMessage = altGrRegistered
            ? $"Save hotkey ready: {_binding.ToDisplayString()} (Right Alt supported)"
            : $"Save hotkey ready: {_binding.ToDisplayString()}";
        RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey
            && (wParam.ToInt32() == PrimaryHotkeyId || wParam.ToInt32() == AltGrHotkeyId))
        {
            handled = true;
            SaveReplayPressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    private static uint GetVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= 'A' and <= 'Z')
            {
                return character;
            }
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            return (uint)(0x6F + functionKey);
        }

        return 'P';
    }

    private bool TryRegisterAltGrVariant(uint primaryModifiers, uint virtualKey)
    {
        if (!_binding.Alt || _binding.Ctrl)
        {
            return false;
        }

        var altGrModifiers = primaryModifiers | ModControl;
        return RegisterHotKey(_handle, AltGrHotkeyId, altGrModifiers, virtualKey);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
