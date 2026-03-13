using PullToOBS.Models;
using Xunit;

namespace PullToOBS.Tests;

public class InCombatPayloadTests
{
    [Fact]
    public void Record_PropertiesAreSet()
    {
        var payload = new InCombatPayload(InGameCombat: true, InActCombat: false);

        Assert.True(payload.InGameCombat);
        Assert.False(payload.InActCombat);
    }

    [Fact]
    public void Record_ValueEquality()
    {
        var a = new InCombatPayload(true, false);
        var b = new InCombatPayload(true, false);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_ValueInequality()
    {
        var a = new InCombatPayload(true, false);
        var b = new InCombatPayload(false, true);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Record_Deconstruction()
    {
        var payload = new InCombatPayload(true, true);
        var (inGame, inAct) = payload;

        Assert.True(inGame);
        Assert.True(inAct);
    }

    [Fact]
    public void Record_ToString_ContainsPropertyValues()
    {
        var payload = new InCombatPayload(true, false);
        var str = payload.ToString();

        Assert.Contains("InGameCombat", str);
        Assert.Contains("True", str);
        Assert.Contains("InActCombat", str);
        Assert.Contains("False", str);
    }
}
