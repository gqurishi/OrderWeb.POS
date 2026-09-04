using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace POS_in_NET.Models
{
    public class SelectableCategory : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#3B82F6";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
