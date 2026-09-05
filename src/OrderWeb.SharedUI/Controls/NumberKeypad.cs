using System.Windows.Input;

namespace OrderWeb.SharedUI.Controls;

public class NumberKeypad : ContentView
{
    public static readonly BindableProperty KeyCommandProperty = BindableProperty.Create(nameof(KeyCommand), typeof(ICommand), typeof(NumberKeypad));
    public static readonly BindableProperty ClearCommandProperty = BindableProperty.Create(nameof(ClearCommand), typeof(ICommand), typeof(NumberKeypad));
    public static readonly BindableProperty BackspaceCommandProperty = BindableProperty.Create(nameof(BackspaceCommand), typeof(ICommand), typeof(NumberKeypad));
    public static readonly BindableProperty KeySizeProperty = BindableProperty.Create(nameof(KeySize), typeof(double), typeof(NumberKeypad), 72d, propertyChanged: (b, _, _) => ((NumberKeypad)b).Build());
    public static readonly BindableProperty ShowActionsProperty = BindableProperty.Create(nameof(ShowActions), typeof(bool), typeof(NumberKeypad), true, propertyChanged: (b, _, _) => ((NumberKeypad)b).Build());

    public NumberKeypad() => Build();

    public event EventHandler<KeypadKeyEventArgs>? KeyPressed;
    public event EventHandler? ClearPressed;
    public event EventHandler? BackspacePressed;
    public ICommand? KeyCommand { get => (ICommand?)GetValue(KeyCommandProperty); set => SetValue(KeyCommandProperty, value); }
    public ICommand? ClearCommand { get => (ICommand?)GetValue(ClearCommandProperty); set => SetValue(ClearCommandProperty, value); }
    public ICommand? BackspaceCommand { get => (ICommand?)GetValue(BackspaceCommandProperty); set => SetValue(BackspaceCommandProperty, value); }
    public double KeySize { get => (double)GetValue(KeySizeProperty); set => SetValue(KeySizeProperty, value); }
    public bool ShowActions { get => (bool)GetValue(ShowActionsProperty); set => SetValue(ShowActionsProperty, value); }

    private void Build()
    {
        var grid = new Grid { RowSpacing = 12, ColumnSpacing = 12, HorizontalOptions = LayoutOptions.Center };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(KeySize));
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition(KeySize));

        for (var digit = 1; digit <= 9; digit++)
            AddKey(grid, digit.ToString(), (digit - 1) / 3, (digit - 1) % 3);

        if (ShowActions) AddAction(grid, "Clear", 3, 0, false);
        AddKey(grid, "0", 3, 1);
        if (ShowActions) AddAction(grid, "⌫", 3, 2, true);
        Content = grid;
    }

    private Button NewButton(string text)
    {
        var button = new Button { Text = text, WidthRequest = KeySize, HeightRequest = KeySize, CornerRadius = (int)(KeySize / 2), Padding = 0, FontSize = KeySize >= 90 ? 34 : 24, FontAttributes = FontAttributes.Bold };
        button.Use(Button.BackgroundColorProperty, "PosSurface");
        button.Use(Button.TextColorProperty, "PosTextPrimary");
        button.Use(Button.BorderColorProperty, "PosBorderStrong");
        button.BorderWidth = 2;
        return button;
    }

    private void AddKey(Grid grid, string key, int row, int column)
    {
        var button = NewButton(key);
        button.Clicked += (_, _) => { KeyPressed?.Invoke(this, new KeypadKeyEventArgs(key)); if (KeyCommand?.CanExecute(key) == true) KeyCommand.Execute(key); };
        grid.Add(button, column, row);
    }

    private void AddAction(Grid grid, string text, int row, int column, bool backspace)
    {
        var button = NewButton(text);
        button.FontSize = KeySize >= 90 ? 18 : 14;
        if (backspace) button.Use(Button.TextColorProperty, "PosError");
        button.Clicked += (_, _) =>
        {
            if (backspace) { BackspacePressed?.Invoke(this, EventArgs.Empty); if (BackspaceCommand?.CanExecute(null) == true) BackspaceCommand.Execute(null); }
            else { ClearPressed?.Invoke(this, EventArgs.Empty); if (ClearCommand?.CanExecute(null) == true) ClearCommand.Execute(null); }
        };
        grid.Add(button, column, row);
    }
}
