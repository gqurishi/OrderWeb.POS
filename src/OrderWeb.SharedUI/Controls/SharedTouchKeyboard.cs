namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Opt-in attached behavior that opens the canonical SharedUI keyboard or numeric keypad.
/// Password fields are deliberately left for their dedicated secure flows.
/// </summary>
public static class SharedTouchKeyboard
{
    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled",
        typeof(bool),
        typeof(SharedTouchKeyboard),
        false,
        propertyChanged: OnEnabledChanged);

    private static readonly HashSet<VisualElement> OpenInputs = [];
    private static readonly Dictionary<VisualElement, long> SuppressUntilTicks = [];
    private static long _globalSuppressUntilTicks;
    private const int RefocusSuppressMs = 1200;

    public static bool GetEnabled(BindableObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(BindableObject target, bool value) => target.SetValue(EnabledProperty, value);

    /// <summary>
    /// Blocks every SharedUI keyboard open until <paramref name="milliseconds"/> elapse.
    /// Used after Cancel/Close so the same pointer click cannot reopen a field underneath.
    /// </summary>
    public static void SuppressAllBriefly(int milliseconds = RefocusSuppressMs) =>
        _globalSuppressUntilTicks = Math.Max(_globalSuppressUntilTicks, Environment.TickCount64 + Math.Max(0, milliseconds));

    private static bool IsGloballySuppressed() =>
        Environment.TickCount64 < _globalSuppressUntilTicks;

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var enabled = newValue is true;
        switch (bindable)
        {
            case Entry entry:
                entry.Focused -= OnEntryFocused;
                if (enabled) entry.Focused += OnEntryFocused;
                break;
            case Editor editor:
                editor.Focused -= OnEditorFocused;
                if (enabled) editor.Focused += OnEditorFocused;
                break;
        }
    }

    private static async void OnEntryFocused(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry entry || !CanOpen(entry) || entry.IsPassword)
        {
            return;
        }

        // After Cancel/Done, the same tap often lands on the field under the overlay.
        if (IsGloballySuppressed() || IsSuppressed(entry))
        {
            entry.Unfocus();
            return;
        }

        entry.Unfocus();
        if (IsNumeric(entry.Keyboard) || IsIpField(entry))
        {
            await EditNumericAsync(entry);
            return;
        }

        await EditAsync(
            entry,
            entry.Text,
            value => entry.Text = value,
            ResolveMode(entry.Keyboard),
            entry.Placeholder,
            entry.MaxLength);
    }

    private static async Task EditNumericAsync(Entry entry)
    {
        if (!OpenInputs.Add(entry)) return;

        try
        {
            var descriptor = $"{entry.AutomationId} {entry.Placeholder}".ToLowerInvariant();
            var mode = ResolveNumericMode(descriptor);
            var (minimum, maximum) = NumericRange(mode, descriptor);
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(BuildTitle(entry, entry.Placeholder), "Done");
            keyboard.SetPlaceholder(entry.Placeholder);
            keyboard.SetMaximumLength(ResolveMaxLength(entry, mode));
            keyboard.SetNumericMode(
                mode,
                minimum,
                maximum);
            keyboard.SetInitialText(entry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync(FindContentPage(entry));
            if (result is not null) entry.Text = result;
        }
        finally
        {
            SuppressAllBriefly(RefocusSuppressMs);
            MarkSuppressed(entry);
            OpenInputs.Remove(entry);
            entry.Unfocus();
        }
    }

    private static async void OnEditorFocused(object? sender, FocusEventArgs e)
    {
        if (sender is not Editor editor || !CanOpen(editor))
        {
            return;
        }

        if (IsGloballySuppressed() || IsSuppressed(editor))
        {
            editor.Unfocus();
            return;
        }

        editor.Unfocus();
        await EditAsync(
            editor,
            editor.Text,
            value => editor.Text = value,
            VirtualKeyboardTextMode.Notes,
            editor.Placeholder,
            editor.MaxLength);
    }

    private static async Task EditAsync(
        VisualElement input,
        string? initialValue,
        Action<string> apply,
        VirtualKeyboardTextMode mode,
        string? placeholder,
        int maximumLength)
    {
        if (!OpenInputs.Add(input))
        {
            return;
        }

        try
        {
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(BuildTitle(input, placeholder), "Done");
            keyboard.SetTextMode(mode);
            keyboard.SetPlaceholder(placeholder);
            keyboard.SetMaximumLength(maximumLength == int.MaxValue ? 0 : maximumLength);
            keyboard.SetInitialText(initialValue ?? string.Empty);

            var result = await keyboard.ShowAsync(FindContentPage(input));
            if (result is not null)
            {
                apply(result);
            }
        }
        finally
        {
            SuppressAllBriefly(RefocusSuppressMs);
            MarkSuppressed(input);
            OpenInputs.Remove(input);
            if (input is Entry entry)
            {
                entry.Unfocus();
            }
            else if (input is Editor editor)
            {
                editor.Unfocus();
            }
        }
    }

    private static bool CanOpen(VisualElement input) =>
        input.IsEnabled && input.IsVisible && !input.InputTransparent &&
        !HasAncestor<VirtualKeyboardDialog>(input) &&
        input switch
        {
            Entry entry => !entry.IsReadOnly,
            Editor editor => !editor.IsReadOnly,
            _ => false
        };

    private static bool IsNumeric(Keyboard keyboard) =>
        ReferenceEquals(keyboard, Keyboard.Numeric) ||
        ReferenceEquals(keyboard, Keyboard.Telephone);

    private static bool IsIpField(Entry entry)
    {
        var descriptor = $"{entry.AutomationId} {entry.Placeholder}".ToLowerInvariant();
        return descriptor.Contains("ip") || descriptor.Contains("mother pos ip");
    }

    private static VirtualKeyboardTextMode ResolveMode(Keyboard keyboard)
    {
        if (ReferenceEquals(keyboard, Keyboard.Email)) return VirtualKeyboardTextMode.Email;
        if (ReferenceEquals(keyboard, Keyboard.Telephone)) return VirtualKeyboardTextMode.Phone;
        return VirtualKeyboardTextMode.Text;
    }

    private static VirtualKeyboardNumericMode ResolveNumericMode(string descriptor)
    {
        if (descriptor.Contains("ip") || descriptor.Contains("host") && descriptor.Contains("address"))
            return VirtualKeyboardNumericMode.IpAddress;

        if (descriptor.Contains("gift") || descriptor.Contains("card number") || descriptor.Contains("order number") ||
            descriptor.Contains("pairing") || descriptor.Contains("long digits") || descriptor.Contains("pin"))
            return VirtualKeyboardNumericMode.LongDigits;

        if (descriptor.Contains("price") || descriptor.Contains("amount") || descriptor.Contains("cash") ||
            descriptor.Contains("cost") || descriptor.Contains("fee") || descriptor.Contains("tip") ||
            descriptor.Contains("refund") || descriptor.Contains("spent") || descriptor.Contains("balance"))
            return VirtualKeyboardNumericMode.Currency;

        if (descriptor.Contains("discount") || descriptor.Contains("percent") || descriptor.Contains("rate") ||
            descriptor.Contains("weight"))
            return VirtualKeyboardNumericMode.Decimal;

        if (descriptor.Contains("quantity") || descriptor.Contains("qty") ||
            descriptor.Contains("covers") || descriptor.Contains("guest"))
            return VirtualKeyboardNumericMode.Quantity;

        return VirtualKeyboardNumericMode.WholeNumber;
    }

    private static (decimal? Minimum, decimal? Maximum) NumericRange(
        VirtualKeyboardNumericMode mode,
        string descriptor) => mode switch
    {
        VirtualKeyboardNumericMode.Currency => (0m, 999999.99m),
        VirtualKeyboardNumericMode.Quantity => (1m, 999m),
        VirtualKeyboardNumericMode.Decimal when descriptor.Contains("discount") || descriptor.Contains("percent") => (0m, 100m),
        VirtualKeyboardNumericMode.Decimal => (0m, 999999.99m),
        VirtualKeyboardNumericMode.WholeNumber => (0m, 999999999m),
        VirtualKeyboardNumericMode.IpAddress => (null, null),
        VirtualKeyboardNumericMode.LongDigits => (null, null),
        _ => (null, null)
    };

    private static int ResolveMaxLength(Entry entry, VirtualKeyboardNumericMode mode)
    {
        if (entry.MaxLength is > 0 and < int.MaxValue)
        {
            return entry.MaxLength;
        }

        return mode switch
        {
            VirtualKeyboardNumericMode.IpAddress => 45,
            VirtualKeyboardNumericMode.LongDigits => 32,
            _ => 0
        };
    }

    private static string BuildTitle(VisualElement input, string? placeholder)
    {
        if (!string.IsNullOrWhiteSpace(placeholder)) return placeholder.Trim();
        if (!string.IsNullOrWhiteSpace(input.AutomationId)) return input.AutomationId.Replace("Entry", string.Empty).Trim();
        return input is Editor ? "Enter notes" : "Enter text";
    }

    private static ContentPage? FindContentPage(Element element)
    {
        Element? current = element;
        while (current is not null)
        {
            if (current is ContentPage page) return page;
            current = current.Parent;
        }
        return null;
    }

    private static bool HasAncestor<T>(Element element) where T : Element
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (current is T) return true;
            current = current.Parent;
        }
        return false;
    }

    private static void MarkSuppressed(VisualElement input) =>
        SuppressUntilTicks[input] = Environment.TickCount64 + RefocusSuppressMs;

    private static bool IsSuppressed(VisualElement input)
    {
        if (!SuppressUntilTicks.TryGetValue(input, out var until))
        {
            return false;
        }

        if (Environment.TickCount64 >= until)
        {
            SuppressUntilTicks.Remove(input);
            return false;
        }

        return true;
    }
}
