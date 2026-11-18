namespace AltyaziCevirici;

/// <summary>Hidden message window receiving global hotkeys.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    /// <returns>false if the key is already taken by another program.</returns>
    public bool Register(Keys key, Action action)
    {
        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, Native.MOD_NOREPEAT, (uint)key)) return false;
        _actions[id] = action;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var a)) a();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(Handle, id);
        _actions.Clear();
        DestroyHandle();
    }
}
