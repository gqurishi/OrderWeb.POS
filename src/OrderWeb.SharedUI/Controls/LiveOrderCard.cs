using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.SharedUI.Controls;

/// <summary>Mother Live Order card chrome (order or table-session).</summary>
public sealed class LiveOrderCard : ContentView
{
    public const double CardWidth = 220;
    public const double CardMinHeight = 132;

    public static readonly BindableProperty PresentationProperty = BindableProperty.Create(
        nameof(Presentation),
        typeof(LiveOrderCardPresentation),
        typeof(LiveOrderCard),
        propertyChanged: (b, _, n) => ((LiveOrderCard)b).Apply((LiveOrderCardPresentation?)n));

    public LiveOrderCardPresentation? Presentation
    {
        get => (LiveOrderCardPresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    public event EventHandler<LiveOrderCardTappedEventArgs>? Tapped;

    public LiveOrderCard()
    {
        WidthRequest = CardWidth;
        MinimumHeightRequest = CardMinHeight;
        Margin = new Thickness(0, 0, 14, 14);
    }

    public LiveOrderCard(LiveOrderCardPresentation presentation) : this()
    {
        Presentation = presentation;
    }

    private void Apply(LiveOrderCardPresentation? card)
    {
        if (card is null)
        {
            Content = null;
            return;
        }

        var accent = Color.FromArgb(string.IsNullOrWhiteSpace(card.AccentColorHex) ? "#10B981" : card.AccentColorHex);

        var stack = new VerticalStackLayout { Spacing = 4 };

        if (card.Badges is { Count: > 0 })
        {
            stack.Children.Add(CreateBadgeRow(card.Badges));
        }

        stack.Children.Add(new Label
        {
            Text = card.Title,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E293B"),
            LineBreakMode = LineBreakMode.TailTruncation
        });

        if (card.Kind == LiveOrderCardKind.TableSession)
        {
            if (!string.IsNullOrWhiteSpace(card.Subtitle))
            {
                stack.Children.Add(new Label
                {
                    Text = card.Subtitle,
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (!string.IsNullOrWhiteSpace(card.OrderNumber))
            {
                stack.Children.Add(new Label
                {
                    Text = card.OrderNumber,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8"),
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }
        }
        else
        {
            stack.Children.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(card.OrderNumber) ? "Order" : card.OrderNumber,
                FontSize = 12,
                TextColor = Color.FromArgb("#94A3B8"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            if (!string.IsNullOrWhiteSpace(card.Subtitle))
            {
                stack.Children.Add(new Label
                {
                    Text = card.Subtitle,
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }

        stack.Children.Add(new Label
        {
            Text = card.TotalText,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = accent,
            Margin = new Thickness(0, card.Kind == LiveOrderCardKind.TableSession ? 4 : 6, 0, 0)
        });

        stack.Children.Add(new Label
        {
            Text = card.TimeText,
            FontSize = 12,
            TextColor = Color.FromArgb("#94A3B8")
        });

        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = accent,
            StrokeThickness = 2,
            Padding = new Thickness(16, 14),
            WidthRequest = CardWidth,
            MinimumHeightRequest = CardMinHeight,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.08f
            },
            Content = stack
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (Presentation is not null)
            {
                Tapped?.Invoke(this, new LiveOrderCardTappedEventArgs(Presentation));
            }
        };
        border.GestureRecognizers.Add(tap);

        Content = border;
    }

    private static HorizontalStackLayout CreateBadgeRow(IReadOnlyList<LiveOrderBadgePresentation> badges)
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };

        foreach (var badge in badges)
        {
            row.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb(badge.BackgroundColor),
                StrokeThickness = 0,
                Padding = new Thickness(6, 4),
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Content = new Label
                {
                    Text = badge.Text,
                    FontSize = 9,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(badge.TextColor),
                    LineBreakMode = LineBreakMode.NoWrap
                }
            });
        }

        return row;
    }
}
