namespace OrderWeb.Client.Views.Layout;

public partial class ClientToast : ContentView
{
    public ClientToast()
    {
        InitializeComponent();
    }

    public void Show(string message)
    {
        MessageLabel.Text = message;
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
