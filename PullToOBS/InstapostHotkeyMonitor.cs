using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;

namespace PullToOBS;

public sealed class InstapostHotkeyMonitor
{
    private readonly IKeyState _keyState;
    private bool _wasHotkeyDown;
    private bool _suppressUntilRelease;

    public InstapostHotkeyMonitor(IKeyState keyState)
    {
        _keyState = keyState;
    }

    public bool Update(int keyCode, bool ctrl, bool shift, bool alt)
    {
        var hotkeyDown = IsHotkeyDown(keyCode, ctrl, shift, alt);

        if (_suppressUntilRelease)
        {
            _wasHotkeyDown = hotkeyDown;
            if (!hotkeyDown)
                _suppressUntilRelease = false;

            return false;
        }

        var justPressed = hotkeyDown && !_wasHotkeyDown;
        _wasHotkeyDown = hotkeyDown;
        return justPressed;
    }

    public void SuppressUntilReleased()
    {
        _suppressUntilRelease = true;
    }

    public void Reset()
    {
        _wasHotkeyDown = false;
        _suppressUntilRelease = false;
    }

    internal bool IsHotkeyDown(int keyCode, bool ctrl, bool shift, bool alt)
    {
        var key = InstapostHotkey.FromConfigValue(keyCode);
        if (key == VirtualKey.NO_KEY || !_keyState.IsVirtualKeyValid(key))
            return false;

        if (!_keyState[key])
            return false;

        return InstapostHotkey.IsAnyCtrlDown(_keyState) == ctrl
               && InstapostHotkey.IsAnyShiftDown(_keyState) == shift
               && InstapostHotkey.IsAnyAltDown(_keyState) == alt;
    }
}
