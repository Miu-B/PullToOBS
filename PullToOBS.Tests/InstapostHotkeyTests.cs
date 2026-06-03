using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public class InstapostHotkeyTests
{
    [Theory]
    [InlineData("A", VirtualKey.A)]
    [InlineData("z", VirtualKey.Z)]
    [InlineData("0", VirtualKey.KEY_0)]
    [InlineData("9", VirtualKey.KEY_9)]
    [InlineData("F1", VirtualKey.F1)]
    [InlineData("F12", VirtualKey.F12)]
    [InlineData("NumPad0", VirtualKey.NUMPAD0)]
    [InlineData("NumPad9", VirtualKey.NUMPAD9)]
    [InlineData("Space", VirtualKey.SPACE)]
    [InlineData("Tab", VirtualKey.TAB)]
    [InlineData("Enter", VirtualKey.RETURN)]
    [InlineData("Esc", VirtualKey.ESCAPE)]
    [InlineData("Escape", VirtualKey.ESCAPE)]
    [InlineData("Backspace", VirtualKey.BACK)]
    [InlineData("Del", VirtualKey.DELETE)]
    [InlineData("Delete", VirtualKey.DELETE)]
    [InlineData("Ins", VirtualKey.INSERT)]
    [InlineData("Insert", VirtualKey.INSERT)]
    [InlineData("Home", VirtualKey.HOME)]
    [InlineData("End", VirtualKey.END)]
    [InlineData("PgUp", VirtualKey.PRIOR)]
    [InlineData("PageUp", VirtualKey.PRIOR)]
    [InlineData("PgDn", VirtualKey.NEXT)]
    [InlineData("PageDown", VirtualKey.NEXT)]
    [InlineData("Left", VirtualKey.LEFT)]
    [InlineData("Right", VirtualKey.RIGHT)]
    [InlineData("Up", VirtualKey.UP)]
    [InlineData("Down", VirtualKey.DOWN)]
    public void TryParseLegacyKeyName_ParsesSupportedNames(string name, VirtualKey expected)
    {
        var success = InstapostHotkey.TryParseLegacyKeyName(name, out var key);

        Assert.True(success);
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NotAKey")]
    [InlineData("NumPadFoo")]
    [InlineData("F99")]
    public void TryParseLegacyKeyName_RejectsInvalidNames(string name)
    {
        var success = InstapostHotkey.TryParseLegacyKeyName(name, out var key);

        Assert.False(success);
        Assert.Equal(VirtualKey.NO_KEY, key);
    }

    [Theory]
    [InlineData(VirtualKey.NO_KEY, "None")]
    [InlineData(VirtualKey.KEY_0, "0")]
    [InlineData(VirtualKey.KEY_9, "9")]
    [InlineData(VirtualKey.NUMPAD3, "NumPad3")]
    [InlineData(VirtualKey.RETURN, "Enter")]
    [InlineData(VirtualKey.PRIOR, "PageUp")]
    [InlineData(VirtualKey.NEXT, "PageDown")]
    [InlineData(VirtualKey.ESCAPE, "Escape")]
    public void FormatKey_ReturnsExpectedNames(VirtualKey key, string expected)
    {
        Assert.Equal(expected, InstapostHotkey.FormatKey(key));
    }

    [Fact]
    public void FormatBinding_IncludesModifiersAndKey()
    {
        var binding = InstapostHotkey.FormatBinding((int)VirtualKey.F9, ctrl: true, shift: false, alt: true);

        Assert.Equal("Ctrl+Alt+F9", binding);
    }

    [Fact]
    public void SupportedKeys_IncludesCommonKeysAndNone()
    {
        Assert.Contains(VirtualKey.NO_KEY, InstapostHotkey.SupportedKeys);
        Assert.Contains(VirtualKey.A, InstapostHotkey.SupportedKeys);
        Assert.Contains(VirtualKey.KEY_0, InstapostHotkey.SupportedKeys);
        Assert.Contains(VirtualKey.F12, InstapostHotkey.SupportedKeys);
        Assert.Contains(VirtualKey.NUMPAD9, InstapostHotkey.SupportedKeys);
        Assert.Contains(VirtualKey.SPACE, InstapostHotkey.SupportedKeys);
    }

    [Fact]
    public void FromConfigValue_InvalidValue_ReturnsNoKey()
    {
        Assert.Equal(VirtualKey.NO_KEY, InstapostHotkey.FromConfigValue(123456));
    }

    [Fact]
    public void AreAnyCaptureKeysDown_ReturnsTrueForModifierOrSupportedKey()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.LCONTROL].Returns(true);

        Assert.True(InstapostHotkey.AreAnyCaptureKeysDown(keyState));

        keyState[VirtualKey.LCONTROL].Returns(false);
        keyState[VirtualKey.F9].Returns(true);

        Assert.True(InstapostHotkey.AreAnyCaptureKeysDown(keyState));
    }

    [Fact]
    public void AreAnyCaptureKeysDown_ReturnsFalseWhenNothingRelevantDown()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);

        Assert.False(InstapostHotkey.AreAnyCaptureKeysDown(keyState));
    }

    [Fact]
    public void TryGetPressedPrimaryKey_ReturnsFirstPressedNonModifierKey()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.LCONTROL].Returns(true);
        keyState[VirtualKey.K].Returns(true);

        var success = InstapostHotkey.TryGetPressedPrimaryKey(keyState, out var key);

        Assert.True(success);
        Assert.Equal(VirtualKey.K, key);
    }

    [Fact]
    public void ModifierHelpers_DetectAggregateModifierState()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.RCONTROL].Returns(true);
        keyState[VirtualKey.LSHIFT].Returns(true);
        keyState[VirtualKey.MENU].Returns(true);

        Assert.True(InstapostHotkey.IsAnyCtrlDown(keyState));
        Assert.True(InstapostHotkey.IsAnyShiftDown(keyState));
        Assert.True(InstapostHotkey.IsAnyAltDown(keyState));
    }

    [Fact]
    public void TryMigrateLegacyBinding_MigratesOnlyWhenKeyCodeIsUnset()
    {
        var configuration = new PullToOBSConfiguration
        {
            InstapostKeyCode = (int)VirtualKey.NO_KEY,
            InstapostKey = "F9",
        };

        var changed = InstapostHotkey.TryMigrateLegacyBinding(configuration);

        Assert.True(changed);
        Assert.Equal((int)VirtualKey.F9, configuration.InstapostKeyCode);
        Assert.Equal(string.Empty, configuration.InstapostKey);
    }

    [Fact]
    public void TryMigrateLegacyBinding_DoesNothingWhenKeyCodeAlreadySet()
    {
        var configuration = new PullToOBSConfiguration
        {
            InstapostKeyCode = (int)VirtualKey.F10,
            InstapostKey = "F9",
        };

        var changed = InstapostHotkey.TryMigrateLegacyBinding(configuration);

        Assert.False(changed);
        Assert.Equal((int)VirtualKey.F10, configuration.InstapostKeyCode);
        Assert.Equal("F9", configuration.InstapostKey);
    }

    [Fact]
    public void GetCaptureAction_UsesEscapeToCancelWhenNoModifiersHeld()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.ESCAPE].Returns(true);

        var action = InstapostHotkey.GetCaptureAction(keyState);

        Assert.Equal(InstapostHotkeyCaptureActionKind.Cancel, action.Kind);
    }

    [Fact]
    public void GetCaptureAction_UsesDeleteOrBackspaceToClearWhenNoModifiersHeld()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.DELETE].Returns(true);

        var action = InstapostHotkey.GetCaptureAction(keyState);

        Assert.Equal(InstapostHotkeyCaptureActionKind.Clear, action.Kind);
    }

    [Fact]
    public void GetCaptureAction_CapturesModifiedDeleteAsHotkeyInsteadOfClearing()
    {
        var keyState = Substitute.For<IKeyState>();
        keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
        keyState[VirtualKey.DELETE].Returns(true);
        keyState[VirtualKey.CONTROL].Returns(true);

        var action = InstapostHotkey.GetCaptureAction(keyState);

        Assert.Equal(InstapostHotkeyCaptureActionKind.Set, action.Kind);
        Assert.Equal(VirtualKey.DELETE, action.Key);
        Assert.True(action.Ctrl);
        Assert.False(action.Shift);
        Assert.False(action.Alt);
    }
}
