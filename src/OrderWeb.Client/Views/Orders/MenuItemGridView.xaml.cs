using OrderWeb.Client.Models;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.Client.Views.Orders;

public partial class MenuItemGridView : ContentView
{
    public MenuItemGridView()
    {
        InitializeComponent();
    }

    public event EventHandler<CachedProduct>? ItemTapped;

    public void SetItems(IEnumerable<CachedProduct> items)
    {
        ItemsLayout.Children.Clear();
        foreach (var item in items)
        {
            ItemsLayout.Children.Add(BuildItemCard(item));
        }
    }

    private View BuildItemCard(CachedProduct item)
    {
        var card = new Border
        {
            WidthRequest = 178,
            HeightRequest = 128,
                Margin = new Thickness(0, 0, 26, 26),
            Padding = new Thickness(16, 14),
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.White,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Label
                    {
                        Text = item.Name,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 16,
                        TextColor = Color.FromArgb("#1F2937"),
                        LineBreakMode = LineBreakMode.WordWrap
                    },
                    new Label
                    {
                        Text = $"£{item.Price:F2}",
                        FontFamily = "OpenSansBold",
                        FontSize = 18,
                            TextColor = Color.FromArgb("#10B981")
                    }
                }
            }
        };
        SetRow(((Grid)card.Content).Children[1], 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ItemTapped?.Invoke(this, item);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static void SetRow(IView view, int row)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Grid.RowProperty, row);
        }
    }
}
