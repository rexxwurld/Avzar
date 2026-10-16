using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Avzar.Mobile;

/// <summary>
/// Authenticated SignalR client for tenant-scoped business events. The server decides group membership;
/// the client never supplies a business or user group name.
/// </summary>
public sealed class BusinessEventsClient(AvzarApi api) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _connection;

    public event Action<JsonElement>? TaskUpdated;
    public event Action<HubConnectionState>? StateChanged;
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is null)
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(api.BusinessEventsHubUrl, options =>
                    {
                        options.AccessTokenProvider = () => api.GetRealtimeAccessTokenAsync(ct);
                    })
                    .WithAutomaticReconnect(new[]
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30)
                    })
                    .Build();

                _connection.On<JsonElement>("task.updated", payload => TaskUpdated?.Invoke(payload));
                _connection.Reconnecting += error => { StateChanged?.Invoke(HubConnectionState.Reconnecting); return Task.CompletedTask; };
                _connection.Reconnected += _ => { StateChanged?.Invoke(HubConnectionState.Connected); return Task.CompletedTask; };
                _connection.Closed += error => { StateChanged?.Invoke(HubConnectionState.Disconnected); return Task.CompletedTask; };
            }

            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync(ct);
                StateChanged?.Invoke(_connection.State);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is not null && _connection.State != HubConnectionState.Disconnected)
                await _connection.StopAsync(ct);
            StateChanged?.Invoke(HubConnectionState.Disconnected);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
