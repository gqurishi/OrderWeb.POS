using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using POS_in_NET.Helpers;

namespace POS_in_NET.Views
{
    public partial class MoreOptionsDialog : ContentView
    {
        private TaskCompletionSource<string?>? _taskCompletionSource;
        private Grid? _parentGrid;

        public MoreOptionsDialog()
        {
            InitializeComponent();
            TabletLayoutHelper.AttachDialog(this, DialogCard, 650, 760);
        }

        public void SetOptions(List<(string Text, string Icon, bool IsEnabled, bool IsDestructive)> options)
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

                var isRestoreAction = string.Equals(option.Text, "RESTORE SERVICE CHARGE", StringComparison.Ordinal);
                var button = new Button
                {
                    Text = option.Text,
                    BackgroundColor = !option.IsEnabled
                        ? Color.FromArgb("#F3F4F6")
                        : option.IsDestructive
                            ? Color.FromArgb("#DC2626")
                            : isRestoreAction
                                ? Color.FromArgb("#059669")
                                : Color.FromArgb("#F9FAFB"),
                    TextColor = !option.IsEnabled
                        ? Color.FromArgb("#9CA3AF")
                        : option.IsDestructive || isRestoreAction
                            ? Colors.White
                            : Color.FromArgb("#1F2937"),
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 12,
                    HeightRequest = 70,
                    BorderColor = option.IsDestructive
                        ? Color.FromArgb("#B91C1C")
                        : isRestoreAction
                            ? Color.FromArgb("#047857")
                            : Color.FromArgb("#E5E7EB"),
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
