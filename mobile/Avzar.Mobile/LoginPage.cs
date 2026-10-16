using Microsoft.Maui.Controls;
namespace Avzar.Mobile;
public sealed class LoginPage : ContentPage
{
    private readonly AvzarApi _api; private readonly OfflineSyncQueue _queue; private readonly Entry _email=new(){Placeholder="Email",Keyboard=Keyboard.Email}; private readonly Entry _password=new(){Placeholder="Password",IsPassword=true}; private readonly Label _status=new(){TextColor=Colors.Firebrick}; private readonly Button _login=new(){Text="Sign in",BackgroundColor=Color.FromArgb("#6D28D9"),TextColor=Colors.White}; private readonly Picker _businessPicker=new(){Title="Choose business",IsVisible=false}; private IReadOnlyList<MobileBusinessChoice> _choices=Array.Empty<MobileBusinessChoice>();
    public LoginPage(AvzarApi api,OfflineSyncQueue queue){_api=api;_queue=queue;Title="Avzar";Content=new ScrollView{Content=new VerticalStackLayout{Padding=28,Spacing=16,VerticalOptions=LayoutOptions.Center,Children={new Label{Text="Avzar",FontSize=32,FontAttributes=FontAttributes.Bold},new Label{Text="Sign in to your tailoring workspace"},_email,_password,_businessPicker,_login,_status}}};_login.Clicked+=SignIn;}
    private async void SignIn(object? sender,EventArgs e)
    {
        _login.IsEnabled=false;_status.Text="Signing in…";
        try
        {
            Guid? businessId=null;
            if(_choices.Count>0){if(_businessPicker.SelectedIndex<0){_status.Text="Choose a business first.";return;}businessId=_choices[_businessPicker.SelectedIndex].BusinessId;}
            var result=await _api.SignInAsync(_email.Text?.Trim()??"",_password.Text??"",businessId);
            await Navigation.PushAsync(new TasksPage(_api,_queue,result.User));
        }
        catch(BusinessSelectionRequiredException choice)
        {
            _choices=choice.Choices;_businessPicker.ItemsSource=_choices.Select(x=>x.BusinessName).ToList();_businessPicker.SelectedIndex=_choices.Count==1?0:-1;_businessPicker.IsVisible=true;_login.Text="Continue";_status.Text="This account belongs to more than one business. Choose the workspace and continue.";
        }
        catch(Exception ex){_status.Text=ex.Message;}
        finally{_login.IsEnabled=true;}
    }
}
