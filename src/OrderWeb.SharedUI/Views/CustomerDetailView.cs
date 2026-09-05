using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Customers;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Customer detail card. Visible fields follow Mother field-access policy.</summary>
public sealed class CustomerDetailView : ContentView
{
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly VerticalStackLayout _fields = new() { Spacing = 10 };
    private readonly Label _title = new()
    {
        Text = "Customer Details",
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#111827")
    };
    private readonly SharedButton _assignButton = new()
    {
        Text = "Assign to Order",
        Variant = ButtonVariant.Success,
        HeightRequest = 52,
        IsVisible = false
    };

    private CustomerDetailDto? _current;

    public CustomerDetailView()
    {
        _assignButton.Clicked += (_, _) =>
        {
            if (_current is not null)
            {
                AssignRequested?.Invoke(this, _current.Customer);
            }
        };

        Content = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = new Thickness(20),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { _staleBanner, _title, _fields, _assignButton }
            }
        };
    }

    public event EventHandler<CustomerSummaryDto>? AssignRequested;

    public void Apply(CustomerDetailDto state)
    {
        _current = state;
        _fields.Children.Clear();
        _title.Text = state.Customer.Name ?? "Customer Details";
        foreach (var field in state.VisibleFields)
        {
            var value = field switch
            {
                CustomerFieldKind.Name => state.Customer.Name,
                CustomerFieldKind.Phone => state.Customer.Phone,
                CustomerFieldKind.Email => state.Customer.Email,
                CustomerFieldKind.Address => state.Customer.Address,
                CustomerFieldKind.City => state.Customer.City,
                CustomerFieldKind.County => state.Customer.County,
                CustomerFieldKind.Postcode => state.Customer.Postcode,
                CustomerFieldKind.LoyaltyPoints => state.Customer.LoyaltyPoints?.ToString(),
                CustomerFieldKind.Notes => state.Notes,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            _fields.Children.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = FormatCaption(field),
                        FontSize = 12,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    new Label
                    {
                        Text = value,
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F172A")
                    }
                }
            });
        }

        _assignButton.IsVisible = state.FieldPolicy.AllowAssignCustomer;
        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
    }

    private static string FormatCaption(CustomerFieldKind field) =>
        field switch
        {
            CustomerFieldKind.LoyaltyPoints => "Loyalty Points",
            _ => field.ToString()
        };
}
