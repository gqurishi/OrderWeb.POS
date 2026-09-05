namespace OrderWeb.SharedUI.Controls;

public class SharedTextInput : Border
{
    private readonly Entry _entry;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(SharedTextInput), string.Empty, BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((SharedTextInput)b)._entry.Text = value?.ToString() ?? string.Empty);
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(SharedTextInput), string.Empty,
        propertyChanged: (b, _, value) => ((SharedTextInput)b)._entry.Placeholder = value?.ToString());
    public static readonly BindableProperty IsPinProperty = BindableProperty.Create(
        nameof(IsPin), typeof(bool), typeof(SharedTextInput), false,
        propertyChanged: (b, _, value) => ((SharedTextInput)b).ApplyPin((bool)value));
    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(SharedTextInput), int.MaxValue,
        propertyChanged: (b, _, value) => ((SharedTextInput)b)._entry.MaxLength = (int)value);

    public SharedTextInput()
    {
        HeightRequest = 48;
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 };
        Padding = new Thickness(14, 0);
        this.Use(BackgroundColorProperty, "PosBackground");
        this.Use(StrokeProperty, "PosInputBorder");

        _entry = new Entry { BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Center };
        _entry.Use(Entry.TextColorProperty, "PosTextPrimary");
        _entry.Use(Entry.PlaceholderColorProperty, "PosTextPlaceholder");
        _entry.TextChanged += (_, e) => SetValue(TextProperty, e.NewTextValue ?? string.Empty);
        _entry.Completed += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
        Content = _entry;
    }

    public event EventHandler? Completed;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public bool IsPin { get => (bool)GetValue(IsPinProperty); set => SetValue(IsPinProperty, value); }
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    public Keyboard Keyboard { get => _entry.Keyboard; set => _entry.Keyboard = value; }
    public new bool Focus() => _entry.Focus();

    private void ApplyPin(bool isPin)
    {
        _entry.IsPassword = isPin;
        if (isPin)
        {
            _entry.Keyboard = Keyboard.Numeric;
            if (MaxLength == int.MaxValue) MaxLength = 4;
        }
    }
}

public sealed class SharedPinInput : SharedTextInput
{
    public SharedPinInput()
    {
        IsPin = true;
        MaxLength = 4;
        Placeholder = "Enter PIN";
    }
}
