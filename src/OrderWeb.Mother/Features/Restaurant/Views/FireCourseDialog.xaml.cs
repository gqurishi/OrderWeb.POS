using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class FireCourseDialog : ContentView
    {
        private TaskCompletionSource<string?>? _tcs;

        public FireCourseDialog()
        {
            InitializeComponent();
        }

        public Task<string?> ShowAsync()
        {
            _tcs = new TaskCompletionSource<string?>();
            
            // Add to page
            if (Application.Current?.MainPage is Page page)
            {
                if (page is Shell shell && shell.CurrentPage is ContentPage contentPage)
                {
                    AddToPage(contentPage);
                }
                else if (page is ContentPage cp)
                {
                    AddToPage(cp);
                }
            }
            
            return _tcs.Task;
        }

        private void AddToPage(ContentPage page)
        {
            if (page.Content is Grid grid)
            {
                // Span all rows and columns to cover full page
                if (grid.RowDefinitions.Count > 0)
                    Grid.SetRowSpan(this, grid.RowDefinitions.Count);
                if (grid.ColumnDefinitions.Count > 0)
                    Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
                
                grid.Children.Add(this);
            }
            else if (page.Content is Layout layout)
            {
                var newGrid = new Grid();
                var existingContent = page.Content;
                page.Content = null;
                newGrid.Children.Add(existingContent);
                newGrid.Children.Add(this);
                page.Content = newGrid;
            }
            
            IsVisible = true;
        }

        private void CloseDialog()
        {
            if (Parent is Grid grid)
            {
                grid.Children.Remove(this);
            }
        }

        private void CloseWithResult(string? result)
        {
            CloseDialog();
            _tcs?.TrySetResult(result);
        }

        private void OnStartersClicked(object? sender, TappedEventArgs e)
        {
            CloseWithResult("Starters");
        }

        private void OnMainsClicked(object? sender, TappedEventArgs e)
        {
            CloseWithResult("Mains");
        }

        private void OnDessertsClicked(object? sender, TappedEventArgs e)
        {
            CloseWithResult("Desserts");
        }

        private void OnDrinksClicked(object? sender, TappedEventArgs e)
        {
            CloseWithResult("Drinks");
        }

        private void OnFireAllClicked(object? sender, TappedEventArgs e)
        {
            CloseWithResult("All");
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            CloseWithResult(null);
        }
    }
}
