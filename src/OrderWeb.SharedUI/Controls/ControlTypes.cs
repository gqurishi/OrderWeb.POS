namespace OrderWeb.SharedUI.Controls;

public enum ButtonVariant
{
    Primary,
    Secondary,
    Success,
    Danger,
    /// <summary>Neutral grey utility actions (e.g. MORE).</summary>
    Utility
}

public enum StatusKind
{
    Neutral,
    Info,
    Success,
    Warning,
    Error
}

public sealed class KeypadKeyEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}

internal static class ControlResources
{
    public static T Value<T>(string key, T fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is T typed)
        {
            return typed;
        }

        return fallback;
    }

    public static void Use(this Element target, BindableProperty property, string key) =>
        target.SetDynamicResource(property, key);
}
