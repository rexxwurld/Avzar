using System.Text.Json;
namespace Avzar.Mobile;
public sealed class TasksPage : ContentPage
{
    private readonly AvzarApi _api;private readonly OfflineSyncQueue _queue;private readonly MobileUser _user;private readonly VerticalStackLayout _list=new(){Spacing=12};private readonly Label _status=new();private readonly Button _refresh=new(){Text="Refresh / sync"};
    public TasksPage(AvzarApi api,OfflineSyncQueue queue,MobileUser user){_api=api;_queue=queue;_user=user;Title="My tasks";Content=new ScrollView{Content=new VerticalStackLayout{Padding=16,Spacing=14,Children={new Label{Text=$"{user.BusinessName} · {user.FullName}",FontSize=20,FontAttributes=FontAttributes.Bold},_refresh,_status,_list}}};_refresh.Clicked+=async(_,_)=>await LoadTasks();Connectivity.Current.ConnectivityChanged+=OnConnectivityChanged;Appearing+=async(_,_)=>await LoadTasks();ToolbarItems.Add(new ToolbarItem("Sign out",null,async()=>await SignOut()));}
    private async void OnConnectivityChanged(object? sender,ConnectivityChangedEventArgs e){if(e.NetworkAccess==NetworkAccess.Internet)await MainThread.InvokeOnMainThreadAsync(LoadTasks);}
    private async Task LoadTasks(){_refresh.IsEnabled=false;try{if(Connectivity.Current.NetworkAccess==NetworkAccess.Internet){var synced=await _queue.SyncAsync(_api);var result=await _api.GetAsync<JsonElement>("/api/my/tasks?page=1&pageSize=100&show=open");var arr=result.TryGetProperty("items",out var items)?items:default;if(arr.ValueKind==JsonValueKind.Array)await SecureStorage.Default.SetAsync("avzar.mobile.my-tasks-cache",arr.GetRawText());RenderTasks(arr);_status.Text=$"Online · {await _queue.CountAsync()} pending action(s) · synced {synced}";}else{var cache=await SecureStorage.Default.GetAsync("avzar.mobile.my-tasks-cache");if(!string.IsNullOrWhiteSpace(cache)){using var doc=JsonDocument.Parse(cache);RenderTasks(doc.RootElement);} _status.Text=$"Offline · {await _queue.CountAsync()} action(s) waiting to sync. Cached tasks may be stale; queued actions are not final until accepted by the API.";}}catch(Exception ex){_status.Text=ex.Message;}finally{_refresh.IsEnabled=true;}}
    private void RenderTasks(JsonElement arr){_list.Clear();if(arr.ValueKind!=JsonValueKind.Array)return;foreach(var item in arr.EnumerateArray()){var id=item.GetProperty("id").GetGuid();var title=item.GetProperty("title").GetString()??"Production task";var stage=item.GetProperty("stageName").GetString()??"Production";var status=item.GetProperty("status").GetString()??"Pending";var card=new VerticalStackLayout{Spacing=6,Padding=12};card.Add(new Label{Text=title,FontAttributes=FontAttributes.Bold,FontSize=18});card.Add(new Label{Text=$"{stage} · {status}"});foreach(var action in new[]{("Start","start"),("Pause","pause"),("Resume","resume"),("Complete","complete")}){var b=new Button{Text=action.Item1,Padding=8};b.Clicked+=async(_,_)=>await QueueAction(id,action.Item2);card.Add(b);}_list.Add(new Border{Stroke=Color.FromArgb("#dddddd"),StrokeShape=new Microsoft.Maui.Controls.Shapes.RoundRectangle{CornerRadius=12},Padding=8,Content=card});}}
    private async Task SignOut()
    {
        var pending=await _queue.CountAsync();
        if(pending>0)
        {
            var choice=await DisplayActionSheet($"There are {pending} pending offline action(s).", "Cancel", null, "Sync now", "Discard pending actions");
            if(choice=="Cancel"||choice is null)return;
            if(choice=="Sync now")
            {
                if(Connectivity.Current.NetworkAccess!=NetworkAccess.Internet){_status.Text="Connect to the internet before syncing pending actions.";return;}
                await _queue.SyncAsync(_api);if(await _queue.CountAsync()>0){_status.Text="Some actions could not sync. Resolve them before signing out or discard them explicitly.";return;}
            }
            else if(choice=="Discard pending actions")await _queue.ClearAsync();
        }
        await _api.SignOutAsync();await Navigation.PopToRootAsync();
    }
    private async Task QueueAction(Guid id,string action)
    {
        string? note=null;
        if(action=="pause"||action=="complete") note=await DisplayPromptAsync(action=="pause"?"Pause task":"Complete task",action=="pause"?"Why are you pausing?":"Completion notes (optional)",accept:"Save",cancel:"Skip",maxLength:2000);
        if(Connectivity.Current.NetworkAccess==NetworkAccess.Internet)
        {
            try
            {
                switch(action){case "start":await _api.PostAsync<JsonElement>($"/api/my/tasks/{id}/start",new{});break;case "pause":await _api.PostAsync<JsonElement>($"/api/my/tasks/{id}/pause",new{note});break;case "resume":await _api.PostAsync<JsonElement>($"/api/my/tasks/{id}/resume",new{});break;case "complete": {
                    var detail=await _api.GetAsync<JsonElement>($"/api/my/tasks/{id}");
                    var checks=detail.TryGetProperty("requiredChecks",out var required)&&required.ValueKind==JsonValueKind.Array?required.EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToArray():Array.Empty<string>();
                    var completed=new List<string>();
                    foreach(var check in checks){if(!await DisplayAlert("Task checklist",$"Have you completed: {check}?","Yes","Not yet")){_status.Text="Complete the required checklist before closing this task.";return;}completed.Add(check);}
                    await _api.PostAsync<JsonElement>($"/api/my/tasks/{id}/complete",new{notes=note,completedChecks=completed});break;} }
                _status.Text="Action accepted by the server.";
            }
            catch(Exception ex){_status.Text=ex.Message;return;}
        }
        else
        {
            if(action=="complete"){_status.Text="Task completion requires a connection so required checklist items can be validated. Other actions can be queued offline.";return;}
            await _queue.EnqueueAsync(new QueuedTaskAction(id,action,note,DateTime.UtcNow));
            _status.Text="Action queued securely. It is not final until the server accepts it after reconnection.";
        }
        await LoadTasks();
    }
}
