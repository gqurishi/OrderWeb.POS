namespace OrderWeb.Client.Dialogs;

public partial class PaymentMethodDialog : ContentPage
{
    public PaymentMethodDialog()
    {
        InitializeComponent();
    }

    public event EventHandler<string>? MethodSelected;

    private async Task SelectMethodAsync(string method)
    {
        MethodSelected?.Invoke(this, method);
        await Navigation.PopModalAsync(false);
    }

    private async void OnCashClicked(object sender, EventArgs e) => await SelectMethodAsync("Cash");
    private async void OnCardClicked(object sender, EventArgs e) => await SelectMethodAsync("Card");
    private async void OnGiftCardClicked(object sender, EventArgs e) => await SelectMethodAsync("Gift Card");
    private async void OnSplitClicked(object sender, EventArgs e) => await SelectMethodAsync("Split");
    private async void OnCloseClicked(object sender, EventArgs e) => await Navigation.PopModalAsync(false);
}
