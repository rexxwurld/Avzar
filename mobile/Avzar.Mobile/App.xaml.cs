namespace Avzar.Mobile;
public partial class App : Application
{
    public App(AvzarApi api, OfflineSyncQueue queue) { InitializeComponent(); MainPage = new NavigationPage(new LoginPage(api, queue)); }
}
