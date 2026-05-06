using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class QuickNoteDialog : ContentView
    {
        private TaskCompletionSource<MenuItemQuickNote?>? _taskCompletionSource;
        private MenuItemQuickNote? _editingNote;
        private Grid? _parentGrid;

        public QuickNoteDialog()
        {
            InitializeComponent();
            InputEntry.TextChanged += OnTextChanged;
        }

        public Task<MenuItemQuickNote?> ShowAsync(MenuItemQuickNote? note = null)
        {
            _editingNote = note;
            _taskCompletionSource = new TaskCompletionSource<MenuItemQuickNote?>();

            if (_editingNote != null)
            {
                // Edit mode
                TitleLabel.Text = "Edit Quick Note";
                SaveButton.Text = "Update";
                InputEntry.Text = _editingNote.NoteText;
            }
            else
            {
                // Add mode
                TitleLabel.Text = "Add Quick Note";
                SaveButton.Text = "Add Note";
                InputEntry.Text = string.Empty;
            }

            UpdateCharCount();
            this.IsVisible = true;
            InputEntry.Focus();
            return _taskCompletionSource.Task;
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateCharCount();
        }

        private void UpdateCharCount()
        {
            var count = InputEntry.Text?.Length ?? 0;
            CharCountLabel.Text = $"{count}/50";
            CharCountLabel.TextColor = count > 45 ? Color.FromArgb("#EF4444") : Color.FromArgb("#94A3B8");
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            var text = InputEntry.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(text))
            {
                // Simple validation - could show toast
                return;
            }

            MenuItemQuickNote result;
            
            if (_editingNote != null)
            {
                // Update existing
                _editingNote.NoteText = text;
                _editingNote.UpdatedAt = DateTime.Now;
                result = _editingNote;
            }
            else
            {
                // Create new
                result = new MenuItemQuickNote
                {
                    Id = Guid.NewGuid().ToString(),
                    NoteText = text,
                    Active = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
            }

            this.IsVisible = false;
            _taskCompletionSource?.SetResult(result);
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(null);
        }
    }
}
