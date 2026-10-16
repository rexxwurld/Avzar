namespace Avzar.Mobile;
public partial class App : Application
{
    public App(AvzarApi api, OfflineSyncQueue queue, BusinessEventsClient events) { InitializeComponent(); MainPage = new NavigationPage(new LoginPage(api, queue, events)); }
}
