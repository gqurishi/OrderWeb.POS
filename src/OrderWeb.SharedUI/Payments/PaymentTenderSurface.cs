using OrderWeb.SharedUI.ViewModels;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// Factory helpers for the canonical SharedUI tender screen after PaymentWizard setup.
/// </summary>
public static class PaymentTenderSurface
{
    /// <summary>
    /// Creates Mother-chrome PaymentView + ViewModel for hosts.
    /// Loyalty on by default (Client); Mother may set ShowLoyaltyMethod=false.
    /// Inline split off by default — use PaymentWizard for Table splits.
    /// </summary>
    public static (PaymentView View, PaymentViewModel ViewModel) Create(
        decimal amountDue,
        bool allowSplit,
        bool showLoyalty = true)
    {
        var viewModel = new PaymentViewModel
        {
            AmountDue = amountDue,
            AllowSplit = allowSplit,
            ShowLoyaltyMethod = showLoyalty,
            ShowInlineSplitToggle = false,
            ShowInlineSplitMethod = false,
            SelectedMethod = "cash"
        };
        viewModel.SetTenderedExact();

        var view = new PaymentView { ViewModel = viewModel };
        return (view, viewModel);
    }
}
