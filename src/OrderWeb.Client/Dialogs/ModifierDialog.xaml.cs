namespace OrderWeb.Client.Dialogs;

public partial class ModifierDialog : ContentPage
{
    public ModifierDialog()
    {
        InitializeComponent();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(false);
    }
}
