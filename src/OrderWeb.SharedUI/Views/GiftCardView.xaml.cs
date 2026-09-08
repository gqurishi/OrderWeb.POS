namespace OrderWeb.SharedUI.Views;

public enum GiftCardFlowKind
{
    Activate,
    Sell,
    TopUp,
    Redeem
}

public partial class GiftCardView : ContentView
{
    private static readonly string[] PaymentMethods = ["cash", "card"];

    public GiftCardView()
    {
        InitializeComponent();
        InitializePaymentPickers();
        ShowFlow(GiftCardFlowKind.Activate);
    }

    public GiftCardFlowKind CurrentFlow { get; private set; } = GiftCardFlowKind.Activate;

    public event EventHandler<GiftCardFlowKind>? FlowChanged;
    public event EventHandler? ActivateLookupRequested;
    public event EventHandler? ActivateRequested;
    public event EventHandler? GenerateSellCardRequested;
    public event EventHandler? SellRequested;
    public event EventHandler? TopUpLookupRequested;
    public event EventHandler? TopUpRequested;
    public event EventHandler? RedeemLookupRequested;
    public event EventHandler? RedeemRequested;

    public Entry ActivateCardEntry => ActivateCardEntryControl;
    public Button ActivateLookupButton => ActivateLookupButtonControl;
    public Label ActivateStatusLabel => ActivateStatusLabelControl;
    public Entry ActivateAmountEntry => ActivateAmountEntryControl;
    public Picker ActivatePaymentMethodPicker => ActivatePaymentMethodPickerControl;
    public Entry ActivateOrderIdEntry => ActivateOrderIdEntryControl;
    public Button ActivateActionButton => ActivateActionButtonControl;
    public Label ActivateReceiptLabel => ActivateReceiptLabelControl;

    public Entry SellCardEntry => SellCardEntryControl;
    public Entry SellAmountEntry => SellAmountEntryControl;
    public Picker SellPaymentMethodPicker => SellPaymentMethodPickerControl;
    public Entry SellOrderIdEntry => SellOrderIdEntryControl;
    public Button SellActionButton => SellActionButtonControl;
    public Label SellStatusLabel => SellStatusLabelControl;
    public Label SellReceiptLabel => SellReceiptLabelControl;

    public Entry TopUpCardEntry => TopUpCardEntryControl;
    public Button TopUpLookupButton => TopUpLookupButtonControl;
    public Label TopUpStatusLabel => TopUpStatusLabelControl;
    public Entry TopUpAmountEntry => TopUpAmountEntryControl;
    public Picker TopUpPaymentMethodPicker => TopUpPaymentMethodPickerControl;
    public Entry TopUpOrderIdEntry => TopUpOrderIdEntryControl;
    public Button TopUpActionButton => TopUpActionButtonControl;
    public Label TopUpReceiptLabel => TopUpReceiptLabelControl;

    public Entry RedeemCardEntry => RedeemCardEntryControl;
    public Button RedeemLookupButton => RedeemLookupButtonControl;
    public Label RedeemBalanceLabel => RedeemBalanceLabelControl;
    public Label RedeemCardStatusLabel => RedeemCardStatusLabelControl;
    public Entry RedeemAmountEntry => RedeemAmountEntryControl;
    public Entry RedeemOrderIdEntry => RedeemOrderIdEntryControl;
    public Button RedeemActionButton => RedeemActionButtonControl;
    public Label RedeemStatusLabel => RedeemStatusLabelControl;

    public string FlowTitle => CurrentFlow switch
    {
        GiftCardFlowKind.Activate => "Activate",
        GiftCardFlowKind.Sell => "Sell card",
        GiftCardFlowKind.TopUp => "Top-up card",
        GiftCardFlowKind.Redeem => "Redeem card",
        _ => "Gift Cards"
    };

    public void ShowFlow(GiftCardFlowKind flow)
    {
        CurrentFlow = flow;

        var isSellTopUp = flow is GiftCardFlowKind.Sell or GiftCardFlowKind.TopUp;
        ActivateScreen.IsVisible = flow == GiftCardFlowKind.Activate;
        SellTopUpScreen.IsVisible = isSellTopUp;
        RedeemScreen.IsVisible = flow == GiftCardFlowKind.Redeem;

        SellScreen.IsVisible = flow == GiftCardFlowKind.Sell;
        TopUpScreen.IsVisible = flow == GiftCardFlowKind.TopUp;

        SetFlowButtonState(ActivateFlowButton, flow == GiftCardFlowKind.Activate);
        SetFlowButtonState(SellTopUpFlowButton, isSellTopUp);
        SetFlowButtonState(RedeemFlowButton, flow == GiftCardFlowKind.Redeem);

        if (isSellTopUp)
        {
            SetFlowButtonState(SellModeButton, flow == GiftCardFlowKind.Sell);
            SetFlowButtonState(TopUpModeButton, flow == GiftCardFlowKind.TopUp);
        }

        FlowChanged?.Invoke(this, flow);
    }

    public static void SetFlowStatus(Label label, string message, bool isError)
    {
        label.Text = message;
        label.TextColor = isError ? Color.FromArgb("#DC2626") : Color.FromArgb("#047857");
    }

    private void InitializePaymentPickers()
    {
        foreach (var picker in new[] { ActivatePaymentMethodPickerControl, SellPaymentMethodPickerControl, TopUpPaymentMethodPickerControl })
        {
            picker.Items.Clear();
            foreach (var method in PaymentMethods)
            {
                picker.Items.Add(method);
            }

            picker.SelectedIndex = 0;
        }

        ApplyPaymentMethod(SellPaymentMethodPickerControl, SellCashButton, SellCardButton, "cash");
        ApplyPaymentMethod(TopUpPaymentMethodPickerControl, TopUpCashButton, TopUpCardButton, "cash");
    }

    private static void SetFlowButtonState(Button button, bool active)
    {
        if (active)
        {
            button.SetDynamicResource(BackgroundColorProperty, "OwPrimary");
            button.SetDynamicResource(Button.TextColorProperty, "OwTextOnPrimary");
            button.BorderWidth = 0;
        }
        else
        {
            button.SetDynamicResource(BackgroundColorProperty, "OwSurface");
            button.SetDynamicResource(Button.TextColorProperty, "OwTextStrong");
            button.SetDynamicResource(Button.BorderColorProperty, "OwBorderStrong");
            button.BorderWidth = 1;
        }
    }

    private static void ApplyPaymentMethod(Picker picker, Button cashButton, Button cardButton, string method)
    {
        var normalized = string.Equals(method, "card", StringComparison.OrdinalIgnoreCase) ? "card" : "cash";
        picker.SelectedIndex = normalized == "card" ? 1 : 0;
        SetFlowButtonState(cashButton, normalized == "cash");
        SetFlowButtonState(cardButton, normalized == "card");
    }

    private void OnActivateFlowClicked(object? sender, EventArgs e) => ShowFlow(GiftCardFlowKind.Activate);
    private void OnSellTopUpFlowClicked(object? sender, EventArgs e) => ShowFlow(GiftCardFlowKind.Sell);
    private void OnSellModeClicked(object? sender, EventArgs e) => ShowFlow(GiftCardFlowKind.Sell);
    private void OnTopUpModeClicked(object? sender, EventArgs e) => ShowFlow(GiftCardFlowKind.TopUp);
    private void OnRedeemFlowClicked(object? sender, EventArgs e) => ShowFlow(GiftCardFlowKind.Redeem);

    private void OnSellCashClicked(object? sender, EventArgs e) =>
        ApplyPaymentMethod(SellPaymentMethodPickerControl, SellCashButton, SellCardButton, "cash");

    private void OnSellCardPayClicked(object? sender, EventArgs e) =>
        ApplyPaymentMethod(SellPaymentMethodPickerControl, SellCashButton, SellCardButton, "card");

    private void OnTopUpCashClicked(object? sender, EventArgs e) =>
        ApplyPaymentMethod(TopUpPaymentMethodPickerControl, TopUpCashButton, TopUpCardButton, "cash");

    private void OnTopUpCardPayClicked(object? sender, EventArgs e) =>
        ApplyPaymentMethod(TopUpPaymentMethodPickerControl, TopUpCashButton, TopUpCardButton, "card");

    private void OnActivateLookupClicked(object? sender, EventArgs e) => ActivateLookupRequested?.Invoke(this, EventArgs.Empty);
    private void OnActivateCardClicked(object? sender, EventArgs e) => ActivateRequested?.Invoke(this, EventArgs.Empty);
    private void OnGenerateSellCardClicked(object? sender, EventArgs e) => GenerateSellCardRequested?.Invoke(this, EventArgs.Empty);
    private void OnSellCardClicked(object? sender, EventArgs e) => SellRequested?.Invoke(this, EventArgs.Empty);
    private void OnTopUpLookupClicked(object? sender, EventArgs e) => TopUpLookupRequested?.Invoke(this, EventArgs.Empty);
    private void OnTopUpCardClicked(object? sender, EventArgs e) => TopUpRequested?.Invoke(this, EventArgs.Empty);
    private void OnRedeemLookupClicked(object? sender, EventArgs e) => RedeemLookupRequested?.Invoke(this, EventArgs.Empty);
    private void OnRedeemCardClicked(object? sender, EventArgs e) => RedeemRequested?.Invoke(this, EventArgs.Empty);
}
