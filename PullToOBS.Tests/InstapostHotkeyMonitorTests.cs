using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public class InstapostHotkeyMonitorTests
{
    private readonly IKeyState _keyState;
    private readonly InstapostHotkeyMonitor _sut;

    public InstapostHotkeyMonitorTests()
    {
        _keyState = Substitute.For<IKeyState>();
        _sut = new InstapostHotkeyMonitor(_keyState);

        _keyState.IsVirtualKeyValid(Arg.Any<VirtualKey>()).Returns(true);
    }

    [Fact]
    public void Update_ReturnsTrueOnlyOnPressTransition()
    {
        _keyState[VirtualKey.F9].Returns(false, true, true, false, true);

        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }

    [Fact]
    public void Update_WhenConfiguredModifierMissing_ReturnsFalse()
    {
        _keyState[VirtualKey.F9].Returns(true);
        _keyState[VirtualKey.CONTROL].Returns(false);
        _keyState[VirtualKey.LCONTROL].Returns(false);
        _keyState[VirtualKey.RCONTROL].Returns(false);

        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: true, shift: false, alt: false));
    }

    [Fact]
    public void Update_WhenUnexpectedModifierHeld_ReturnsFalse()
    {
        _keyState[VirtualKey.F9].Returns(true);
        _keyState[VirtualKey.CONTROL].Returns(true);

        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }

    [Fact]
    public void Update_WhenModifiersMatchExactly_ReturnsTrue()
    {
        _keyState[VirtualKey.F9].Returns(true);
        _keyState[VirtualKey.CONTROL].Returns(true);
        _keyState[VirtualKey.LCONTROL].Returns(false);
        _keyState[VirtualKey.RCONTROL].Returns(false);
        _keyState[VirtualKey.SHIFT].Returns(false);
        _keyState[VirtualKey.LSHIFT].Returns(false);
        _keyState[VirtualKey.RSHIFT].Returns(false);
        _keyState[VirtualKey.MENU].Returns(false);
        _keyState[VirtualKey.LMENU].Returns(false);
        _keyState[VirtualKey.RMENU].Returns(false);

        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: true, shift: false, alt: false));
    }

    [Fact]
    public void Reset_ClearsPreviousState()
    {
        _keyState[VirtualKey.F9].Returns(true);

        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));

        _sut.Reset();

        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }

    [Fact]
    public void Update_InvalidOrUnsetKey_ReturnsFalseAndClearsState()
    {
        _keyState[VirtualKey.F9].Returns(true);

        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.NO_KEY, ctrl: false, shift: false, alt: false));
        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }

    [Fact]
    public void SuppressUntilReleased_PreventsTriggerUntilHotkeyIsReleased()
    {
        _keyState[VirtualKey.F9].Returns(true, true, false, true);

        _sut.SuppressUntilReleased();

        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.False(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }

    [Fact]
    public void Reset_ClearsSuppressionState()
    {
        _keyState[VirtualKey.F9].Returns(true);

        _sut.SuppressUntilReleased();
        _sut.Reset();

        Assert.True(_sut.Update((int)VirtualKey.F9, ctrl: false, shift: false, alt: false));
    }
}
