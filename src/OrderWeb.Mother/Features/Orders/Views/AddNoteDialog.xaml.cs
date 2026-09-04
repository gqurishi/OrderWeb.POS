using Microsoft.Maui.Controls;
using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using POS_in_NET.Services;
using System;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public partial class AddNoteDialog : ContentView
    {
        private readonly CommentNoteService _noteService;
        private PredefinedNote? _editingNote;
        private TaskCompletionSource<bool>? _taskCompletionSource;

        public AddNoteDialog()
        {
            InitializeComponent();
            _noteService = new CommentNoteService();
        }

        public Task<bool> ShowAsync(PredefinedNote? note = null)
        {
            _editingNote = note;
            _taskCompletionSource = new TaskCompletionSource<bool>();

            if (_editingNote != null)
            {
                // Edit mode
                DialogTitle.Text = "Edit Note";
                SaveButton.Text = "Update";
                NoteTextEditor.Text = _editingNote.NoteText;
                DisplayOrderEntry.Text = _editingNote.DisplayOrder.ToString();
                ActiveSwitch.IsToggled = _editingNote.Active;

                // Set category
                if (!string.IsNullOrEmpty(_editingNote.Category))
                {
                    var categories = new[] { "Cooking", "Allergy", "Special Request", "Dietary", "Temperature", "Portion", "Other" };
                    var categoryIndex = Array.IndexOf(categories, _editingNote.Category);
                    if (categoryIndex >= 0)
                    {
                        CategoryPicker.SelectedIndex = categoryIndex;
                    }
                }

                // Set priority
                var priorities = new[] { "low", "normal", "high", "urgent" };
                var priorityIndex = Array.IndexOf(priorities, _editingNote.Priority.ToLower());
                if (priorityIndex >= 0)
                {
                    PriorityPicker.SelectedIndex = priorityIndex;
                }
            }
            else
            {
                // Add mode
                DialogTitle.Text = "Add New Note";
                SaveButton.Text = "Save";
                NoteTextEditor.Text = string.Empty;
                DisplayOrderEntry.Text = "0";
                ActiveSwitch.IsToggled = true;
                CategoryPicker.SelectedIndex = -1;
                PriorityPicker.SelectedIndex = 1; // normal
            }

            this.IsVisible = true;
            return _taskCompletionSource.Task;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NoteTextEditor.Text))
                {
                    NotificationService.Instance.ShowWarning("Note text is required", "Validation Error");
                    return;
                }

                if (PriorityPicker.SelectedIndex < 0)
                {
                    NotificationService.Instance.ShowWarning("Please select a priority", "Validation Error");
                    return;
                }

                if (!int.TryParse(DisplayOrderEntry.Text, out int displayOrder))
                {
                    displayOrder = 0;
                }

                var priority = PriorityPicker.SelectedItem.ToString() ?? "normal";
                var category = CategoryPicker.SelectedIndex >= 0 ? CategoryPicker.SelectedItem?.ToString() : null;

                // Get color based on priority
                var color = priority.ToLower() switch
                {
                    "urgent" => "#EF4444",
                    "high" => "#F59E0B",
                    "normal" => "#3B82F6",
                    "low" => "#6B7280",
                    _ => "#F59E0B"
                };

                if (_editingNote != null)
                {
                    // Update existing note
                    _editingNote.NoteText = NoteTextEditor.Text.Trim();
                    _editingNote.Category = category;
                    _editingNote.Priority = priority;
                    _editingNote.DisplayOrder = displayOrder;
                    _editingNote.Active = ActiveSwitch.IsToggled;
                    _editingNote.Color = color;
                    _editingNote.UpdatedAt = DateTime.Now;

                    await _noteService.UpdateNoteAsync(_editingNote);
                    NotificationService.Instance.ShowSuccess("Note updated successfully");
                }
                else
                {
                    // Create new note
                    var newNote = new PredefinedNote
                    {
                        Id = Guid.NewGuid().ToString(),
                        NoteText = NoteTextEditor.Text.Trim(),
                        Category = category,
                        Priority = priority,
                        DisplayOrder = displayOrder,
                        Active = ActiveSwitch.IsToggled,
                        Color = color,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _noteService.CreateNoteAsync(newNote);
                    NotificationService.Instance.ShowSuccess("Note added successfully");
                }

                this.IsVisible = false;
                _taskCompletionSource?.SetResult(true);
            }
            catch (Exception ex)
            {
                NotificationService.Instance.ShowError($"Failed to save note: {ex.Message}");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            this.IsVisible = false;
            _taskCompletionSource?.SetResult(false);
        }
    }
}
