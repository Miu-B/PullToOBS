using System;
using PullToOBS.Models;

namespace PullToOBS;

public interface IIINACTClient : IDisposable
{
    bool IsConnected { get; }

    event Action? Connected;
    event Action? Disconnected;
    event Action<InCombatPayload>? CombatStateChanged;
    event Action<string>? ErrorOccurred;

    void TryConnect();
    void TryHeartbeat();
}
