using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class PrintTemplatesPage : ContentPage
{
    private KitchenTicketPreviewMode _previewMode = KitchenTicketPreviewMode.Table;

    public PrintTemplatesPage()
    {
        InitializeComponent();
        SetKitchenTemplate(KitchenTicketPreviewMode.Table);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TopBar.SetPageTitle("Print Templates");
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
            return;
        }

        await Shell.Current.GoToAsync("//printersetup", false);
    }

    private void OnTablePreviewClicked(object? sender, EventArgs e)
    {
        SetKitchenTemplate(KitchenTicketPreviewMode.Table);
    }

    private void OnCollectionPreviewClicked(object? sender, EventArgs e)
    {
        SetKitchenTemplate(KitchenTicketPreviewMode.Collection);
    }

    private void OnDeliveryPreviewClicked(object? sender, EventArgs e)
    {
        SetKitchenTemplate(KitchenTicketPreviewMode.Delivery);
    }

    private void OnKitchenTemplateClicked(object? sender, EventArgs e)
    {
        SetKitchenTemplate(_previewMode);
    }

    private void OnCollectionReceiptTemplateClicked(object? sender, EventArgs e)
    {
        SetCustomerReceiptTemplate(CustomerReceiptKind.Collection);
    }

    private void OnDeliveryReceiptTemplateClicked(object? sender, EventArgs e)
    {
        SetCustomerReceiptTemplate(CustomerReceiptKind.Delivery);
    }

    private void OnTableBillTemplateClicked(object? sender, EventArgs e)
    {
        SetCustomerReceiptTemplate(CustomerReceiptKind.TableBill);
    }

    private void OnTablePaymentTemplateClicked(object? sender, EventArgs e)
    {
        SetCustomerReceiptTemplate(CustomerReceiptKind.TablePayment);
    }

    private void SetKitchenTemplate(KitchenTicketPreviewMode mode)
    {
        _previewMode = mode;
        PageHeadingLabel.Text = "Kitchen Template";
        PageSubheadingLabel.Text = "80mm kitchen ticket preview";
        KitchenPreviewModes.IsVisible = true;
        PreviewTextLabel.Text = KitchenTicketTemplateService.BuildPreview(mode);
        PreviewTitleLabel.Text = mode switch
        {
            KitchenTicketPreviewMode.Table => "Table kitchen ticket",
            KitchenTicketPreviewMode.Delivery => "Delivery kitchen ticket",
            _ => "Collection kitchen ticket"
        };
        RulesTitleLabel.Text = "Kitchen Rules";
        RuleOneLabel.Text = "Table orders cut after each section ticket.";
        RuleTwoLabel.Text = "Collection and delivery print on one ticket.";
        RuleThreeLabel.Text = "No money, VAT, payment, or customer contact details.";
        RuleFourLabel.Text = "Every ticket keeps order number, date, time, items, checked-by line, and footer.";
        NextStepLabel.Text = "Collection receipt is ready to preview here; delivery and table receipts come after.";

        StylePreviewButton(KitchenTemplateButton, true);
        StylePreviewButton(CollectionReceiptTemplateButton, false);
        StylePreviewButton(DeliveryReceiptTemplateButton, false);
        StylePreviewButton(TableBillTemplateButton, false);
        StylePreviewButton(TablePaymentTemplateButton, false);
        StylePreviewButton(TablePreviewButton, mode == KitchenTicketPreviewMode.Table);
        StylePreviewButton(CollectionPreviewButton, mode == KitchenTicketPreviewMode.Collection);
        StylePreviewButton(DeliveryPreviewButton, mode == KitchenTicketPreviewMode.Delivery);
    }

    private void SetCustomerReceiptTemplate(CustomerReceiptKind receiptKind)
    {
        var isDelivery = receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = receiptKind == CustomerReceiptKind.TablePayment;
        PageHeadingLabel.Text = receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery Receipt",
            CustomerReceiptKind.TableBill => "Table Bill",
            CustomerReceiptKind.TablePayment => "Table Payment Receipt",
            _ => "Collection Receipt"
        };
        PageSubheadingLabel.Text = "80mm customer receipt preview";
        KitchenPreviewModes.IsVisible = false;
        PreviewTextLabel.Text = CollectionReceiptTemplateService.BuildPreview(receiptKind);
        PreviewTitleLabel.Text = receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery customer receipt",
            CustomerReceiptKind.TableBill => "Table bill",
            CustomerReceiptKind.TablePayment => "Final paid table receipt",
            _ => "Collection customer receipt"
        };
        RulesTitleLabel.Text = receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery Receipt Rules",
            CustomerReceiptKind.TableBill => "Table Bill Rules",
            CustomerReceiptKind.TablePayment => "Payment Receipt Rules",
            _ => "Collection Receipt Rules"
        };
        RuleOneLabel.Text = "Logo/header uses the restaurant business details from Admin settings.";
        RuleTwoLabel.Text = isTableBill || isTablePayment
            ? "Shows order number, date, time, table number, covers, and priced items."
            : isDelivery
            ? "Shows order number, date, time, customer name, phone, and full delivery address."
            : "Shows order number, date, time, customer name, and phone only.";
        RuleThreeLabel.Text = isTableBill || isTablePayment
            ? "Service charge prints when added; otherwise it prints service charge not included."
            : "Items print with prices, then subtotal, discount, fee, tip, and total.";
        RuleFourLabel.Text = isTablePayment
            ? "Prints only after final payment, says PAID, and lists Cash, Card, Gift Card, or split payments."
            : isTableBill
            ? "No customer details, customer note, or payment section before final payment."
            : isDelivery
            ? "Customer note prints after payment and before the thank-you footer. No paid/unpaid status."
            : "Payment prints as Cash, Card, Gift Card, or split methods. No paid/unpaid status.";
        NextStepLabel.Text = "Table bill and final paid table receipt are ready to preview here.";

        StylePreviewButton(KitchenTemplateButton, false);
        StylePreviewButton(CollectionReceiptTemplateButton, receiptKind == CustomerReceiptKind.Collection);
        StylePreviewButton(DeliveryReceiptTemplateButton, receiptKind == CustomerReceiptKind.Delivery);
        StylePreviewButton(TableBillTemplateButton, receiptKind == CustomerReceiptKind.TableBill);
        StylePreviewButton(TablePaymentTemplateButton, receiptKind == CustomerReceiptKind.TablePayment);
    }

    private static void StylePreviewButton(Button button, bool isSelected)
    {
        button.BackgroundColor = Color.FromArgb(isSelected ? "#0F172A" : "#E2E8F0");
        button.TextColor = Color.FromArgb(isSelected ? "#FFFFFF" : "#334155");
    }
}
