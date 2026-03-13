using Xunit;
using PullToOBS;
using System.Numerics;
using Dalamud.Configuration;

namespace PullToOBS.Tests;

public class PullToOBSConfigurationTests
{
    [Fact]
    public void Configuration_Initializes_With_Defaults()
    {
        var config = new PullToOBSConfiguration();

        Assert.Equal("ws://localhost:4455", config.ObsWebSocketUrl);
        Assert.Equal("", config.ObsPassword);
        Assert.False(config.AutoConnectOnStart);
        Assert.Equal(new Vector2(300, 300), config.IndicatorPosition);
        Assert.Equal(1.0f, config.IndicatorScale);
        Assert.False(config.HideIndicator);
    }

    [Fact]
    public void Configuration_Version_Is_Set()
    {
        var config = new PullToOBSConfiguration();

        Assert.Equal(1, config.Version);
    }

    [Fact]
    public void Save_Invokes_Delegate_When_Set()
    {
        var config = new PullToOBSConfiguration();
        IPluginConfiguration? saved = null;
        config.SetSaveAction(c => saved = c);

        config.Save();

        Assert.Same(config, saved);
    }

    [Fact]
    public void Save_Does_Not_Throw_When_No_Delegate()
    {
        var config = new PullToOBSConfiguration();

        // Should not throw -- delegate is null by default
        var ex = Record.Exception(() => config.Save());

        Assert.Null(ex);
    }

    [Fact]
    public void Configuration_Properties_Are_Mutable()
    {
        var config = new PullToOBSConfiguration();

        config.ObsWebSocketUrl = "ws://192.168.1.1:9999";
        config.ObsPassword = "secret";
        config.AutoConnectOnStart = true;
        config.IndicatorPosition = new Vector2(100, 200);
        config.IndicatorScale = 1.5f;
        config.HideIndicator = true;

        Assert.Equal("ws://192.168.1.1:9999", config.ObsWebSocketUrl);
        Assert.Equal("secret", config.ObsPassword);
        Assert.True(config.AutoConnectOnStart);
        Assert.Equal(new Vector2(100, 200), config.IndicatorPosition);
        Assert.Equal(1.5f, config.IndicatorScale);
        Assert.True(config.HideIndicator);
    }
}
