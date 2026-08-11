using Microsoft.Maui.Controls;
using POS_in_NET.Services;

namespace POS_in_NET.Helpers;

/// <summary>
/// App-wide responsive behavior applied by the implicit ContentPage style.
/// It preserves each page's design while enforcing usable bounds and keyboard access.
/// </summary>
public static class ResponsiveLayout
{
    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled",
        typeof(bool),
        typeof(ResponsiveLayout),
        false,
        propertyChanged: OnEnabledChanged);

    public static readonly BindableProperty SizeClassProperty = BindableProperty.CreateAttached(
        "SizeClass",
        typeof(ResponsiveSizeClass),
        typeof(ResponsiveLayout),
        ResponsiveSizeClass.Standard);

    private static readonly BindableProperty OriginalRootPaddingProperty = BindableProperty.CreateAttached(
        "OriginalRootPadding",
        typeof(Thickness?),
        typeof(ResponsiveLayout),
        null);

    private static readonly BindableProperty KeyboardHookedProperty = BindableProperty.CreateAttached(
        "KeyboardHooked",
        typeof(bool),
        typeof(ResponsiveLayout),
        false);

    public static bool GetEnabled(BindableObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(BindableObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static ResponsiveSizeClass GetSizeClass(BindableObject target) =>
        (ResponsiveSizeClass)target.GetValue(SizeClassProperty);

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ContentPage page || newValue is not true) return;

        page.Loaded += OnPageLoaded;
        page.SizeChanged += OnPageSizeChanged;
        Apply(page);
    }

    private static void OnPageLoaded(object? sender, EventArgs e)
    {
        if (sender is ContentPage page)
        {
            Apply(page);
            PosPerformanceMonitor.MarkCurrentNavigationFrameVisible(page.GetType().Name);
        }
    }

    private static void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ContentPage page) Apply(page);
    }

    private static void Apply(ContentPage page)
    {
        if (page.Width <= 0 || page.Height <= 0) return;

        var profile = TabletLayoutHelper.GetProfile(page.Width, page.Height);
        page.SetValue(SizeClassProperty, profile.SizeClass);
        UpdateSharedResources(profile);

        if (page.Content is Layout root)
        {
            var original = (Thickness?)root.GetValue(OriginalRootPaddingProperty);
            if (original == null)
            {
                original = root.Padding;
                root.SetValue(OriginalRootPaddingProperty, original);
            }

            root.Padding = new Thickness(
                original.Value.Left,
                original.Value.Top,
                original.Value.Right,
                Math.Max(original.Value.Bottom, profile.SafeBottom));
        }

        var availableWidth = Math.Max(280, page.Width - (profile.DialogMargin * 2));
        var availableHeight = Math.Max(280, page.Height - profile.SafeBottom - (profile.DialogMargin * 2));
        ApplyToTree(page.Content, availableWidth, availableHeight, profile);
    }

    private static void UpdateSharedResources(ResponsiveLayoutProfile profile)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        resources["ResponsivePagePadding"] = new Thickness(profile.PagePadding);
        resources["ResponsiveCompactPadding"] = new Thickness(profile.PagePadding, Math.Max(8, profile.PagePadding - 2));
        resources["ResponsiveCardMargin"] = new Thickness(
            profile.PagePadding,
            Math.Max(8, profile.PagePadding - 6),
            profile.PagePadding,
            0);
        resources["ResponsiveDialogMargin"] = new Thickness(profile.DialogMargin);
        resources["ResponsiveDialogPadding"] = new Thickness(profile.DialogPadding);
        resources["ResponsiveSafeBottom"] = profile.SafeBottom;
        resources["ResponsiveSectionSpacing"] = profile.Spacing;
        resources["ResponsiveTextScale"] = profile.TextScale;
        resources["ResponsiveLongListHeight"] = Math.Max(300, Math.Min(700, profile.Height - 240));
        resources["ResponsiveDialogBodyHeight"] = Math.Max(220, profile.Height - 220);
    }

    private static void ApplyToTree(
        Element? element,
        double availableWidth,
        double availableHeight,
        ResponsiveLayoutProfile profile)
    {
        if (element == null) return;

        if (element is Button button)
        {
            button.MinimumHeightRequest = Math.Max(44, button.MinimumHeightRequest);
            button.MinimumWidthRequest = Math.Max(44, button.MinimumWidthRequest);
        }
        else if (element is InputView input)
        {
            input.MinimumHeightRequest = Math.Max(44, input.MinimumHeightRequest);
            HookKeyboardScroll(input);
        }
        else if (element is Picker picker)
        {
            picker.MinimumHeightRequest = Math.Max(44, picker.MinimumHeightRequest);
            HookKeyboardScroll(picker);
        }
        else if (element is DatePicker datePicker)
        {
            datePicker.MinimumHeightRequest = Math.Max(44, datePicker.MinimumHeightRequest);
            HookKeyboardScroll(datePicker);
        }
        else if (element is TimePicker timePicker)
        {
            timePicker.MinimumHeightRequest = Math.Max(44, timePicker.MinimumHeightRequest);
            HookKeyboardScroll(timePicker);
        }

        if (element is Border border)
        {
            if (border.WidthRequest > availableWidth) border.WidthRequest = availableWidth;
            if (border.MaximumWidthRequest > availableWidth) border.MaximumWidthRequest = availableWidth;
            if (border.MaximumHeightRequest > availableHeight) border.MaximumHeightRequest = availableHeight;

            if (string.Equals(border.StyleId, "DialogCard", StringComparison.OrdinalIgnoreCase))
            {
                border.Margin = new Thickness(profile.DialogMargin);
                border.Padding = new Thickness(profile.DialogPadding);
            }
        }

        if (element is ScrollView or CollectionView or ListView)
        {
            if (element is VisualElement list && list.HeightRequest >= 500)
                list.HeightRequest = -1;
        }

        if (element is not IVisualTreeElement tree) return;
        foreach (var child in tree.GetVisualChildren().OfType<Element>())
            ApplyToTree(child, availableWidth, availableHeight, profile);
    }

    private static void HookKeyboardScroll(VisualElement input)
    {
        if ((bool)input.GetValue(KeyboardHookedProperty)) return;
        input.SetValue(KeyboardHookedProperty, true);
        input.Focused += async (_, _) =>
        {
            var parent = input.Parent;
            while (parent != null && parent is not ScrollView)
                parent = parent.Parent;

            if (parent is ScrollView scrollView)
            {
                await Task.Delay(80);
                await scrollView.ScrollToAsync(input, ScrollToPosition.Center, true);
            }
        };
    }
}
