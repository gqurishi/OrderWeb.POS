namespace OrderWeb.Client.Dialogs;

public partial class MoreOptionsDialog : ContentPage
{
    public MoreOptionsDialog()
    {
        InitializeComponent();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(false);
    }
}
