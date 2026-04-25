using System;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using NSubstitute;
using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public class EncounterManagerTests : IDisposable
{
    private readonly IOBSController _obs;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly ICondition _condition;
    private readonly IPluginLog _log;
    private readonly EncounterManager _sut;

    private bool _inCombat;

    public EncounterManagerTests()
    {
        _obs = Substitute.For<IOBSController>();
        _clientState = Substitute.For<IClientState>();
        _playerState = Substitute.For<IPlayerState>();
        _condition = Substitute.For<ICondition>();
        _log = Substitute.For<IPluginLog>();

        // Default: OBS is connected and not recording
        _obs.IsConnected.Returns(true);
        _obs.IsRecording.Returns(false);
        _obs.IsReplayBufferConfigured.Returns(false);
        _obs.SaveReplayBuffer().Returns(Task.FromResult<string?>("replay.mkv"));
        _obs.StopRecording().Returns("recording.mkv");
        _clientState.TerritoryType.Returns((ushort)987);

        // Default: not in combat
        _inCombat = false;
        _condition[ConditionFlag.InCombat].Returns(_ => _inCombat);

        _sut = new EncounterManager(_obs, _clientState, _playerState, _condition, _log);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    /// <summary>Sets combat state and calls Update to trigger detection.</summary>
    private void SetCombatState(bool inCombat)
    {
        _inCombat = inCombat;
        _sut.Update();
    }

    [Fact]
    public void InitialState_NotInCombat()
    {
        Assert.False(_sut.IsInCombat);
    }

    [Fact]
    public async Task EnteringCombat_StartsRecording()
    {
        SetCombatState(true);

        // Give the background work item a moment to run
        await Task.Delay(500);

        Assert.True(_sut.IsInCombat);
        _obs.Received(1).StartRecording();
    }

    [Fact]
    public async Task EnteringCombat_WhenDisconnected_DoesNotStartRecording()
    {
        _obs.IsConnected.Returns(false);

        SetCombatState(true);
        await Task.Delay(500);

        Assert.True(_sut.IsInCombat);
        _obs.DidNotReceive().StartRecording();
    }

    [Fact]
    public async Task LeavingCombat_StopsRecordingAfterGracePeriod()
    {
        _obs.IsRecording.Returns(true);

        // Enter combat
        SetCombatState(true);
        await Task.Delay(500);

        // Leave combat
        SetCombatState(false);

        // Should NOT have stopped yet (grace period is 5s, we only wait briefly)
        await Task.Delay(500);
        _obs.DidNotReceive().StopRecording();

        Assert.False(_sut.IsInCombat);
    }

    [Fact]
    public async Task ReenteringCombat_CancelsPendingStop()
    {
        _obs.IsRecording.Returns(true);

        // Enter -> Leave -> Re-enter combat quickly
        SetCombatState(true);
        await Task.Delay(500);

        SetCombatState(false);
        await Task.Delay(100);

        // Re-enter before grace period expires
        SetCombatState(true);
        await Task.Delay(6000);

        // StopRecording should NOT have been called -- the pending stop was cancelled
        _obs.DidNotReceive().StopRecording();
    }

    [Fact]
    public void StateChanged_FiresOnCombatStateChange()
    {
        var fired = false;
        _sut.StateChanged += () => fired = true;

        SetCombatState(true);

        Assert.True(fired);
    }

    [Fact]
    public void StateChanged_DoesNotFireWhenStateUnchanged()
    {
        var fireCount = 0;
        _sut.StateChanged += () => fireCount++;

        // Call Update without changing state
        _sut.Update();
        _sut.Update();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task EncounterStarted_FiresAfterReplayBufferSaveDelay()
    {
        var fired = false;
        _sut.EncounterStarted += () => fired = true;

        SetCombatState(true);

        // EncounterStarted fires after the replay buffer save delay (5s)
        await Task.Delay(6500);

        Assert.True(fired);
    }

    [Fact]
    public async Task Dispose_CancelsInFlightTimers()
    {
        // Enter combat to start background work and schedule replay buffer timer
        SetCombatState(true);
        await Task.Delay(500);

        // Dispose while the replay buffer timer is pending
        _sut.Dispose();

        // Double dispose should be safe
        _sut.Dispose();

        // Wait for what would have been the replay buffer delay
        await Task.Delay(6000);

        // SaveReplayBuffer should NOT have been called because disposal cancelled the timer
        _ = _obs.DidNotReceive().SaveReplayBuffer();
    }

    [Fact]
    public void Dispose_PreventsSubsequentUpdates()
    {
        _sut.Dispose();

        // After disposal, Update should not cause any OBS calls
        _obs.ClearReceivedCalls();

        SetCombatState(true);

        _obs.DidNotReceive().StartRecording();
    }

    [Fact]
    public async Task EnteringCombat_WithReplayBuffer_SavesReplayBuffer()
    {
        _obs.IsReplayBufferConfigured.Returns(true);

        // Simulate realistic OBS behavior: IsRecording becomes true after StartRecording is called
        _obs.When(x => x.StartRecording()).Do(_ => _obs.IsRecording.Returns(true));

        SetCombatState(true);

        // Wait for the replay buffer save delay (5s) + margin
        await Task.Delay(6500);

        await _obs.Received(1).SaveReplayBuffer();
    }

    [Fact]
    public async Task EnteringCombat_WithoutReplayBuffer_DoesNotSaveReplayBuffer()
    {
        _obs.IsReplayBufferConfigured.Returns(false);

        // Simulate realistic OBS behavior: IsRecording becomes true after StartRecording is called
        _obs.When(x => x.StartRecording()).Do(_ => _obs.IsRecording.Returns(true));

        SetCombatState(true);
        await Task.Delay(6500);

        _ = _obs.DidNotReceive().SaveReplayBuffer();
    }

    [Fact]
    public async Task ErrorOccurred_FiresOnStartRecordingException()
    {
        _obs.When(x => x.StartRecording()).Do(_ => throw new Exception("OBS error"));

        string? errorMsg = null;
        _sut.ErrorOccurred += msg => errorMsg = msg;

        SetCombatState(true);
        await Task.Delay(500);

        Assert.NotNull(errorMsg);
        Assert.Contains("OBS error", errorMsg);
    }

    [Fact]
    public async Task LeavingAndReentering_ProducesContinuousRecording()
    {
        _obs.IsRecording.Returns(true);

        // Enter combat
        SetCombatState(true);
        await Task.Delay(500);

        // Leave combat briefly
        SetCombatState(false);
        await Task.Delay(100);

        // Re-enter combat
        SetCombatState(true);
        await Task.Delay(100);

        // Leave again
        SetCombatState(false);

        // Wait full grace period
        await Task.Delay(6500);

        // StopRecording should be called exactly once (from the final leave)
        _obs.Received(1).StopRecording();
    }

    [Fact]
    public async Task LeavingCombat_RecordingStopsDuringGracePeriod_DoesNotCallStopRecording()
    {
        _obs.IsRecording.Returns(true);

        // Enter combat
        SetCombatState(true);
        await Task.Delay(500);

        // Leave combat -- grace period starts
        SetCombatState(false);

        // Simulate recording stopping externally during the grace period
        await Task.Delay(1000);
        _obs.IsRecording.Returns(false);

        // Wait for full grace period to elapse
        await Task.Delay(5500);

        // StopRecording should NOT have been called -- recording was already stopped
        _obs.DidNotReceive().StopRecording();
    }

    [Fact]
    public async Task LeavingCombat_RecordingStopsDuringGracePeriod_FiresEncounterEnded()
    {
        _obs.IsRecording.Returns(true);

        var encounterEndedFired = false;
        _sut.EncounterEnded += _ => encounterEndedFired = true;

        // Enter combat
        SetCombatState(true);
        await Task.Delay(500);

        // Leave combat -- grace period starts
        SetCombatState(false);

        // Simulate recording stopping externally during the grace period
        await Task.Delay(1000);
        _obs.IsRecording.Returns(false);

        // Wait for full grace period to elapse
        await Task.Delay(5500);

        // EncounterEnded should still fire even though we didn't call StopRecording
        Assert.True(encounterEndedFired);
    }

    [Fact]
    public void LeavingCombat_WhenNotRecording_FiresEncounterEndedSynchronously()
    {
        // OBS is connected but not recording -- the pre-check in HandleEncounterEnd
        // should fire EncounterEnded without starting the grace period timer.
        _obs.IsRecording.Returns(false);

        var encounterEndedFired = false;
        _sut.EncounterEnded += _ => encounterEndedFired = true;

        // Enter then leave combat
        SetCombatState(true);
        SetCombatState(false);

        // EncounterEnded fires synchronously (no timer involved)
        Assert.True(encounterEndedFired);
    }

    [Fact]
    public async Task LeavingCombat_EncounterEndedIncludesPathsAndTerritoryType()
    {
        _obs.IsReplayBufferConfigured.Returns(true);
        _obs.When(x => x.StartRecording()).Do(_ => _obs.IsRecording.Returns(true));

        EncounterRecord? encounterRecord = null;
        _sut.EncounterEnded += record => encounterRecord = record;

        SetCombatState(true);
        await Task.Delay(6500);

        SetCombatState(false);
        await Task.Delay(6500);

        Assert.NotNull(encounterRecord);
        Assert.Equal(987u, encounterRecord!.TerritoryType);
        Assert.Null(encounterRecord.JobAbbreviation);
        Assert.Equal("recording.mkv", encounterRecord.RecordingPath);
        Assert.Equal("replay.mkv", encounterRecord.ReplayBufferPath);
    }

    [Fact]
    public async Task Dispose_CancelsGracePeriodTimer()
    {
        _obs.IsRecording.Returns(true);

        // Enter combat
        SetCombatState(true);
        await Task.Delay(500);

        // Leave combat -- grace period timer starts
        SetCombatState(false);

        // Dispose while grace period is pending
        _sut.Dispose();

        // Wait for what would have been the grace period
        await Task.Delay(6000);

        // StopRecording should NOT have been called
        _obs.DidNotReceive().StopRecording();
    }
}
