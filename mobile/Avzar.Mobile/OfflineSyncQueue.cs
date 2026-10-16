using System.Text.Json;
namespace Avzar.Mobile;
/// <summary>Offline action queue contains only task IDs and user-entered notes, not customer profiles or measurements.</summary>
public sealed class OfflineSyncQueue
{
    private const string QueueKey = "avzar.mobile.pending-actions";
    private readonly SemaphoreSlim _gate = new(1,1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task EnqueueAsync(QueuedTaskAction action)
    {
        await _gate.WaitAsync(); try { var list=await ReadAsync(); list.Add(action); await SecureStorage.Default.SetAsync(QueueKey,JsonSerializer.Serialize(list,JsonOptions)); } finally {_gate.Release();}
    }
    public async Task<int> CountAsync(){await _gate.WaitAsync();try{return (await ReadAsync()).Count;}finally{_gate.Release();}}
    public async Task ClearAsync(){await _gate.WaitAsync();try{SecureStorage.Default.Remove(QueueKey);}finally{_gate.Release();}}
    public async Task<int> SyncAsync(AvzarApi api, CancellationToken ct=default)
    {
        if(Connectivity.Current.NetworkAccess!=NetworkAccess.Internet)return 0;
        await _gate.WaitAsync(ct);try{var queue=await ReadAsync();var done=0;foreach(var action in queue.ToArray()){ct.ThrowIfCancellationRequested();try{await Send(api,action,ct);queue.Remove(action);done++;}catch(HttpRequestException){break;}catch(InvalidOperationException){break;}}
            await SecureStorage.Default.SetAsync(QueueKey,JsonSerializer.Serialize(queue,JsonOptions));return done;
        }finally{_gate.Release();}
    }
    private static async Task Send(AvzarApi api,QueuedTaskAction a,CancellationToken ct)
    {
        switch(a.Action){case "start":await api.PostAsync<object>($"/api/my/tasks/{a.TaskId}/start",new{},ct);break;case "pause":await api.PostAsync<object>($"/api/my/tasks/{a.TaskId}/pause",new{note=a.Note},ct);break;case "resume":await api.PostAsync<object>($"/api/my/tasks/{a.TaskId}/resume",new{},ct);break;case "complete":await api.PostAsync<object>($"/api/my/tasks/{a.TaskId}/complete",new{notes=a.Note,completedChecks=Array.Empty<string>()},ct);break;default:throw new InvalidOperationException("Unknown queued action.");}
    }
    private static async Task<List<QueuedTaskAction>> ReadAsync(){try{var raw=await SecureStorage.Default.GetAsync(QueueKey);if(string.IsNullOrWhiteSpace(raw))return new();return JsonSerializer.Deserialize<List<QueuedTaskAction>>(raw,JsonOptions)??new();}catch(JsonException){return new();}}
}
