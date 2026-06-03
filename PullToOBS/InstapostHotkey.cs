using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;

namespace PullToOBS;

public enum InstapostHotkeyCaptureActionKind
{
    None,
    Cancel,
    Clear,
    Set,
}

public readonly record struct InstapostHotkeyCaptureAction(
    InstapostHotkeyCaptureActionKind Kind,
    VirtualKey Key,
    bool Ctrl,
    bool Shift,
    bool Alt);

public static class InstapostHotkey
{
    public static IReadOnlyList<VirtualKey> SupportedKeys { get; } = BuildSupportedKeys();

    public static IReadOnlyList<VirtualKey> ModifierKeys { get; } =
    [
        VirtualKey.CONTROL,
        VirtualKey.LCONTROL,
        VirtualKey.RCONTROL,
        VirtualKey.SHIFT,
        VirtualKey.LSHIFT,
        VirtualKey.RSHIFT,
        VirtualKey.MENU,
        VirtualKey.LMENU,
        VirtualKey.RMENU,
    ];

    public static VirtualKey FromConfigValue(int keyCode)
    {
        if (keyCode < ushort.MinValue || keyCode > ushort.MaxValue)
            return VirtualKey.NO_KEY;

        var vkCode = (ushort)keyCode;
        return Enum.IsDefined(typeof(VirtualKey), vkCode)
            ? (VirtualKey)vkCode
            : VirtualKey.NO_KEY;
    }

    public static string FormatKey(VirtualKey key)
    {
        if (key >= VirtualKey.KEY_0 && key <= VirtualKey.KEY_9)
            return ((int)(key - VirtualKey.KEY_0)).ToString();

        if (key >= VirtualKey.NUMPAD0 && key <= VirtualKey.NUMPAD9)
            return $"NumPad{(int)(key - VirtualKey.NUMPAD0)}";

        return key switch
        {
            VirtualKey.NO_KEY => "None",
            VirtualKey.RETURN => "Enter",
            VirtualKey.ESCAPE => "Escape",
            VirtualKey.BACK => "Backspace",
            VirtualKey.DELETE => "Delete",
            VirtualKey.INSERT => "Insert",
            VirtualKey.SPACE => "Space",
            VirtualKey.TAB => "Tab",
            VirtualKey.PRIOR => "PageUp",
            VirtualKey.NEXT => "PageDown",
            VirtualKey.LEFT => "Left",
            VirtualKey.RIGHT => "Right",
            VirtualKey.UP => "Up",
            VirtualKey.DOWN => "Down",
            _ => key.ToString(),
        };
    }

    public static string FormatBinding(int keyCode, bool ctrl, bool shift, bool alt)
    {
        var parts = new List<string>();

        if (ctrl)
            parts.Add("Ctrl");
        if (shift)
            parts.Add("Shift");
        if (alt)
            parts.Add("Alt");

        parts.Add(FormatKey(FromConfigValue(keyCode)));
        return string.Join("+", parts);
    }

    public static bool TryParseLegacyKeyName(string? name, out VirtualKey key)
    {
        key = VirtualKey.NO_KEY;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();

        if (trimmed.Length == 1)
        {
            var upper = char.ToUpperInvariant(trimmed[0]);
            if (upper >= 'A' && upper <= 'Z')
            {
                key = (VirtualKey)((int)VirtualKey.A + (upper - 'A'));
                return true;
            }

            if (upper >= '0' && upper <= '9')
            {
                key = (VirtualKey)((int)VirtualKey.KEY_0 + (upper - '0'));
                return true;
            }
        }

        if (trimmed.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[1..], out var functionNumber) &&
            functionNumber >= 1 && functionNumber <= 24)
        {
            key = (VirtualKey)((int)VirtualKey.F1 + (functionNumber - 1));
            return true;
        }

        if (trimmed.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed[6..];
            if (int.TryParse(suffix, out var numpadNumber) && numpadNumber >= 0 && numpadNumber <= 9)
            {
                key = (VirtualKey)((int)VirtualKey.NUMPAD0 + numpadNumber);
                return true;
            }

            if (suffix.Equals("Enter", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.RETURN;
                return true;
            }
        }

        var matched = trimmed.ToUpperInvariant() switch
        {
            "SPACE" => VirtualKey.SPACE,
            "TAB" => VirtualKey.TAB,
            "ENTER" => VirtualKey.RETURN,
            "ESC" or "ESCAPE" => VirtualKey.ESCAPE,
            "BACKSPACE" => VirtualKey.BACK,
            "DEL" or "DELETE" => VirtualKey.DELETE,
            "INS" or "INSERT" => VirtualKey.INSERT,
            "HOME" => VirtualKey.HOME,
            "END" => VirtualKey.END,
            "PGUP" or "PAGEUP" => VirtualKey.PRIOR,
            "PGDN" or "PAGEDOWN" => VirtualKey.NEXT,
            "UP" => VirtualKey.UP,
            "DOWN" => VirtualKey.DOWN,
            "LEFT" => VirtualKey.LEFT,
            "RIGHT" => VirtualKey.RIGHT,
            _ => VirtualKey.NO_KEY,
        };

        if (matched == VirtualKey.NO_KEY)
            return false;

        key = matched;
        return true;
    }

    public static bool TryMigrateLegacyBinding(PullToOBSConfiguration configuration)
    {
        if (configuration.InstapostKeyCode != (int)VirtualKey.NO_KEY)
            return false;

        if (!TryParseLegacyKeyName(configuration.InstapostKey, out var migratedKey))
            return false;

        configuration.InstapostKeyCode = (int)migratedKey;
        configuration.InstapostKey = string.Empty;
        return true;
    }

    public static bool IsModifierKey(VirtualKey key)
    {
        foreach (var modifier in ModifierKeys)
        {
            if (modifier == key)
                return true;
        }

        return false;
    }

    public static bool IsAnyCtrlDown(IKeyState keyState)
    {
        return IsKeyDownIfValid(keyState, VirtualKey.CONTROL)
               || IsKeyDownIfValid(keyState, VirtualKey.LCONTROL)
               || IsKeyDownIfValid(keyState, VirtualKey.RCONTROL);
    }

    public static bool IsAnyShiftDown(IKeyState keyState)
    {
        return IsKeyDownIfValid(keyState, VirtualKey.SHIFT)
               || IsKeyDownIfValid(keyState, VirtualKey.LSHIFT)
               || IsKeyDownIfValid(keyState, VirtualKey.RSHIFT);
    }

    public static bool IsAnyAltDown(IKeyState keyState)
    {
        return IsKeyDownIfValid(keyState, VirtualKey.MENU)
               || IsKeyDownIfValid(keyState, VirtualKey.LMENU)
               || IsKeyDownIfValid(keyState, VirtualKey.RMENU);
    }

    public static bool AreAnyCaptureKeysDown(IKeyState keyState)
    {
        foreach (var modifier in ModifierKeys)
        {
            if (IsKeyDownIfValid(keyState, modifier))
                return true;
        }

        foreach (var key in SupportedKeys)
        {
            if (key == VirtualKey.NO_KEY)
                continue;

            if (IsKeyDownIfValid(keyState, key))
                return true;
        }

        return false;
    }

    public static bool TryGetPressedPrimaryKey(IKeyState keyState, out VirtualKey key)
    {
        foreach (var candidate in SupportedKeys)
        {
            if (candidate == VirtualKey.NO_KEY || IsModifierKey(candidate))
                continue;

            if (IsKeyDownIfValid(keyState, candidate))
            {
                key = candidate;
                return true;
            }
        }

        key = VirtualKey.NO_KEY;
        return false;
    }

    public static InstapostHotkeyCaptureAction GetCaptureAction(IKeyState keyState)
    {
        var ctrl = IsAnyCtrlDown(keyState);
        var shift = IsAnyShiftDown(keyState);
        var alt = IsAnyAltDown(keyState);

        if (!ctrl && !shift && !alt)
        {
            if (IsKeyDownIfValid(keyState, VirtualKey.ESCAPE))
                return new InstapostHotkeyCaptureAction(InstapostHotkeyCaptureActionKind.Cancel, VirtualKey.NO_KEY, false, false, false);

            if (IsKeyDownIfValid(keyState, VirtualKey.DELETE) || IsKeyDownIfValid(keyState, VirtualKey.BACK))
                return new InstapostHotkeyCaptureAction(InstapostHotkeyCaptureActionKind.Clear, VirtualKey.NO_KEY, false, false, false);
        }

        if (!TryGetPressedPrimaryKey(keyState, out var key))
            return new InstapostHotkeyCaptureAction(InstapostHotkeyCaptureActionKind.None, VirtualKey.NO_KEY, ctrl, shift, alt);

        return new InstapostHotkeyCaptureAction(InstapostHotkeyCaptureActionKind.Set, key, ctrl, shift, alt);
    }

    private static bool IsKeyDownIfValid(IKeyState keyState, VirtualKey key)
    {
        return keyState.IsVirtualKeyValid(key) && keyState[key];
    }

    private static IReadOnlyList<VirtualKey> BuildSupportedKeys()
    {
        var keys = new List<VirtualKey>
        {
            VirtualKey.NO_KEY,
        };

        AddRange(keys, VirtualKey.A, VirtualKey.Z);
        AddRange(keys, VirtualKey.KEY_0, VirtualKey.KEY_9);
        AddRange(keys, VirtualKey.F1, VirtualKey.F12);
        AddRange(keys, VirtualKey.NUMPAD0, VirtualKey.NUMPAD9);

        keys.Add(VirtualKey.SPACE);
        keys.Add(VirtualKey.TAB);
        keys.Add(VirtualKey.RETURN);
        keys.Add(VirtualKey.ESCAPE);
        keys.Add(VirtualKey.BACK);
        keys.Add(VirtualKey.DELETE);
        keys.Add(VirtualKey.INSERT);
        keys.Add(VirtualKey.HOME);
        keys.Add(VirtualKey.END);
        keys.Add(VirtualKey.PRIOR);
        keys.Add(VirtualKey.NEXT);
        keys.Add(VirtualKey.LEFT);
        keys.Add(VirtualKey.RIGHT);
        keys.Add(VirtualKey.UP);
        keys.Add(VirtualKey.DOWN);

        return keys;
    }

    private static void AddRange(List<VirtualKey> keys, VirtualKey first, VirtualKey last)
    {
        for (var value = (int)first; value <= (int)last; value++)
            keys.Add((VirtualKey)value);
    }
}
