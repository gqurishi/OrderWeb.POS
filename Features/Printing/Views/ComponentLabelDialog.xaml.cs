using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views;

public partial class ComponentLabelDialog : ContentView
{
    private TaskCompletionSource<string?> _taskCompletionSource;

    public ComponentLabelDialog()
    {
        InitializeComponent();
        _taskCompletionSource = new TaskCompletionSource<string?>();
    }

    public Task<string?> ShowAsync()
    {
        ComponentNameEntry.Focus();
        return _taskCompletionSource.Task;
    }

    private void OnOkClicked(object sender, EventArgs e)
    {
        var result = ComponentNameEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(result))
        {
            _taskCompletionSource.TrySetResult(result);
        }
        else
        {
            // Don't close if empty
            ComponentNameEntry.Focus();
        }
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult(null);
    }

    private void OnBackgroundTapped(object sender, EventArgs e)
    {
        _taskCompletionSource.TrySetResult(null);
    }

    private void OnEntryCompleted(object sender, EventArgs e)
    {
        OnOkClicked(sender, e);
    }
}
