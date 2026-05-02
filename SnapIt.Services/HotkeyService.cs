using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SnapIt.Services.Contracts;

namespace SnapIt.Services;

public class HotkeyService : IHotkeyService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private enum HotkeyId
    {
        CycleLayouts = 1,
        StartStop = 2,
        MoveLeft = 3,
        MoveRight = 4,
        MoveUp = 5,
        MoveDown = 6
    }

    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly ISettingService settingService;
    private HwndSource? hwndSource;
    private IntPtr hwnd;

    public HotKey CycleLayoutsHotKey { get; set; } = new();
    public HotKey StartStopHotKey { get; set; } = new();
    public HotKey MoveLeftHotKey { get; set; } = new();
    public HotKey MoveRightHotKey { get; set; } = new();
    public HotKey MoveUpHotKey { get; set; } = new();
    public HotKey MoveDownHotKey { get; set; } = new();
    public bool IsInitialized { get; private set; }

    public event EventHandler<HotKeyPressedEventArgs>? KeyPressed;

    public HotkeyService(ISettingService settingService)
    {
        this.settingService = settingService;
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized)
            return;

        await settingService.InitializeAsync();

        CycleLayoutsHotKey = HotKeyFromString(settingService.Settings.CycleLayoutsShortcut);
        StartStopHotKey = HotKeyFromString(settingService.Settings.StartStopShortcut);
        MoveLeftHotKey = HotKeyFromString(settingService.Settings.MoveLeftShortcut);
        MoveRightHotKey = HotKeyFromString(settingService.Settings.MoveRightShortcut);
        MoveUpHotKey = HotKeyFromString(settingService.Settings.MoveUpShortcut);
        MoveDownHotKey = HotKeyFromString(settingService.Settings.MoveDownShortcut);

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Rejtett üzenetablak létrehozása a WM_HOTKEY fogadásához
            var helperWindow = new Window
            {
                Width = 0, Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            helperWindow.Show();
            helperWindow.Hide();

            hwnd = new WindowInteropHelper(helperWindow).Handle;
            hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(WndProc);
        });

        RegisterAll();

        IsInitialized = true;
    }

    public void RegisterStartStopHotkey()
    {
        UnregisterHotKey(hwnd, (int)HotkeyId.StartStop);
        RegisterSingle(HotkeyId.StartStop, StartStopHotKey);
    }

    public void Dispose()
    {
        UnregisterAll();
        hwndSource?.RemoveHook(WndProc);
        hwndSource?.Dispose();
        IsInitialized = false;
    }

    private void RegisterAll()
    {
        RegisterSingle(HotkeyId.CycleLayouts, CycleLayoutsHotKey);
        RegisterSingle(HotkeyId.StartStop, StartStopHotKey);
        RegisterSingle(HotkeyId.MoveLeft, MoveLeftHotKey);
        RegisterSingle(HotkeyId.MoveRight, MoveRightHotKey);
        RegisterSingle(HotkeyId.MoveUp, MoveUpHotKey);
        RegisterSingle(HotkeyId.MoveDown, MoveDownHotKey);
    }

    private void UnregisterAll()
    {
        foreach (HotkeyId id in Enum.GetValues<HotkeyId>())
            UnregisterHotKey(hwnd, (int)id);
    }

    private void RegisterSingle(HotkeyId id, HotKey hotKey)
    {
        if (hotKey.Key == Key.None)
            return;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(hotKey.Key);
        var mod = ToNativeModifiers(hotKey.Modifiers) | MOD_NOREPEAT;

        // Ha már regisztrálva van, előbb töröljük
        UnregisterHotKey(hwnd, (int)id);
        RegisterHotKey(hwnd, (int)id, mod, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = (HotkeyId)wParam.ToInt32();
            var hotKey = id switch
            {
                HotkeyId.CycleLayouts => CycleLayoutsHotKey,
                HotkeyId.StartStop => StartStopHotKey,
                HotkeyId.MoveLeft => MoveLeftHotKey,
                HotkeyId.MoveRight => MoveRightHotKey,
                HotkeyId.MoveUp => MoveUpHotKey,
                HotkeyId.MoveDown => MoveDownHotKey,
                _ => null
            };

            if (hotKey != null)
            {
                KeyPressed?.Invoke(this, new HotKeyPressedEventArgs(hotKey));
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = MOD_NONE;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
        return result;
    }

    private static HotKey HotKeyFromString(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return new HotKey();

        var keys = hotkey.Split('+');

        if (!Enum.TryParse(keys[^1].Trim(), ignoreCase: true, out Key regularKey))
            return new HotKey();

        ModifierKeys modifierKeys = ModifierKeys.None;

        for (int i = 0; i < keys.Length - 1; i++)
        {
            var part = keys[i].Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifierKeys |= ModifierKeys.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifierKeys |= ModifierKeys.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifierKeys |= ModifierKeys.Shift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
                modifierKeys |= ModifierKeys.Windows;
        }

        return new HotKey(regularKey, modifierKeys);
    }
}
