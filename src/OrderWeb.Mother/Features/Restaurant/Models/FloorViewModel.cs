using System.ComponentModel;

namespace POS_in_NET.Models
{
    /// <summary>
    /// ViewModel for Floor with selection state for UI binding
    /// </summary>
    public class FloorViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TableCount { get; set; }
        public bool IsActive { get; set; } = true;
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        
        // Source Floor object
        public Floor SourceFloor { get; set; }
        
        public FloorViewModel(Floor floor)
        {
            Id = floor.Id;
            Name = floor.Name;
            Description = floor.Description;
            TableCount = floor.TableCount;
            IsActive = floor.IsActive;
            SourceFloor = floor;
            IsSelected = false;
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}