namespace OrderWeb.Client.Dialogs;

public partial class NoteDialog : ContentPage
{
    public NoteDialog()
    {
        InitializeComponent();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(false);
    }
}
