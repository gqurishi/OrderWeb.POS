using OrderWeb.Contracts.Printing;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Shared Mother/Client print result presentation. Status text and tone come only
/// from Mother's <see cref="PrintResultDto"/> — never invent success locally.
/// </summary>
public sealed class PrintResultView : ContentView
{
    private readonly StatusBadge _badge = new();
    private readonly Label _title = new() { FontSize = 16, FontAttributes = FontAttributes.Bold };
    private readonly Label _message = new() { FontSize = 13 };
    private readonly Label _meta = new() { FontSize = 12 };
    private readonly Label _audit = new() { FontSize = 12 };
    private readonly VerticalStackLayout _failures = new() { Spacing = 4 };

    public PrintResultView()
    {
        _title.Use(Label.TextColorProperty, "PosTextPrimary");
        _message.Use(Label.TextColorProperty, "PosTextSecondary");
        _meta.Use(Label.TextColorProperty, "PosTextMuted");
        _audit.Use(Label.TextColorProperty, "PosTextMuted");

        var card = new Border
        {
            Padding = new Thickness(14),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { _badge, _title }
                    },
                    _message,
                    _meta,
                    _failures,
                    _audit
                }
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");
        card.Use(Border.StrokeProperty, "PosBorder");
        Content = card;
        ShowIdle();
    }

    public void ShowIdle(string message = "No print request yet.")
    {
        _badge.Text = "Idle";
        _badge.Kind = StatusKind.Neutral;
        _title.Text = "Print status";
        _message.Text = message;
        _meta.Text = string.Empty;
        _audit.Text = string.Empty;
        _failures.Children.Clear();
    }

    public void Bind(PrintResultDto? result)
    {
        if (result is null)
        {
            ShowIdle();
            return;
        }

        _badge.Text = result.StatusLabel;
        _badge.Kind = result.Status switch
        {
            PrintStatus.Printed => StatusKind.Success,
            PrintStatus.Queued or PrintStatus.Printing => StatusKind.Info,
            PrintStatus.PartialFailure => StatusKind.Warning,
            PrintStatus.Failed or PrintStatus.PrinterUnavailable => StatusKind.Error,
            _ => StatusKind.Neutral
        };

        _title.Text = result.Kind switch
        {
            PrintKind.KitchenTicket => "Kitchen ticket",
            PrintKind.CustomerReceipt => "Customer receipt",
            PrintKind.Reprint => "Reprint",
            PrintKind.CashDrawer => "Cash drawer",
            _ => "Print"
        };

        _message.Text = result.Message;
        _meta.Text = string.IsNullOrWhiteSpace(result.OrderId)
            ? $"Updated {result.UpdatedAtUtc.LocalDateTime:HH:mm:ss}"
            : $"Order {result.OrderId} · {result.UpdatedAtUtc.LocalDateTime:HH:mm:ss}";
        _audit.Text = $"Audit {result.AuditId}";

        _failures.Children.Clear();
        if (result.FailedRoutes is { Count: > 0 })
        {
            foreach (var failure in result.FailedRoutes)
            {
                var row = new Label
                {
                    Text = $"{failure.Route}: {failure.Reason}",
                    FontSize = 12
                };
                row.Use(Label.TextColorProperty, "PosWarningText");
                _failures.Children.Add(row);
            }
        }
    }
}
