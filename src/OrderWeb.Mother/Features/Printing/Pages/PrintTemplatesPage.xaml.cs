using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class PrintTemplatesPage : ContentPage
{
    private const int PreviewLineWidth = 48;
    private const double PreviewFontSize = 14;
    private const double PreviewHeadingLargeFontSize = 22;
    private const double PreviewHeadingExtraLargeFontSize = 30;

    private readonly KitchenTemplateSettingsService _kitchenSettingsService;
    private readonly CollectionReceiptTemplateSettingsService _collectionSettingsService;
    private readonly DeliveryReceiptTemplateSettingsService _deliverySettingsService;
    private readonly TableBillReceiptTemplateSettingsService _tableBillSettingsService;
    private readonly TablePaymentReceiptTemplateSettingsService _tablePaymentSettingsService;
    private KitchenTicketPreviewMode _previewMode = KitchenTicketPreviewMode.Table;
    private CustomerReceiptKind _receiptKind = CustomerReceiptKind.Collection;
    private KitchenTemplateSettings _kitchenSettings = KitchenTemplateSettings.Default();
    private CollectionReceiptTemplateSettings _collectionSettings = CollectionReceiptTemplateSettings.Default();
    private CollectionReceiptTemplateSettings _deliverySettings = CollectionReceiptTemplateSettings.Default();
    private CollectionReceiptTemplateSettings _tableBillSettings = CollectionReceiptTemplateSettings.Default();
    private CollectionReceiptTemplateSettings _tablePaymentSettings = CollectionReceiptTemplateSettings.Default();
    private bool _isApplyingKitchenSettings;
    private bool _isApplyingCollectionSettings;
    private bool _hasLoadedKitchenSettings;
    private bool _hasLoadedCollectionSettings;
    private bool _hasLoadedDeliverySettings;
    private bool _hasLoadedTableBillSettings;
    private bool _hasLoadedTablePaymentSettings;

    public PrintTemplatesPage()
    {
        InitializeComponent();
        _kitchenSettingsService = ServiceHelper.GetService<KitchenTemplateSettingsService>()
            ?? new KitchenTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
        _collectionSettingsService = ServiceHelper.GetService<CollectionReceiptTemplateSettingsService>()
            ?? new CollectionReceiptTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
        _deliverySettingsService = ServiceHelper.GetService<DeliveryReceiptTemplateSettingsService>()
            ?? new DeliveryReceiptTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
        _tableBillSettingsService = ServiceHelper.GetService<TableBillReceiptTemplateSettingsService>()
            ?? new TableBillReceiptTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
        _tablePaymentSettingsService = ServiceHelper.GetService<TablePaymentReceiptTemplateSettingsService>()
            ?? new TablePaymentReceiptTemplateSettingsService(ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService());
        ApplyKitchenSettingsToControls();
        ApplyCustomerReceiptSettingsToControls();
        SetKitchenTemplate(KitchenTicketPreviewMode.Table);
    }

    private void OnPrintTemplatesPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var tablet = Width <= 1280 || Height <= 800;
        TemplateLayoutGrid.ColumnDefinitions.Clear();
        TemplateLayoutGrid.RowDefinitions.Clear();

        if (tablet)
        {
            TemplateLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            TemplateLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TemplateLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TemplateLayoutGrid.Padding = new Thickness(16);
            TemplateLayoutGrid.ColumnSpacing = 0;
            TemplateLayoutGrid.RowSpacing = 18;
            Grid.SetColumn(TemplateSettingsPanel, 0);
            Grid.SetRow(TemplateSettingsPanel, 1);
            return;
        }

        TemplateLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        TemplateLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(420));
        TemplateLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TemplateLayoutGrid.Padding = new Thickness(32);
        TemplateLayoutGrid.ColumnSpacing = 28;
        TemplateLayoutGrid.RowSpacing = 0;
        Grid.SetColumn(TemplateSettingsPanel, 1);
        Grid.SetRow(TemplateSettingsPanel, 0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TopBar.SetPageTitle("Print Templates");
        await LoadKitchenSettingsAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
        {
            await NavigationCoordinator.Shared.PopTemporaryPageAsync(Navigation, source: sender as VisualElement);
            return;
        }

        await NavigationCoordinator.Shared.NavigateShellAsync("printersetup", animated: false, source: sender as VisualElement);
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
        KitchenControlsPanel.IsVisible = true;
        CollectionControlsPanel.IsVisible = false;
        RefreshKitchenPreview();
        PreviewTitleLabel.Text = mode switch
        {
            KitchenTicketPreviewMode.Table => "Table kitchen ticket",
            KitchenTicketPreviewMode.Delivery => "Delivery kitchen ticket",
            _ => "Collection kitchen ticket"
        };
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
        _receiptKind = receiptKind;
        PageHeadingLabel.Text = receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery Receipt",
            CustomerReceiptKind.TableBill => "Table Bill",
            CustomerReceiptKind.TablePayment => "Table Payment Receipt",
            _ => "Collection Receipt"
        };
        PageSubheadingLabel.Text = "80mm customer receipt preview";
        KitchenPreviewModes.IsVisible = false;
        KitchenControlsPanel.IsVisible = false;
        CollectionControlsPanel.IsVisible = IsEditableCustomerReceipt;

        if (IsEditableCustomerReceipt)
        {
            PreviewTextLabel.IsVisible = false;
            PreviewTextLabel.Text = null;
            PreviewTextLabel.FormattedText = null;
            KitchenPreviewLines.IsVisible = true;
            ApplyCustomerReceiptSettingsToControls();
            RenderCustomerReceiptPreview();

            if (receiptKind == CustomerReceiptKind.Delivery)
            {
                _ = LoadDeliverySettingsAsync();
            }
            else if (receiptKind == CustomerReceiptKind.TableBill)
            {
                _ = LoadTableBillSettingsAsync();
            }
            else if (receiptKind == CustomerReceiptKind.TablePayment)
            {
                _ = LoadTablePaymentSettingsAsync();
            }
            else
            {
                _ = LoadCollectionSettingsAsync();
            }
        }
        else
        {
            KitchenPreviewLines.IsVisible = false;
            KitchenPreviewLines.Children.Clear();
            PreviewTextLabel.IsVisible = true;
            PreviewTextLabel.FormattedText = null;
            PreviewTextLabel.Text = CollectionReceiptTemplateService.BuildPreview(receiptKind);
        }
        PreviewTitleLabel.Text = receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery customer receipt",
            CustomerReceiptKind.TableBill => "Table bill",
            CustomerReceiptKind.TablePayment => "Final paid table receipt",
            _ => "Collection customer receipt"
        };
        StylePreviewButton(KitchenTemplateButton, false);
        StylePreviewButton(CollectionReceiptTemplateButton, receiptKind == CustomerReceiptKind.Collection);
        StylePreviewButton(DeliveryReceiptTemplateButton, receiptKind == CustomerReceiptKind.Delivery);
        StylePreviewButton(TableBillTemplateButton, receiptKind == CustomerReceiptKind.TableBill);
        StylePreviewButton(TablePaymentTemplateButton, receiptKind == CustomerReceiptKind.TablePayment);
    }

    private bool IsEditableCustomerReceipt =>
        _receiptKind is CustomerReceiptKind.Collection
            or CustomerReceiptKind.Delivery
            or CustomerReceiptKind.TableBill
            or CustomerReceiptKind.TablePayment;

    private CollectionReceiptTemplateSettings ActiveCustomerReceiptSettings =>
        _receiptKind switch
        {
            CustomerReceiptKind.Delivery => _deliverySettings,
            CustomerReceiptKind.TableBill => _tableBillSettings,
            CustomerReceiptKind.TablePayment => _tablePaymentSettings,
            _ => _collectionSettings
        };

    private void SetActiveCustomerReceiptSettings(CollectionReceiptTemplateSettings settings)
    {
        if (_receiptKind == CustomerReceiptKind.Delivery)
        {
            _deliverySettings = settings;
            return;
        }

        if (_receiptKind == CustomerReceiptKind.TableBill)
        {
            _tableBillSettings = settings;
            return;
        }

        if (_receiptKind == CustomerReceiptKind.TablePayment)
        {
            _tablePaymentSettings = settings;
            return;
        }

        _collectionSettings = settings;
    }

    private static void StylePreviewButton(Button button, bool isSelected)
    {
        button.BackgroundColor = Color.FromArgb(isSelected ? "#0F172A" : "#E2E8F0");
        button.TextColor = Color.FromArgb(isSelected ? "#FFFFFF" : "#334155");
    }

    private async Task LoadKitchenSettingsAsync()
    {
        if (_hasLoadedKitchenSettings)
        {
            return;
        }

        _kitchenSettings = await _kitchenSettingsService.GetSettingsAsync();
        ApplyKitchenSettingsToControls();
        RefreshKitchenPreview();
        _hasLoadedKitchenSettings = true;
    }

    private async Task LoadCollectionSettingsAsync()
    {
        if (_hasLoadedCollectionSettings)
        {
            return;
        }

        _collectionSettings = await _collectionSettingsService.GetSettingsAsync();
        ApplyCustomerReceiptSettingsToControls();
        if (_receiptKind == CustomerReceiptKind.Collection)
        {
            RenderCustomerReceiptPreview();
        }

        _hasLoadedCollectionSettings = true;
    }

    private async Task LoadDeliverySettingsAsync()
    {
        if (_hasLoadedDeliverySettings)
        {
            return;
        }

        _deliverySettings = await _deliverySettingsService.GetSettingsAsync();
        ApplyCustomerReceiptSettingsToControls();
        if (_receiptKind == CustomerReceiptKind.Delivery)
        {
            RenderCustomerReceiptPreview();
        }

        _hasLoadedDeliverySettings = true;
    }

    private async Task LoadTableBillSettingsAsync()
    {
        if (_hasLoadedTableBillSettings)
        {
            return;
        }

        _tableBillSettings = await _tableBillSettingsService.GetSettingsAsync();
        ApplyCustomerReceiptSettingsToControls();
        if (_receiptKind == CustomerReceiptKind.TableBill)
        {
            RenderCustomerReceiptPreview();
        }

        _hasLoadedTableBillSettings = true;
    }

    private async Task LoadTablePaymentSettingsAsync()
    {
        if (_hasLoadedTablePaymentSettings)
        {
            return;
        }

        _tablePaymentSettings = await _tablePaymentSettingsService.GetSettingsAsync();
        ApplyCustomerReceiptSettingsToControls();
        if (_receiptKind == CustomerReceiptKind.TablePayment)
        {
            RenderCustomerReceiptPreview();
        }

        _hasLoadedTablePaymentSettings = true;
    }

    private void ApplyKitchenSettingsToControls()
    {
        _isApplyingKitchenSettings = true;
        HeadingSizePicker.SelectedIndex = _kitchenSettings.HeadingSize switch
        {
            KitchenHeadingSize.Normal => 0,
            KitchenHeadingSize.ExtraLarge => 2,
            _ => 1
        };
        HeadingBoldSwitch.IsToggled = _kitchenSettings.HeadingBold;
        OrderInfoSizePicker.SelectedIndex = GetSizeIndex(_kitchenSettings.OrderInfoSize);
        OrderInfoBoldSwitch.IsToggled = _kitchenSettings.OrderInfoBold;
        SectionSizePicker.SelectedIndex = GetSizeIndex(_kitchenSettings.SectionHeadingSize);
        SectionBoldSwitch.IsToggled = _kitchenSettings.SectionHeadingBold;
        CheckedBySwitch.IsToggled = _kitchenSettings.ShowCheckedByLine;
        FooterTextEntry.Text = _kitchenSettings.FooterText;
        UpdateFooterCounter();
        _isApplyingKitchenSettings = false;
    }

    private KitchenTemplateSettings ReadKitchenSettingsFromControls()
    {
        return new KitchenTemplateSettings
        {
            HeadingSize = HeadingSizePicker.SelectedIndex switch
            {
                0 => KitchenHeadingSize.Normal,
                2 => KitchenHeadingSize.ExtraLarge,
                _ => KitchenHeadingSize.Large
            },
            HeadingBold = HeadingBoldSwitch.IsToggled,
            OrderInfoSize = GetSizeFromIndex(OrderInfoSizePicker.SelectedIndex),
            OrderInfoBold = OrderInfoBoldSwitch.IsToggled,
            SectionHeadingSize = GetSizeFromIndex(SectionSizePicker.SelectedIndex),
            SectionHeadingBold = SectionBoldSwitch.IsToggled,
            ShowCheckedByLine = CheckedBySwitch.IsToggled,
            FooterText = FooterTextEntry.Text ?? string.Empty
        }.Normalized();
    }

    private void ApplyCustomerReceiptSettingsToControls()
    {
        var settings = ActiveCustomerReceiptSettings;
        var isDelivery = _receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = _receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = _receiptKind == CustomerReceiptKind.TablePayment;
        var isTableReceipt = isTableBill || isTablePayment;

        _isApplyingCollectionSettings = true;
        CustomerReceiptControlsTitleLabel.Text = _receiptKind switch
        {
            CustomerReceiptKind.Delivery => "Delivery Customise",
            CustomerReceiptKind.TableBill => "Table Bill Customise",
            CustomerReceiptKind.TablePayment => "Table Payment Customise",
            _ => "Collection Customise"
        };
        CustomerNameSizeControls.IsVisible = !isTableReceipt;
        CustomerNameBoldControls.IsVisible = !isTableReceipt;
        CustomerPhoneSizeControls.IsVisible = !isTableReceipt;
        CustomerPhoneBoldControls.IsVisible = !isTableReceipt;
        PaidStatusSizeControls.IsVisible = isTablePayment;
        PaidStatusBoldControls.IsVisible = isTablePayment;
        TableNumberSizeControls.IsVisible = isTableReceipt;
        TableNumberBoldControls.IsVisible = isTableReceipt;
        DeliveryAddressSizeControls.IsVisible = isDelivery;
        DeliveryAddressBoldControls.IsVisible = isDelivery;
        ServiceChargeSizeControls.IsVisible = isTableReceipt;
        ServiceChargeBoldControls.IsVisible = isTableReceipt;
        PaymentSizeControls.IsVisible = !isTableBill;
        PaymentBoldControls.IsVisible = !isTableBill;
        PaymentControlsLabel.Text = isTablePayment ? "Payment details" : "Payment";
        CollectionBusinessNameSizePicker.SelectedIndex = GetSizeIndex(settings.BusinessNameSize);
        CollectionBusinessNameBoldSwitch.IsToggled = settings.BusinessNameBold;
        CollectionAddressSizePicker.SelectedIndex = GetSizeIndex(settings.AddressSize);
        CollectionAddressBoldSwitch.IsToggled = settings.AddressBold;
        CollectionPhoneSizePicker.SelectedIndex = GetSizeIndex(settings.PhoneSize);
        CollectionPhoneBoldSwitch.IsToggled = settings.PhoneBold;
        CollectionVatSizePicker.SelectedIndex = GetSizeIndex(settings.VatSize);
        CollectionVatBoldSwitch.IsToggled = settings.VatBold;
        CollectionHeadingSizePicker.SelectedIndex = GetSizeIndex(settings.HeadingSize);
        CollectionHeadingBoldSwitch.IsToggled = settings.HeadingBold;
        CollectionOrderInfoSizePicker.SelectedIndex = GetSizeIndex(settings.OrderInfoSize);
        CollectionOrderInfoBoldSwitch.IsToggled = settings.OrderInfoBold;
        CollectionCustomerNameSizePicker.SelectedIndex = GetSizeIndex(settings.CustomerNameSize);
        CollectionCustomerNameBoldSwitch.IsToggled = settings.CustomerNameBold;
        CollectionCustomerPhoneSizePicker.SelectedIndex = GetSizeIndex(settings.CustomerPhoneSize);
        CollectionCustomerPhoneBoldSwitch.IsToggled = settings.CustomerPhoneBold;
        DeliveryAddressSizePicker.SelectedIndex = GetSizeIndex(settings.DeliveryAddressSize);
        DeliveryAddressBoldSwitch.IsToggled = settings.DeliveryAddressBold;
        TableNumberSizePicker.SelectedIndex = GetSizeIndex(settings.TableNumberSize);
        TableNumberBoldSwitch.IsToggled = settings.TableNumberBold;
        ServiceChargeSizePicker.SelectedIndex = GetSizeIndex(settings.ServiceChargeSize);
        ServiceChargeBoldSwitch.IsToggled = settings.ServiceChargeBold;
        PaidStatusSizePicker.SelectedIndex = GetSizeIndex(settings.PaidStatusSize);
        PaidStatusBoldSwitch.IsToggled = settings.PaidStatusBold;
        CollectionPaymentSizePicker.SelectedIndex = GetSizeIndex(settings.PaymentSize);
        CollectionPaymentBoldSwitch.IsToggled = settings.PaymentBold;
        CollectionFooterTextEntry.Text = settings.FooterText;
        UpdateCollectionFooterCounter();
        _isApplyingCollectionSettings = false;
    }

    private CollectionReceiptTemplateSettings ReadCustomerReceiptSettingsFromControls()
    {
        return new CollectionReceiptTemplateSettings
        {
            BusinessNameSize = GetSizeFromIndex(CollectionBusinessNameSizePicker.SelectedIndex),
            BusinessNameBold = CollectionBusinessNameBoldSwitch.IsToggled,
            AddressSize = GetSizeFromIndex(CollectionAddressSizePicker.SelectedIndex),
            AddressBold = CollectionAddressBoldSwitch.IsToggled,
            PhoneSize = GetSizeFromIndex(CollectionPhoneSizePicker.SelectedIndex),
            PhoneBold = CollectionPhoneBoldSwitch.IsToggled,
            VatSize = GetSizeFromIndex(CollectionVatSizePicker.SelectedIndex),
            VatBold = CollectionVatBoldSwitch.IsToggled,
            HeadingSize = GetSizeFromIndex(CollectionHeadingSizePicker.SelectedIndex),
            HeadingBold = CollectionHeadingBoldSwitch.IsToggled,
            OrderInfoSize = GetSizeFromIndex(CollectionOrderInfoSizePicker.SelectedIndex),
            OrderInfoBold = CollectionOrderInfoBoldSwitch.IsToggled,
            CustomerNameSize = GetSizeFromIndex(CollectionCustomerNameSizePicker.SelectedIndex),
            CustomerNameBold = CollectionCustomerNameBoldSwitch.IsToggled,
            CustomerPhoneSize = GetSizeFromIndex(CollectionCustomerPhoneSizePicker.SelectedIndex),
            CustomerPhoneBold = CollectionCustomerPhoneBoldSwitch.IsToggled,
            DeliveryAddressSize = GetSizeFromIndex(DeliveryAddressSizePicker.SelectedIndex),
            DeliveryAddressBold = DeliveryAddressBoldSwitch.IsToggled,
            TableNumberSize = GetSizeFromIndex(TableNumberSizePicker.SelectedIndex),
            TableNumberBold = TableNumberBoldSwitch.IsToggled,
            ServiceChargeSize = GetSizeFromIndex(ServiceChargeSizePicker.SelectedIndex),
            ServiceChargeBold = ServiceChargeBoldSwitch.IsToggled,
            PaidStatusSize = GetSizeFromIndex(PaidStatusSizePicker.SelectedIndex),
            PaidStatusBold = PaidStatusBoldSwitch.IsToggled,
            PaymentSize = GetSizeFromIndex(CollectionPaymentSizePicker.SelectedIndex),
            PaymentBold = CollectionPaymentBoldSwitch.IsToggled,
            FooterText = CollectionFooterTextEntry.Text ?? string.Empty
        }.Normalized();
    }

    private void RefreshKitchenPreview()
    {
        PreviewTextLabel.IsVisible = false;
        PreviewTextLabel.Text = null;
        PreviewTextLabel.FormattedText = null;
        KitchenPreviewLines.IsVisible = true;
        RenderKitchenPreview();
    }

    private void OnKitchenSettingChanged(object? sender, EventArgs e)
    {
        if (_isApplyingKitchenSettings)
        {
            return;
        }

        _kitchenSettings = ReadKitchenSettingsFromControls();
        RefreshKitchenPreview();
    }

    private void OnKitchenFooterTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingKitchenSettings)
        {
            UpdateFooterCounter();
            return;
        }

        _kitchenSettings = ReadKitchenSettingsFromControls();
        UpdateFooterCounter();
        RefreshKitchenPreview();
    }

    private void OnCollectionSettingChanged(object? sender, EventArgs e)
    {
        if (_isApplyingCollectionSettings)
        {
            return;
        }

        SetActiveCustomerReceiptSettings(ReadCustomerReceiptSettingsFromControls());
        RenderCustomerReceiptPreview();
    }

    private void OnCollectionFooterTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingCollectionSettings)
        {
            UpdateCollectionFooterCounter();
            return;
        }

        SetActiveCustomerReceiptSettings(ReadCustomerReceiptSettingsFromControls());
        UpdateCollectionFooterCounter();
        RenderCustomerReceiptPreview();
    }

    private async void OnSaveKitchenSettingsClicked(object? sender, EventArgs e)
    {
        _kitchenSettings = ReadKitchenSettingsFromControls();
        ApplyKitchenSettingsToControls();
        RefreshKitchenPreview();

        SaveKitchenSettingsButton.IsEnabled = false;
        SaveKitchenSettingsButton.Text = "Saving...";
        var saved = await _kitchenSettingsService.SaveSettingsAsync(_kitchenSettings);
        SaveKitchenSettingsButton.Text = "Save";
        SaveKitchenSettingsButton.IsEnabled = true;

        await AppAlertService.ShowAlertAsync(
            saved ? "Saved" : "Error",
            saved
                ? "Kitchen template settings saved."
                : "Could not save kitchen template settings.");
    }

    private async void OnResetKitchenSettingsClicked(object? sender, EventArgs e)
    {
        _kitchenSettings = KitchenTemplateSettings.Default();
        ApplyKitchenSettingsToControls();
        RefreshKitchenPreview();
        var saved = await _kitchenSettingsService.SaveSettingsAsync(_kitchenSettings);

        await AppAlertService.ShowAlertAsync(
            saved ? "Reset" : "Error",
            saved
                ? "Kitchen template settings reset to default."
                : "Could not reset kitchen template settings.");
    }

    private async void OnSaveCollectionSettingsClicked(object? sender, EventArgs e)
    {
        SetActiveCustomerReceiptSettings(ReadCustomerReceiptSettingsFromControls());
        ApplyCustomerReceiptSettingsToControls();
        RenderCustomerReceiptPreview();

        SaveCollectionSettingsButton.IsEnabled = false;
        SaveCollectionSettingsButton.Text = "Saving...";
        var isDelivery = _receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = _receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = _receiptKind == CustomerReceiptKind.TablePayment;
        var saved = _receiptKind switch
        {
            CustomerReceiptKind.Delivery => await _deliverySettingsService.SaveSettingsAsync(_deliverySettings),
            CustomerReceiptKind.TableBill => await _tableBillSettingsService.SaveSettingsAsync(_tableBillSettings),
            CustomerReceiptKind.TablePayment => await _tablePaymentSettingsService.SaveSettingsAsync(_tablePaymentSettings),
            _ => await _collectionSettingsService.SaveSettingsAsync(_collectionSettings)
        };
        SaveCollectionSettingsButton.Text = "Save";
        SaveCollectionSettingsButton.IsEnabled = true;
        var templateName = isDelivery ? "Delivery" : isTableBill ? "Table bill" : isTablePayment ? "Table payment" : "Collection";

        await AppAlertService.ShowAlertAsync(
            saved ? "Saved" : "Error",
            saved
                ? $"{templateName} receipt settings saved."
                : $"Could not save {templateName.ToLowerInvariant()} receipt settings.");
    }

    private async void OnResetCollectionSettingsClicked(object? sender, EventArgs e)
    {
        var isDelivery = _receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = _receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = _receiptKind == CustomerReceiptKind.TablePayment;
        SetActiveCustomerReceiptSettings(CollectionReceiptTemplateSettings.Default());
        ApplyCustomerReceiptSettingsToControls();
        RenderCustomerReceiptPreview();
        var saved = _receiptKind switch
        {
            CustomerReceiptKind.Delivery => await _deliverySettingsService.SaveSettingsAsync(_deliverySettings),
            CustomerReceiptKind.TableBill => await _tableBillSettingsService.SaveSettingsAsync(_tableBillSettings),
            CustomerReceiptKind.TablePayment => await _tablePaymentSettingsService.SaveSettingsAsync(_tablePaymentSettings),
            _ => await _collectionSettingsService.SaveSettingsAsync(_collectionSettings)
        };
        var templateName = isDelivery ? "Delivery" : isTableBill ? "Table bill" : isTablePayment ? "Table payment" : "Collection";

        await AppAlertService.ShowAlertAsync(
            saved ? "Reset" : "Error",
            saved
                ? $"{templateName} receipt settings reset to default."
                : $"Could not reset {templateName.ToLowerInvariant()} receipt settings.");
    }

    private void UpdateFooterCounter()
    {
        var count = FooterTextEntry.Text?.Length ?? 0;
        FooterCounterLabel.Text = $"{count} / {KitchenTemplateSettings.FooterMaxLength}";
        FooterCounterLabel.TextColor = Color.FromArgb(count >= KitchenTemplateSettings.FooterMaxLength ? "#DC2626" : "#64748B");
        FooterWarningLabel.Text = count > 45
            ? "Long footer may wrap into 2 centered lines."
            : "Keep footer short: max 2 centered receipt lines.";
        FooterWarningLabel.TextColor = Color.FromArgb(count > 45 ? "#B45309" : "#64748B");
    }

    private void UpdateCollectionFooterCounter()
    {
        var count = CollectionFooterTextEntry.Text?.Length ?? 0;
        CollectionFooterCounterLabel.Text = $"{count} / {CollectionReceiptTemplateSettings.FooterMaxLength}";
        CollectionFooterCounterLabel.TextColor = Color.FromArgb(count >= CollectionReceiptTemplateSettings.FooterMaxLength ? "#DC2626" : "#64748B");
        CollectionFooterWarningLabel.Text = count > 45
            ? "Long footer may wrap into 2 centered lines."
            : "Keep footer short: max 2 centered receipt lines.";
        CollectionFooterWarningLabel.TextColor = Color.FromArgb(count > 45 ? "#B45309" : "#64748B");
    }

    private void RenderKitchenPreview()
    {
        KitchenPreviewLines.Children.Clear();

        if (_previewMode == KitchenTicketPreviewMode.Table)
        {
            AddKitchenHeader("TABLE 12", "STARTER");
            AddNormalLine("2x Chicken Pakora");
            AddNormalLine("   No salad");
            AddNormalLine("   Extra sauce");
            AddBlankLine();
            AddNormalLine("1x Soup");
            AddNormalLine("   Hot");
            AddKitchenFooter(includeCutMarker: true);
            AddBlankLine();
            AddKitchenHeader("TABLE 12", "MAIN");
            AddNormalLine("1x Lamb Curry");
            AddNormalLine("   Medium hot");
            AddBlankLine();
            AddNormalLine("2x Pilau Rice");
            AddKitchenFooter(includeCutMarker: true);
            return;
        }

        AddKitchenHeader(_previewMode == KitchenTicketPreviewMode.Delivery ? "DELIVERY" : "COLLECTION");
        AddSectionLine("STARTER");
        AddNormalLine("2x Chicken Pakora");
        AddNormalLine("   No salad");
        AddBlankLine();
        AddSectionLine("MAIN");
        AddNormalLine("1x Lamb Curry");
        AddNormalLine("   Medium hot");
        AddBlankLine();
        AddSectionLine("TANDOORI");
        AddNormalLine("1x Chicken Tikka");
        AddNormalLine("   Well done");
        AddKitchenFooter(includeCutMarker: false);
    }

    private void RenderCustomerReceiptPreview()
    {
        var settings = ActiveCustomerReceiptSettings;
        var isDelivery = _receiptKind == CustomerReceiptKind.Delivery;
        var isTableBill = _receiptKind == CustomerReceiptKind.TableBill;
        var isTablePayment = _receiptKind == CustomerReceiptKind.TablePayment;
        var isTableReceipt = isTableBill || isTablePayment;

        KitchenPreviewLines.Children.Clear();

        AddCollectionCenteredLine("[LOGO]", PreviewFontSize, bold: false);
        AddBlankLine();
        AddCollectionCenteredLine(
            "RESTAURANT NAME",
            GetPreviewFontSize(settings.BusinessNameSize, PreviewFontSize),
            settings.BusinessNameBold);
        AddCollectionCenteredLine(
            "123 High Street",
            GetPreviewFontSize(settings.AddressSize, PreviewFontSize),
            settings.AddressBold);
        AddCollectionCenteredLine(
            "London AB1 2CD",
            GetPreviewFontSize(settings.AddressSize, PreviewFontSize),
            settings.AddressBold);
        AddCollectionCenteredLine(
            "Tel: 01234 567890",
            GetPreviewFontSize(settings.PhoneSize, PreviewFontSize),
            settings.PhoneBold);
        AddCollectionCenteredLine(
            "VAT No: GB123456789",
            GetPreviewFontSize(settings.VatSize, PreviewFontSize),
            settings.VatBold);
        AddBlankLine();
        AddNormalLine(new string('=', PreviewLineWidth));
        AddCollectionCenteredLine(
            _receiptKind switch
            {
                CustomerReceiptKind.Delivery => "DELIVERY",
                CustomerReceiptKind.TableBill => "TABLE BILL",
                CustomerReceiptKind.TablePayment => "PAYMENT RECEIPT",
                _ => "COLLECTION"
            },
            GetPreviewFontSize(settings.HeadingSize, PreviewFontSize),
            settings.HeadingBold);
        if (isTablePayment)
        {
            AddPreviewLine(
                "PAID",
                GetPreviewFontSize(settings.PaidStatusSize, PreviewFontSize),
                settings.PaidStatusBold,
                TextAlignment.Center);
        }

        AddNormalLine(new string('=', PreviewLineWidth));

        if (settings.OrderInfoSize == KitchenHeadingSize.Normal)
        {
            AddPreviewLine("Order #: 1042", PreviewFontSize, settings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Date: 06 Jul 2026                    Time: 14:25", PreviewFontSize, settings.OrderInfoBold, TextAlignment.Start);
        }
        else
        {
            var orderInfoSize = GetPreviewFontSize(settings.OrderInfoSize, PreviewFontSize);
            AddPreviewLine("Order #: 1042", orderInfoSize, settings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Date: 06 Jul 2026", orderInfoSize, settings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Time: 14:25", orderInfoSize, settings.OrderInfoBold, TextAlignment.Start);
        }

        AddBlankLine();
        if (isTableReceipt)
        {
            AddPreviewLine(
                "Table: 12",
                GetPreviewFontSize(settings.TableNumberSize, PreviewFontSize),
                settings.TableNumberBold,
                TextAlignment.Start);
            AddNormalLine("Covers: 4");
        }
        else
        {
            AddPreviewLine(
                "Customer: Sara Young",
                GetPreviewFontSize(settings.CustomerNameSize, PreviewFontSize),
                settings.CustomerNameBold,
                TextAlignment.Start);
            AddPreviewLine(
                "Phone: 07123 456789",
                GetPreviewFontSize(settings.CustomerPhoneSize, PreviewFontSize),
                settings.CustomerPhoneBold,
                TextAlignment.Start);
        }

        if (isDelivery)
        {
            AddBlankLine();
            var addressSize = GetPreviewFontSize(settings.DeliveryAddressSize, PreviewFontSize);
            AddPreviewLine("Delivery Address:", addressSize, settings.DeliveryAddressBold, TextAlignment.Start);
            AddPreviewLine("10 Spring Street", addressSize, settings.DeliveryAddressBold, TextAlignment.Start);
            AddPreviewLine("Flat 5B", addressSize, settings.DeliveryAddressBold, TextAlignment.Start);
            AddPreviewLine("London", addressSize, settings.DeliveryAddressBold, TextAlignment.Start);
            AddPreviewLine("AB1 2CD", addressSize, settings.DeliveryAddressBold, TextAlignment.Start);
        }

        AddNormalLine(new string('-', PreviewLineWidth));

        AddNormalLine("2x Chicken Pakora                         £8.50");
        AddNormalLine("   No salad");
        AddNormalLine("   Extra sauce");
        AddBlankLine();
        AddNormalLine("1x Lamb Curry                            £11.95");
        AddNormalLine("   Medium hot");
        AddBlankLine();
        AddNormalLine("2x Pilau Rice                             £7.00");
        AddNormalLine(new string('-', PreviewLineWidth));
        AddNormalLine("Subtotal:                                £27.45");
        AddNormalLine("Discount:                                -£2.00");

        if (isDelivery)
        {
            AddNormalLine("Delivery fee:                             £2.50");
        }
        else if (isTablePayment)
        {
            AddPreviewLine(
                "Service charge (10%):                     £2.75",
                GetPreviewFontSize(settings.ServiceChargeSize, PreviewFontSize),
                settings.ServiceChargeBold,
                TextAlignment.Start);
        }

        AddNormalLine(new string('=', PreviewLineWidth));
        AddNormalLine(isDelivery
            ? "TOTAL:                                   £27.95"
            : isTablePayment
                ? "TOTAL:                                   £28.20"
                : "TOTAL:                                   £25.45");
        AddBlankLine();

        if (isTableBill)
        {
            AddPreviewLine(
                "Service charge not included",
                GetPreviewFontSize(settings.ServiceChargeSize, PreviewFontSize),
                settings.ServiceChargeBold,
                TextAlignment.Start);
        }
        else if (isTablePayment)
        {
            var paymentSize = GetPreviewFontSize(settings.PaymentSize, PreviewFontSize);
            AddPreviewLine("Payment", paymentSize, settings.PaymentBold, TextAlignment.Start);
            AddPreviewLine("Cash:                                    £10.00", paymentSize, settings.PaymentBold, TextAlignment.Start);
            AddPreviewLine("Card:                                    £18.20", paymentSize, settings.PaymentBold, TextAlignment.Start);
            AddNormalLine(new string('-', PreviewLineWidth));
            AddPreviewLine("Paid Total:                              £28.20", paymentSize, settings.PaymentBold, TextAlignment.Start);
            AddPreviewLine(
                "Status: PAID",
                GetPreviewFontSize(settings.PaidStatusSize, PreviewFontSize),
                settings.PaidStatusBold,
                TextAlignment.Start);
        }
        else
        {
            AddPreviewLine(
                isDelivery ? "Payment: Card" : "Payment: Cash",
                GetPreviewFontSize(settings.PaymentSize, PreviewFontSize),
                settings.PaymentBold,
                TextAlignment.Start);
        }

        if (isDelivery)
        {
            AddNormalLine(new string('-', PreviewLineWidth));
            AddNormalLine("Customer Note:");
            AddNormalLine("Please call when outside.");
        }

        AddNormalLine(new string('-', PreviewLineWidth));

        foreach (var footerLine in WrapPreviewText(settings.FooterText, 30).Take(2))
        {
            AddCollectionCenteredLine(footerLine, PreviewFontSize, bold: false);
        }

        AddNormalLine(new string('=', PreviewLineWidth));
    }

    private void AddCollectionCenteredLine(string text, double fontSize, bool bold)
    {
        AddPreviewLine(text, fontSize, bold, TextAlignment.Center);
    }

    private void AddKitchenHeader(string title, string? section = null)
    {
        AddNormalLine(new string('=', PreviewLineWidth));
        AddPreviewLine(
            title,
            GetPreviewFontSize(_kitchenSettings.HeadingSize, PreviewFontSize),
            _kitchenSettings.HeadingBold,
            TextAlignment.Center);
        AddNormalLine(new string('=', PreviewLineWidth));

        if (_kitchenSettings.OrderInfoSize == KitchenHeadingSize.Normal)
        {
            AddPreviewLine("Order #: 1042", PreviewFontSize, _kitchenSettings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Date: 06 Jul 2026                    Time: 14:25", PreviewFontSize, _kitchenSettings.OrderInfoBold, TextAlignment.Start);
        }
        else
        {
            var orderFontSize = GetPreviewFontSize(_kitchenSettings.OrderInfoSize, PreviewFontSize);
            AddPreviewLine("Order #: 1042", orderFontSize, _kitchenSettings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Date: 06 Jul 2026", orderFontSize, _kitchenSettings.OrderInfoBold, TextAlignment.Start);
            AddPreviewLine("Time: 14:25", orderFontSize, _kitchenSettings.OrderInfoBold, TextAlignment.Start);
        }

        if (!string.IsNullOrWhiteSpace(section))
        {
            AddSectionLine($"Section: {section}");
        }

        AddNormalLine(new string('-', PreviewLineWidth));
        AddBlankLine();
    }

    private void AddKitchenFooter(bool includeCutMarker)
    {
        AddNormalLine(new string('-', PreviewLineWidth));
        if (_kitchenSettings.ShowCheckedByLine)
        {
            AddNormalLine("Checked by: ______________________________");
            AddBlankLine();
            AddBlankLine();
        }

        foreach (var footerLine in WrapPreviewText(_kitchenSettings.FooterText, 30).Take(2))
        {
            AddPreviewLine(footerLine, PreviewFontSize, bold: false, TextAlignment.Center);
        }

        AddNormalLine(new string('=', PreviewLineWidth));

        if (includeCutMarker)
        {
            AddNormalLine("                  -- CUT --");
        }
    }

    private void AddSectionLine(string text)
    {
        AddPreviewLine(
            _kitchenSettings.SectionHeadingBold ? text.ToUpperInvariant() : text,
            GetPreviewFontSize(_kitchenSettings.SectionHeadingSize, PreviewFontSize),
            _kitchenSettings.SectionHeadingBold,
            TextAlignment.Start);
    }

    private void AddNormalLine(string text)
    {
        AddPreviewLine(text, PreviewFontSize, bold: false, TextAlignment.Start);
    }

    private void AddBlankLine()
    {
        AddPreviewLine(string.Empty, PreviewFontSize, bold: false, TextAlignment.Start);
    }

    private void AddPreviewLine(string text, double fontSize, bool bold, TextAlignment alignment)
    {
        KitchenPreviewLines.Children.Add(new Label
        {
            Text = text,
            FontFamily = "Consolas",
            FontSize = fontSize,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Color.FromArgb("#111827"),
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = alignment
        });
    }

    private static double GetPreviewFontSize(KitchenHeadingSize size, double normalFontSize)
    {
        return size switch
        {
            KitchenHeadingSize.ExtraLarge => PreviewHeadingExtraLargeFontSize,
            KitchenHeadingSize.Large => PreviewHeadingLargeFontSize,
            _ => normalFontSize
        };
    }

    private static int GetSizeIndex(KitchenHeadingSize size)
    {
        return size switch
        {
            KitchenHeadingSize.ExtraLarge => 2,
            KitchenHeadingSize.Large => 1,
            _ => 0
        };
    }

    private static KitchenHeadingSize GetSizeFromIndex(int selectedIndex)
    {
        return selectedIndex switch
        {
            2 => KitchenHeadingSize.ExtraLarge,
            1 => KitchenHeadingSize.Large,
            _ => KitchenHeadingSize.Normal
        };
    }

    private static IEnumerable<string> WrapPreviewText(string? text, int width)
    {
        width = Math.Max(8, width);
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;

            foreach (var word in words)
            {
                if (word.Length > width)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        yield return line;
                        line = string.Empty;
                    }

                    for (var i = 0; i < word.Length; i += width)
                    {
                        yield return word.Substring(i, Math.Min(width, word.Length - i));
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(line))
                {
                    line = word;
                }
                else if (line.Length + 1 + word.Length <= width)
                {
                    line += " " + word;
                }
                else
                {
                    yield return line;
                    line = word;
                }
            }

            if (!string.IsNullOrEmpty(line))
            {
                yield return line;
            }
        }
    }

}
