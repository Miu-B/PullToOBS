using PullToOBS;
using Xunit;

namespace PullToOBS.Tests;

public class ObsStatusEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsConnecting_WhenConnecting()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: true,
            isConnected: true,
            isRecording: true,
            isStandby: true,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.Connecting, status);
    }

    [Fact]
    public void Evaluate_ReturnsDisconnected_WhenNotConnected()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: false,
            isRecording: false,
            isStandby: false,
            isReplayBufferActive: true);

        Assert.Equal(ObsStatusKind.Disconnected, status);
    }

    [Fact]
    public void Evaluate_ReturnsRecording_WhenRecording()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: true,
            isStandby: true,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.Recording, status);
    }

    [Fact]
    public void Evaluate_ReturnsStandby_WhenStandbyAndNotRecording()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: false,
            isStandby: true,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.Standby, status);
    }

    [Fact]
    public void Evaluate_ReturnsReplayBufferInactive_WhenConnectedAndBufferInactive()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: false,
            isStandby: false,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.ReplayBufferInactive, status);
    }

    [Fact]
    public void Evaluate_ReturnsReady_WhenConnectedAndReady()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: false,
            isStandby: false,
            isReplayBufferActive: true);

        Assert.Equal(ObsStatusKind.Ready, status);
    }

    [Fact]
    public void Evaluate_PrioritizesRecordingOverStandbyAndBufferState()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: true,
            isStandby: true,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.Recording, status);
    }

    [Fact]
    public void Evaluate_PrioritizesStandbyOverReplayBufferInactive()
    {
        var status = ObsStatusEvaluator.Evaluate(
            isConnecting: false,
            isConnected: true,
            isRecording: false,
            isStandby: true,
            isReplayBufferActive: false);

        Assert.Equal(ObsStatusKind.Standby, status);
    }
}
