using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Views.Customers;

public partial class CustomerSearchView : ContentView
{
    public CustomerSearchView()
    {
        InitializeComponent();
    }

    public event EventHandler<string>? SearchRequested;
    public event EventHandler<CachedCustomer>? CustomerSelected;

    public string SearchText => SearchEntry.Text?.Trim() ?? string.Empty;

    public void SetResults(IReadOnlyList<CachedCustomer> customers)
    {
        ResultsStack.Children.Clear();

        if (customers.Count == 0)
        {
            ResultsStack.Children.Add(new Label
            {
                Text = "No existing customer found. Continue with the form below.",
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                TextColor = Color.FromArgb("#64748B"),
                Padding = new Thickness(4, 8)
            });
            return;
        }

        foreach (var customer in customers)
        {
            ResultsStack.Children.Add(BuildResultCard(customer));
        }
    }

    private View BuildResultCard(CachedCustomer customer)
    {
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 14,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = customer.Name, FontFamily = "OpenSansSemibold", FontSize = 16, TextColor = Color.FromArgb("#1F2937") },
                    new Label { Text = customer.Phone, FontFamily = "OpenSansRegular", FontSize = 13, TextColor = Color.FromArgb("#64748B") },
                    new Label { Text = customer.Address, FontFamily = "OpenSansRegular", FontSize = 13, TextColor = Color.FromArgb("#64748B") }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => CustomerSelected?.Invoke(this, customer);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void OnSearchClicked(object sender, EventArgs e)
    {
        SearchRequested?.Invoke(this, SearchText);
    }
}
