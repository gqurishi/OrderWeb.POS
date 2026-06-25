namespace POS_in_NET.Services;

public static class DialogOverlayHelper
{
    public static Grid? FindHostGrid(Page? page = null)
    {
        page ??= Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
        if (page is Shell shell)
        {
            page = shell.CurrentPage;
        }

        if (page is not ContentPage contentPage)
        {
            return null;
        }

        return FindGridInTree(contentPage.Content);
    }

    public static bool TryAttachOverlay(ContentView overlay, out Grid? hostGrid)
    {
        hostGrid = FindHostGrid();
        if (hostGrid == null)
        {
            return false;
        }

        Grid.SetRowSpan(overlay, hostGrid.RowDefinitions.Count > 0 ? hostGrid.RowDefinitions.Count : 1);
        Grid.SetColumnSpan(overlay, hostGrid.ColumnDefinitions.Count > 0 ? hostGrid.ColumnDefinitions.Count : 1);
        Grid.SetRow(overlay, 0);
        Grid.SetColumn(overlay, 0);
        hostGrid.Children.Add(overlay);
        return true;
    }

    public static void DetachOverlay(ContentView overlay, Grid? hostGrid)
    {
        if (hostGrid == null)
        {
            return;
        }

        if (hostGrid.Children.Contains(overlay))
        {
            hostGrid.Children.Remove(overlay);
        }
    }

    private static Grid? FindGridInTree(Element? element, int depth = 0)
    {
        if (element == null || depth > 6)
        {
            return null;
        }

        if (element is Grid grid)
        {
            return grid;
        }

        switch (element)
        {
            case ContentPage page:
                return FindGridInTree(page.Content, depth + 1);
            case ScrollView scrollView:
                return FindGridInTree(scrollView.Content, depth + 1);
            case Border border:
                return FindGridInTree(border.Content, depth + 1);
            case ContentView contentView:
                return FindGridInTree(contentView.Content, depth + 1);
            case Layout layout:
                foreach (var child in layout.Children)
                {
                    if (child is Element childElement)
                    {
                        var found = FindGridInTree(childElement, depth + 1);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }

                break;
        }

        return null;
    }
}
