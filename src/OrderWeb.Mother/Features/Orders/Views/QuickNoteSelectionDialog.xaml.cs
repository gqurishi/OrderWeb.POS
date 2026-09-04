using Microsoft.Maui.Controls.Shapes;
using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace POS_in_NET.Views
{
    public enum QuickNoteSelectionKind
    {
        Cancelled,
        NoNote,
        SavedNote,
        CustomNote
    }

    public sealed class QuickNoteSelectionResult
    {
        public QuickNoteSelectionKind Kind { get; init; }
        public string? NoteText { get; init; }
    }

    public partial class QuickNoteSelectionDialog : ContentView
    {
        private readonly List<MenuItemQuickNote> _notes = new();
        private TaskCompletionSource<QuickNoteSelectionResult>? _taskCompletionSource;
        private Grid? _parentGrid;

        public QuickNoteSelectionDialog()
        {
            InitializeComponent();
        }

        public async Task<QuickNoteSelectionResult> ShowAsync(string itemName, IEnumerable<MenuItemQuickNote> notes)
        {
            using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();

            _taskCompletionSource = new TaskCompletionSource<QuickNoteSelectionResult>();
            _notes.Clear();
            _notes.AddRange(notes.Where(note => note.Active && !string.IsNullOrWhiteSpace(note.NoteText)));

            ItemNameLabel.Text = itemName;
            BuildRows();

            if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
            {
                return new QuickNoteSelectionResult { Kind = QuickNoteSelectionKind.NoNote };
            }

            return await _taskCompletionSource.Task;
        }

        private void BuildRows()
        {
            NotesContainer.Children.Clear();

            foreach (var note in _notes)
            {
                NotesContainer.Children.Add(BuildRow(
                    note.NoteText,
                    "#EEF2FF",
                    "#A5B4FC",
                    "#6366F1",
                    () => SelectSavedNote(note.NoteText)));
            }

            NotesContainer.Children.Add(BuildRow(
                "Custom note...",
                "#ECFDF5",
                "#A7F3D0",
                "#059669",
                SelectCustomNote));
        }

        private Border BuildRow(string text, string markerBackground, string markerStroke, string markerTextColor, Action selectedAction)
        {
            var row = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                Padding = new Thickness(14, 12),
                StrokeShape = new RoundRectangle { CornerRadius = 12 }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 12
            };

            var marker = new Border
            {
                WidthRequest = 30,
                HeightRequest = 30,
                BackgroundColor = Color.FromArgb(markerBackground),
                Stroke = Color.FromArgb(markerStroke),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 15 },
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = "+",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(markerTextColor),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };

            var noteLabel = new Label
            {
                Text = text,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#0F172A"),
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Center
            };

            grid.Add(marker, 0, 0);
            grid.Add(noteLabel, 1, 0);
            row.Content = grid;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (_, _) => selectedAction();
            row.GestureRecognizers.Add(tapGesture);

            return row;
        }

        private void SelectSavedNote(string noteText)
        {
            _taskCompletionSource?.TrySetResult(new QuickNoteSelectionResult
            {
                Kind = QuickNoteSelectionKind.SavedNote,
                NoteText = noteText
            });
            CloseDialog();
        }

        private void SelectCustomNote()
        {
            _taskCompletionSource?.TrySetResult(new QuickNoteSelectionResult
            {
                Kind = QuickNoteSelectionKind.CustomNote
            });
            CloseDialog();
        }

        private void OnNoNoteClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new QuickNoteSelectionResult
            {
                Kind = QuickNoteSelectionKind.NoNote
            });
            CloseDialog();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _taskCompletionSource?.TrySetResult(new QuickNoteSelectionResult
            {
                Kind = QuickNoteSelectionKind.Cancelled
            });
            CloseDialog();
        }

        private void CloseDialog()
        {
            DialogOverlayHelper.DetachOverlay(this, _parentGrid);
            _parentGrid = null;
        }
    }
}
