using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using PullToOBS.Models;
using Xunit;

namespace PullToOBS.Tests;

public class EncounterManagerTests : IDisposable
{
    private readonly IOBSController _obs;
    private readonly IIINACTClient _iinact;
    private readonly IPluginLog _log;
    private readonly EncounterManager _sut;

    public EncounterManagerTests()
    {
        _obs = Substitute.For<IOBSController>();
        _iinact = Substitute.For<IIINACTClient>();
        _log = Substitute.For<IPluginLog>();

        // Default: OBS is connected and not recording
        _obs.IsConnected.Returns(true);
        _obs.IsRecording.Returns(false);
        _obs.IsReplayBufferConfigured.Returns(false);

        _sut = new EncounterManager(_obs, _iinact, _log);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    /// <summary>Raises the CombatStateChanged event on the mock.</summary>
    private void RaiseCombatStateChanged(bool inGame, bool inAct)
    {
        _iinact.CombatStateChanged += Raise.Event<Action<InCombatPayload>>(
            new InCombatPayload(inGame, inAct));
    }

    [Fact]
    public void InitialState_NotInCombat()
    {
        Assert.False(_sut.IsInCombat);
    }

    [Fact]
    public async Task EnteringCombat_StartsRecording()
    {
        RaiseCombatStateChanged(inGame: true, inAct: false);

        // Give the background task a moment to run
        await Task.Delay(200);

        Assert.True(_sut.IsInCombat);
        _obs.Received(1).StartRecording();
    }

    [Fact]
    public async Task EnteringCombat_WhenDisconnected_DoesNotStartRecording()
    {
        _obs.IsConnected.Returns(false);

        RaiseCombatStateChanged(inGame: true, inAct: true);
        await Task.Delay(200);

        Assert.True(_sut.IsInCombat);
        _obs.DidNotReceive().StartRecording();
    }

    [Fact]
    public async Task LeavingCombat_StopsRecordingAfterGracePeriod()
    {
        _obs.IsRecording.Returns(true);

        // Enter combat
        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(200);

        // Leave combat
        RaiseCombatStateChanged(inGame: false, inAct: false);

        // Should NOT have stopped yet (grace period is 5s, we only wait briefly)
        await Task.Delay(200);
        _obs.DidNotReceive().StopRecording();

        Assert.False(_sut.IsInCombat);
    }

    [Fact]
    public async Task ReenteringCombat_CancelsPendingStop()
    {
        _obs.IsRecording.Returns(true);

        // Enter -> Leave -> Re-enter combat quickly
        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(200);

        RaiseCombatStateChanged(inGame: false, inAct: false);
        await Task.Delay(100);

        // Re-enter before grace period expires
        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(6000);

        // StopRecording should NOT have been called -- the pending stop was cancelled
        _obs.DidNotReceive().StopRecording();
    }

    [Fact]
    public void StateChanged_FiresOnCombatStateChange()
    {
        var fired = false;
        _sut.StateChanged += () => fired = true;

        RaiseCombatStateChanged(inGame: true, inAct: false);

        Assert.True(fired);
    }

    [Fact]
    public async Task EncounterStarted_FiresAfterRecordingStarts()
    {
        var fired = false;
        _sut.EncounterStarted += () => fired = true;

        RaiseCombatStateChanged(inGame: true, inAct: false);

        // EncounterStarted fires after the replay buffer save delay (5s),
        // but we can check it fires eventually
        await Task.Delay(6000);

        Assert.True(fired);
    }

    [Fact]
    public async Task Dispose_CancelsInFlightOperations()
    {
        // Enter combat to start a background task
        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(100);

        // Dispose while the replay buffer delay is pending
        _sut.Dispose();

        // Double dispose should be safe
        _sut.Dispose();

        // Wait for what would have been the replay buffer delay
        await Task.Delay(6000);

        // SaveReplayBuffer should NOT have been called because disposal cancelled it
        _obs.DidNotReceive().SaveReplayBuffer();
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvents()
    {
        _sut.Dispose();

        // After disposal, raising events should not cause any OBS calls
        // (event handlers were removed, so NSubstitute won't route them to the disposed manager)
        _obs.ClearReceivedCalls();

        // This should not reach the disposed EncounterManager
        RaiseCombatStateChanged(inGame: true, inAct: false);

        _obs.DidNotReceive().StartRecording();
    }

    [Fact]
    public async Task EnteringCombat_WithReplayBuffer_SavesReplayBuffer()
    {
        _obs.IsReplayBufferConfigured.Returns(true);
        _obs.IsRecording.Returns(true);

        RaiseCombatStateChanged(inGame: true, inAct: true);

        // Wait for the replay buffer save delay (5s) + margin
        await Task.Delay(6000);

        _obs.Received(1).SaveReplayBuffer();
    }

    [Fact]
    public async Task EnteringCombat_WithoutReplayBuffer_DoesNotSaveReplayBuffer()
    {
        _obs.IsReplayBufferConfigured.Returns(false);
        _obs.IsRecording.Returns(true);

        RaiseCombatStateChanged(inGame: true, inAct: true);
        await Task.Delay(6000);

        _obs.DidNotReceive().SaveReplayBuffer();
    }

    [Fact]
    public async Task ErrorOccurred_FiresOnStartRecordingException()
    {
        _obs.When(x => x.StartRecording()).Do(_ => throw new Exception("OBS error"));

        string? errorMsg = null;
        _sut.ErrorOccurred += msg => errorMsg = msg;

        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(200);

        Assert.NotNull(errorMsg);
        Assert.Contains("OBS error", errorMsg);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EnteringCombat_AnyTrueFlag_IsInCombat(bool inGame, bool inAct)
    {
        RaiseCombatStateChanged(inGame, inAct);
        await Task.Delay(200);

        Assert.True(_sut.IsInCombat);
        _obs.Received(1).StartRecording();
    }

    [Fact]
    public async Task BothFlagsFalse_NotInCombat()
    {
        // First enter combat, then leave
        RaiseCombatStateChanged(inGame: true, inAct: false);
        await Task.Delay(100);

        RaiseCombatStateChanged(inGame: false, inAct: false);

        Assert.False(_sut.IsInCombat);
    }
}
