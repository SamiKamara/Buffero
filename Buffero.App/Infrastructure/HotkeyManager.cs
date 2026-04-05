using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private readonly string _registrationLabel;
    private readonly int _primaryHotkeyId;
    private readonly int _altGrHotkeyId;
    private HwndSource? _source;
    private IntPtr _handle;
    private HotkeyBinding? _binding = HotkeyBinding.Default;

    public HotkeyManager(string registrationLabel, int primaryHotkeyId, int altGrHotkeyId)
    {
        _registrationLabel = registrationLabel;
        _primaryHotkeyId = primaryHotkeyId;
        _altGrHotkeyId = altGrHotkeyId;
        RegistrationMessage = $"{_registrationLabel} is not registered.";
    }

    public event Action? Pressed;

    public event Action<bool, string>? RegistrationChanged;

    public bool IsRegistered { get; private set; }

    public string RegistrationMessage { get; private set; }

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public void UpdateBinding(HotkeyBinding? binding)
    {
        _binding = binding is null
            ? null
            : new HotkeyBinding
        {
            Ctrl = binding.Ctrl,
            Alt = binding.Alt,
            Shift = binding.Shift,
            Key = binding.Key
        };

        _binding?.Normalize();
        TryRegisterCurrentHotkey();
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, _primaryHotkeyId);
            UnregisterHotKey(_handle, _altGrHotkeyId);
        }

        _source?.RemoveHook(WndProc);
    }

    private void TryRegisterCurrentHotkey()
    {
        if (_handle == IntPtr.Zero)
        {
            IsRegistered = false;
            RegistrationMessage = $"{_registrationLabel} could not be registered because the window handle is unavailable.";
            RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
            return;
        }

        UnregisterHotKey(_handle, _primaryHotkeyId);
        UnregisterHotKey(_handle, _altGrHotkeyId);

        if (_binding is null)
        {
            IsRegistered = false;
            RegistrationMessage = $"{_registrationLabel} is not registered.";
            RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
            return;
        }

        var modifiers = (_binding.Ctrl ? ModControl : 0)
                        | (_binding.Alt ? ModAlt : 0)
                        | (_binding.Shift ? ModShift : 0);
        var virtualKey = GetVirtualKey(_binding.Key);

        if (!RegisterHotKey(_handle, _primaryHotkeyId, modifiers, virtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            IsRegistered = false;
            RegistrationMessage = error == 1409
                ? $"{_registrationLabel} unavailable: {_binding.ToDisplayString()} is already in use."
                : $"{_registrationLabel} unavailable: failed to register {_binding.ToDisplayString()} (Win32 {error}).";
            RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
            return;
        }

        var altGrRegistered = TryRegisterAltGrVariant(modifiers, virtualKey);
        IsRegistered = true;
        RegistrationMessage = altGrRegistered
            ? $"{_registrationLabel} ready: {_binding.ToDisplayString()} (Right Alt supported)"
            : $"{_registrationLabel} ready: {_binding.ToDisplayString()}";
        RegistrationChanged?.Invoke(IsRegistered, RegistrationMessage);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey
            && (wParam.ToInt32() == _primaryHotkeyId || wParam.ToInt32() == _altGrHotkeyId))
        {
            handled = true;
            Pressed?.Invoke();
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
        var binding = _binding;
        if (binding is null || !binding.Alt || binding.Ctrl)
        {
            return false;
        }

        var altGrModifiers = primaryModifiers | ModControl;
        return RegisterHotKey(_handle, _altGrHotkeyId, altGrModifiers, virtualKey);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
