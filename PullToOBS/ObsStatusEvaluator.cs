namespace PullToOBS;

public static class ObsStatusEvaluator
{
    public static ObsStatusKind Evaluate(
        bool isConnecting,
        bool isConnected,
        bool isRecording,
        bool isStandby,
        bool isReplayBufferActive)
    {
        if (isConnecting)
            return ObsStatusKind.Connecting;

        if (!isConnected)
            return ObsStatusKind.Disconnected;

        if (isRecording)
            return ObsStatusKind.Recording;

        if (isStandby)
            return ObsStatusKind.Standby;

        if (!isReplayBufferActive)
            return ObsStatusKind.ReplayBufferInactive;

        return ObsStatusKind.Ready;
    }
}
