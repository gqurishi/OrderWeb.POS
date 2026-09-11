namespace OrderWeb.SharedUI.Payments;

/// <summary>Attaches a full-screen overlay dialog to the current ContentPage root Grid.</summary>
internal static class PaymentOverlayHost
{
    public static Grid? Attach(ContentView dialog, ContentPage? hostPage = null)
    {
        var page = hostPage
            ?? Shell.Current?.CurrentPage as ContentPage
            ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;

        if (page is null)
        {
            return null;
        }

        var root = page.Content as Grid;
        if (root is null)
        {
            var wrap = new Grid();
            var existing = page.Content;
            page.Content = null;
            if (existing is not null)
            {
                wrap.Children.Add(existing);
            }

            page.Content = wrap;
            root = wrap;
        }

        Grid.SetRow(dialog, 0);
        Grid.SetColumn(dialog, 0);
        if (root.RowDefinitions.Count > 0)
        {
            Grid.SetRowSpan(dialog, Math.Max(1, root.RowDefinitions.Count));
        }

        if (root.ColumnDefinitions.Count > 0)
        {
            Grid.SetColumnSpan(dialog, Math.Max(1, root.ColumnDefinitions.Count));
        }

        dialog.ZIndex = 15000;
        dialog.InputTransparent = false;
        dialog.IsVisible = true;
        root.Children.Add(dialog);
        return root;
    }

    public static void Detach(ContentView dialog, Grid? parent)
    {
        parent?.Children.Remove(dialog);
        if (dialog.Parent is Grid g)
        {
            g.Children.Remove(dialog);
        }
    }

    public static ContentPage? FindPage() =>
        Shell.Current?.CurrentPage as ContentPage
        ?? Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;
}
