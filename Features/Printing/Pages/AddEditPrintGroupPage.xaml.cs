using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models;
using MyFirstMauiApp.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Pages;

public partial class AddEditPrintGroupPage : ContentPage
{
    private readonly PrintGroupService _printGroupService;
    private readonly PrintGroup _printGroup;
    private readonly bool _isNew;

    public AddEditPrintGroupPage(PrintGroup printGroup, bool isNew)
    {
        InitializeComponent();
        _printGroupService = new PrintGroupService();
        _printGroup = printGroup;
        _isNew = isNew;

        Title = isNew ? "Add Print Group" : "Edit Print Group";
        LoadPrintGroup();

        ColorCodeEntry.TextChanged += OnColorCodeChanged;
    }

    private void LoadPrintGroup()
    {
        NameEntry.Text = _printGroup.Name;
        PrinterTypePicker.SelectedItem = _printGroup.PrinterType ?? "Receipt";
        PrinterIpEntry.Text = _printGroup.PrinterIp;
        PrinterPortEntry.Text = _printGroup.PrinterPort.ToString();
        ColorCodeEntry.Text = _printGroup.ColorCode ?? "#9CA3AF";
        DisplayOrderEntry.Text = _printGroup.DisplayOrder.ToString();
        IsActiveSwitch.IsToggled = _printGroup.IsActive;

        UpdateColorPreview();
    }

    private void OnColorCodeChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        try
        {
            var colorCode = ColorCodeEntry.Text?.Trim();
            if (!string.IsNullOrEmpty(colorCode) && colorCode.StartsWith("#"))
            {
                ColorPreview.Color = Color.FromArgb(colorCode);
            }
            else
            {
                ColorPreview.Color = Color.FromArgb("#9CA3AF");
            }
        }
        catch
        {
            ColorPreview.Color = Color.FromArgb("#9CA3AF");
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (!ValidateInputs())
            return;

        try
        {
            _printGroup.Name = NameEntry.Text.Trim();
            _printGroup.PrinterType = PrinterTypePicker.SelectedItem?.ToString();
            _printGroup.PrinterIp = string.IsNullOrWhiteSpace(PrinterIpEntry.Text) ? null : PrinterIpEntry.Text.Trim();
            
            if (int.TryParse(PrinterPortEntry.Text, out int port))
                _printGroup.PrinterPort = port;
            else
                _printGroup.PrinterPort = 9100;

            _printGroup.ColorCode = string.IsNullOrWhiteSpace(ColorCodeEntry.Text) ? "#9CA3AF" : ColorCodeEntry.Text.Trim();
            
            if (int.TryParse(DisplayOrderEntry.Text, out int order))
                _printGroup.DisplayOrder = order;
            else
                _printGroup.DisplayOrder = 0;

            _printGroup.IsActive = IsActiveSwitch.IsToggled;

            if (_isNew)
            {
                await _printGroupService.CreatePrintGroupAsync(_printGroup);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Print group created successfully");
            }
            else
            {
                await _printGroupService.UpdatePrintGroupAsync(_printGroup);
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Print group updated successfully");
            }

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save print group: {ex.Message}");
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please enter a name for the print group");
            return false;
        }

        if (PrinterTypePicker.SelectedItem == null)
        {
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Please select a printer type");
            return false;
        }

        var colorCode = ColorCodeEntry.Text?.Trim();
        if (!string.IsNullOrEmpty(colorCode) && !colorCode.StartsWith("#"))
        {
            _ = POS_in_NET.Services.AppAlertService.ShowAlertAsync("Validation Error", "Color code must start with #");
            return false;
        }

        return true;
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
