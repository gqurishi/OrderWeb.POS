using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class MoreOptionsDialog : ContentView
    {
        private TaskCompletionSource<string?>? _taskCompletionSource;
        private Grid? _parentGrid;

        public MoreOptionsDialog()
        {
            InitializeComponent();
        }

        public void SetOptions(List<(string Text, string Icon, bool IsEnabled)> options)
        {
            OptionsGrid.Children.Clear();
            OptionsGrid.RowDefinitions.Clear();

            int columns = 3;
            int rows = (int)Math.Ceiling(options.Count / (double)columns);
            
            // Add row definitions
            for (int i = 0; i < rows; i++)
            {
                OptionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                int row = i / columns;
                int col = i % columns;

                var button = new Button
                {
                    Text = option.Text, // No emoji, just text
                    BackgroundColor = option.IsEnabled ? Color.FromArgb("#F9FAFB") : Color.FromArgb("#F3F4F6"),
                    TextColor = option.IsEnabled ? Color.FromArgb("#1F2937") : Color.FromArgb("#9CA3AF"),
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 12,
                    HeightRequest = 70,
                    BorderColor = Color.FromArgb("#E5E7EB"),
                    BorderWidth = 1.5,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    IsEnabled = option.IsEnabled
                };
                
                if (option.IsEnabled)
                {
                    button.Clicked += (s, e) =>
                    {
                        _taskCompletionSource?.TrySetResult(option.Text);
                        CloseDialog();
                    };
                }

                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                OptionsGrid.Children.Add(button);
            }
        }

        public async Task<string?> ShowAsync()
        {
            _taskCompletionSource = new TaskCompletionSource<string?>();
            
            if (Application.Current?.MainPage != null)
            {
                var pageContent = GetPageContent(Application.Current.MainPage);
                if (pageContent is Grid mainGrid)
                {
                    _parentGrid = mainGrid;
                    
                    Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                    Grid.SetRow(this, 0);
                    Grid.SetColumn(this, 0);
                    
                    mainGrid.Children.Add(this);
                }
            }

            return await _taskCompletionSource.Task;
        }

        private View? GetPageContent(Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage currentPage)
            {
                return currentPage.Content;
            }
            else if (page is ContentPage contentPage)
            {
                return contentPage.Content;
            }
            return null;
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(null);
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (_parentGrid != null)
            {
                _parentGrid.Children.Remove(this);
            }
        }
    }
}
