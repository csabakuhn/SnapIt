using System.Windows.Input;

namespace SnapIt.Services;

public record HotKey(Key Key = Key.None, ModifierKeys Modifiers = ModifierKeys.None)
{
    public override string ToString() => $"{Modifiers}+{Key}";
}

public class HotKeyPressedEventArgs : EventArgs
{
    public HotKey HotKey { get; }
    public HotKeyPressedEventArgs(HotKey hotKey) => HotKey = hotKey;
}
