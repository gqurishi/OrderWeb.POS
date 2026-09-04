using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyFirstMauiApp.Models
{
    /// <summary>
    /// Print group for routing items to specific printers
    /// </summary>
    public class PrintGroup : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string? _printerIp;
        private int _printerPort = 9100;
        private string _printerType = "receipt";
        private bool _isActive = true;
        private string _colorCode = "#3B82F6";
        private int _displayOrder;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string? PrinterIp
        {
            get => _printerIp;
            set { _printerIp = value; OnPropertyChanged(); }
        }

        public int PrinterPort
        {
            get => _printerPort;
            set { _printerPort = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Type of printer: "kitchen", "bar", "receipt", "label"
        /// </summary>
        public string PrinterType
        {
            get => _printerType;
            set { _printerType = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public string ColorCode
        {
            get => _colorCode;
            set { _colorCode = value; OnPropertyChanged(); }
        }

        public int DisplayOrder
        {
            get => _displayOrder;
            set { _displayOrder = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Helper properties
        public bool HasPrinter => !string.IsNullOrEmpty(PrinterIp);
        public string StatusText => IsActive ? "Active" : "Inactive";
        public string PrinterInfo => HasPrinter ? $"{PrinterIp}:{PrinterPort}" : "Not configured";
        public bool IsLabelPrinter => PrinterType == "label";
        public bool IsKitchenPrinter => PrinterType == "kitchen";
        public bool IsBarPrinter => PrinterType == "bar";
        public bool IsReceiptPrinter => PrinterType == "receipt";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
