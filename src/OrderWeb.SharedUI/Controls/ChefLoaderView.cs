using Microsoft.Maui.Graphics;

namespace OrderWeb.SharedUI.Controls;

public enum ChefLoaderMode
{
    Inline,
    Overlay,
    Fullscreen
}

public enum ChefLoaderSize
{
    Sm,
    Md,
    Lg
}

/// <summary>
/// Shared POS preloader (cartoon chef). Use for real waits via <see cref="IsLoading"/>.
/// Lives in SharedUI so Mother and Client share one control.
/// </summary>
public sealed class ChefLoaderView : ContentView
{
    private readonly Grid _root;
    private readonly BoxView _scrim;
    private readonly GraphicsView _art;
    private readonly ChefArtDrawable _drawable;
    private readonly Label _messageLabel;
    private readonly BoxView _dot1;
    private readonly BoxView _dot2;
    private readonly BoxView _dot3;
    private readonly HorizontalStackLayout _messageRow;
    private readonly VerticalStackLayout _column;
    private readonly HorizontalStackLayout _inlineRow;

    private IDispatcherTimer? _animTimer;
    private IDispatcherTimer? _delayTimer;
    private DateTime _animStartedUtc;
    private bool _isRevealed;

    public static readonly BindableProperty IsLoadingProperty =
        BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(ChefLoaderView), false,
            propertyChanged: (b, _, _) => ((ChefLoaderView)b).OnLoadingChanged());

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(ChefLoaderView), "Cooking up your data…",
            propertyChanged: (b, _, v) =>
            {
                var view = (ChefLoaderView)b;
                var text = string.IsNullOrWhiteSpace(v?.ToString()) ? string.Empty : v!.ToString()!;
                view._messageLabel.Text = text;
                view._messageRow.IsVisible = text.Length > 0;
            });

    public static readonly BindableProperty ModeProperty =
        BindableProperty.Create(nameof(Mode), typeof(ChefLoaderMode), typeof(ChefLoaderView), ChefLoaderMode.Overlay,
            propertyChanged: (b, _, _) => ((ChefLoaderView)b).ApplyChrome());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(ChefLoaderSize), typeof(ChefLoaderView), ChefLoaderSize.Md,
            propertyChanged: (b, _, _) => ((ChefLoaderView)b).ApplySize());

    public static readonly BindableProperty DelayMillisecondsProperty =
        BindableProperty.Create(nameof(DelayMilliseconds), typeof(int), typeof(ChefLoaderView), 300);

    public ChefLoaderView()
    {
        _drawable = new ChefArtDrawable();
        _art = new GraphicsView
        {
            Drawable = _drawable,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _messageLabel = new Label
        {
            Text = "Cooking up your data…",
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _messageLabel.TextColor = Color.FromArgb("#1E2A4A");

        _dot1 = CreateDot();
        _dot2 = CreateDot();
        _dot3 = CreateDot();
        var dots = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { _dot1, _dot2, _dot3 }
        };

        _messageRow = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { _messageLabel, dots }
        };

        _column = new VerticalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { _art, _messageRow }
        };

        _inlineRow = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { _art, _messageRow }
        };

        _scrim = new BoxView
        {
            Color = Color.FromArgb("#EBF8FAFC"),
            InputTransparent = false
        };

        _root = new Grid
        {
            Children = { _scrim, _column, _inlineRow }
        };

        Content = _root;
        InputTransparent = true;
        Opacity = 0;
        IsVisible = false;

        ApplySize();
        ApplyChrome();
        Unloaded += (_, _) => StopTimers();
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ChefLoaderMode Mode
    {
        get => (ChefLoaderMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public ChefLoaderSize Size
    {
        get => (ChefLoaderSize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public int DelayMilliseconds
    {
        get => (int)GetValue(DelayMillisecondsProperty);
        set => SetValue(DelayMillisecondsProperty, value);
    }

    private static BoxView CreateDot() =>
        new()
        {
            WidthRequest = 6,
            HeightRequest = 6,
            CornerRadius = 3,
            Color = Color.FromArgb("#1D7DFC"),
            Opacity = 0.3,
            VerticalOptions = LayoutOptions.Center
        };

    private void OnLoadingChanged()
    {
        CancelDelay();
        if (!IsLoading)
        {
            HideNow();
            return;
        }

        var delay = Math.Max(0, DelayMilliseconds);
        if (delay == 0)
        {
            RevealNow();
            return;
        }

        // Visible but transparent during delay must never steal taps.
        IsVisible = true;
        Opacity = 0;
        InputTransparent = true;
        _delayTimer = Dispatcher.CreateTimer();
        _delayTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _delayTimer.IsRepeating = false;
        _delayTimer.Tick += (_, _) =>
        {
            CancelDelay();
            if (IsLoading)
            {
                RevealNow();
            }
            else
            {
                HideNow();
            }
        };
        _delayTimer.Start();
    }

    private void RevealNow()
    {
        if (!IsLoading)
        {
            HideNow();
            return;
        }

        _isRevealed = true;
        IsVisible = true;
        Opacity = 1;
        // Inline never blocks surrounding content; overlay/fullscreen block only once revealed.
        InputTransparent = Mode == ChefLoaderMode.Inline;
        StartAnimation();
    }

    private void HideNow()
    {
        _isRevealed = false;
        StopTimers();
        Opacity = 0;
        IsVisible = false;
        InputTransparent = true;
    }

    private void CancelDelay()
    {
        if (_delayTimer is null)
        {
            return;
        }

        _delayTimer.Stop();
        _delayTimer = null;
    }

    private void StartAnimation()
    {
        StopAnimationOnly();
        _animStartedUtc = DateTime.UtcNow;
        _animTimer = Dispatcher.CreateTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(50);
        _animTimer.IsRepeating = true;
        _animTimer.Tick += (_, _) => TickAnimation();
        _animTimer.Start();
        TickAnimation();
    }

    private void StopAnimationOnly()
    {
        if (_animTimer is null)
        {
            return;
        }

        _animTimer.Stop();
        _animTimer = null;
    }

    private void StopTimers()
    {
        CancelDelay();
        StopAnimationOnly();
    }

    private void TickAnimation()
    {
        if (!_isRevealed || !IsLoading || !IsVisible || Opacity <= 0 || Window is null)
        {
            if (!_isRevealed || !IsLoading)
            {
                StopAnimationOnly();
            }

            return;
        }

        var elapsed = (DateTime.UtcNow - _animStartedUtc).TotalMilliseconds;
        var cycle = 1400.0;
        var t = (elapsed % cycle) / cycle;
        _drawable.Phase = (float)t;
        _art.Invalidate();

        // Dot bounce (1.2s cycle, staggered).
        AnimateDot(_dot1, elapsed, 0);
        AnimateDot(_dot2, elapsed, 150);
        AnimateDot(_dot3, elapsed, 300);
    }

    private static void AnimateDot(BoxView dot, double elapsedMs, double delayMs)
    {
        var cycle = 1200.0;
        var local = ((elapsedMs - delayMs) % cycle + cycle) % cycle;
        var u = local / cycle;
        // Peak near 30% of cycle.
        double opacity;
        double lift;
        if (u < 0.3)
        {
            var p = u / 0.3;
            opacity = 0.3 + 0.7 * Math.Sin(p * Math.PI);
            lift = -3 * Math.Sin(p * Math.PI);
        }
        else
        {
            opacity = 0.3;
            lift = 0;
        }

        dot.Opacity = opacity;
        dot.TranslationY = lift;
    }

    private void ApplySize()
    {
        var px = Size switch
        {
            ChefLoaderSize.Sm => 52,
            ChefLoaderSize.Lg => 140,
            _ => 96
        };
        _art.WidthRequest = px;
        _art.HeightRequest = px;
        _messageLabel.FontSize = Size switch
        {
            ChefLoaderSize.Sm => 12,
            ChefLoaderSize.Lg => 18,
            _ => 14
        };
        var dot = Size == ChefLoaderSize.Sm ? 5 : Size == ChefLoaderSize.Lg ? 7 : 6;
        foreach (var d in new[] { _dot1, _dot2, _dot3 })
        {
            d.WidthRequest = dot;
            d.HeightRequest = dot;
            d.CornerRadius = dot / 2.0;
        }
    }

    private void ApplyChrome()
    {
        var inline = Mode == ChefLoaderMode.Inline;
        _column.IsVisible = !inline;
        _inlineRow.IsVisible = inline;
        _scrim.IsVisible = !inline;
        _scrim.Color = Mode == ChefLoaderMode.Fullscreen
            ? Color.FromArgb("#EBF8FAFC")
            : Color.FromArgb("#E0FFFFFF");

        if (inline)
        {
            // Re-parent message into inline row if needed — both layouts share the same views;
            // ensure only one layout owns them at a time by clearing and re-adding.
            DetachSharedChildren();
            _inlineRow.Children.Add(_art);
            _inlineRow.Children.Add(_messageRow);
        }
        else
        {
            DetachSharedChildren();
            _column.Children.Add(_art);
            _column.Children.Add(_messageRow);
        }

        InputTransparent = inline || !_isRevealed;
        HorizontalOptions = inline ? LayoutOptions.Center : LayoutOptions.Fill;
        VerticalOptions = inline ? LayoutOptions.Center : LayoutOptions.Fill;
    }

    private void DetachSharedChildren()
    {
        _column.Children.Remove(_art);
        _column.Children.Remove(_messageRow);
        _inlineRow.Children.Remove(_art);
        _inlineRow.Children.Remove(_messageRow);
    }
}

/// <summary>Canvas drawing of the cartoon chef; <see cref="Phase"/> is 0..1 over the 1.4s gesture cycle.</summary>
internal sealed class ChefArtDrawable : IDrawable
{
    private static readonly Color White = Color.FromArgb("#FFFFFF");
    private static readonly Color Shade = Color.FromArgb("#E8EAF0");
    private static readonly Color Line = Color.FromArgb("#D7DBE4");
    private static readonly Color Skin = Color.FromArgb("#FFCFA8");
    private static readonly Color SkinShade = Color.FromArgb("#F2B489");
    private static readonly Color Hair = Color.FromArgb("#3A2A22");
    private static readonly Color Ink = Color.FromArgb("#1E2A4A");
    private static readonly Color Pan = Color.FromArgb("#34383F");
    private static readonly Color PanDark = Color.FromArgb("#23262B");
    private static readonly Color PanRim = Color.FromArgb("#4A4F58");
    private static readonly Color Red = Color.FromArgb("#E63946");
    private static readonly Color RedDark = Color.FromArgb("#C02B38");
    private static readonly Color Steam = Color.FromArgb("#C8CEDB");
    private static readonly Color Onion = Color.FromArgb("#F0D7B4");
    private static readonly Color OnionInner = Color.FromArgb("#D9B382");
    private static readonly Color Chilli = Color.FromArgb("#5CB85C");
    private static readonly Color ChilliEdge = Color.FromArgb("#3F8F3F");
    private static readonly Color Smile = Color.FromArgb("#A3282F");
    private static readonly Color Cheek = Color.FromArgb("#FFA98D");

    public float Phase { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var s = Math.Min(dirtyRect.Width, dirtyRect.Height) / 120f;
        if (s <= 0)
        {
            return;
        }

        canvas.SaveState();
        canvas.Translate(dirtyRect.Center.X - 60f * s, dirtyRect.Center.Y - 60f * s);
        canvas.Scale(s, s);

        var t = Phase;
        // Keyframes aligned with web CSS (bounce / pan-toss / toss).
        var bounceY = Sample(t, (0f, 0f), (0.22f, 1.5f), (0.45f, -3.5f), (0.70f, 0.5f), (1f, 0f));
        var headRot = Sample(t, (0f, 0f), (0.22f, 1.2f), (0.45f, -3f), (0.70f, 0.6f), (1f, 0f));
        var headY = Sample(t, (0f, 0f), (0.22f, 0.6f), (0.45f, -1.2f), (0.70f, 0f), (1f, 0f));
        var bodyRot = Sample(t, (0f, 0f), (0.45f, -1.4f), (1f, 0f));
        var armRot = Sample(t, (0f, 0f), (0.22f, 4f), (0.45f, -12f), (0.70f, 2.5f), (1f, 0f));
        var armY = Sample(t, (0f, 0f), (0.22f, 1.5f), (0.45f, -3f), (0.70f, 0.5f), (1f, 0f));

        DrawSteam(canvas, t);
        DrawFood(canvas, t, onion: true);
        DrawFood(canvas, t, onion: false);

        canvas.SaveState();
        canvas.Translate(0, bounceY);

        // Body
        canvas.SaveState();
        canvas.Translate(47, 110);
        canvas.Rotate(bodyRot);
        canvas.Translate(-47, -110);
        DrawBody(canvas);
        canvas.RestoreState();

        // Head
        canvas.SaveState();
        canvas.Translate(47, 72);
        canvas.Rotate(headRot);
        canvas.Translate(-47, -72);
        canvas.Translate(0, headY);
        DrawHead(canvas);
        canvas.RestoreState();

        // Arm + pan
        canvas.SaveState();
        canvas.Translate(62, 88);
        canvas.Rotate(armRot);
        canvas.Translate(-62, -88);
        canvas.Translate(0, armY);
        DrawArmAndPan(canvas);
        canvas.RestoreState();

        canvas.RestoreState(); // bounce
        canvas.RestoreState(); // scale
    }

    private static void DrawSteam(ICanvas canvas, float t)
    {
        DrawSteamWisp(canvas, t, 0f, 86, 40);
        DrawSteamWisp(canvas, (t + 0.32f) % 1f, 0f, 97, 37);
        DrawSteamWisp(canvas, (t + 0.65f) % 1f, 0f, 107, 41);
    }

    private static void DrawSteamWisp(ICanvas canvas, float t, float _, float x, float y)
    {
        // steam cycle ~1.7s vs 1.4s gesture; approximate with phase.
        var u = t;
        var opacity = u < 0.35f ? u / 0.35f * 0.85f : (1f - u) / 0.65f * 0.85f;
        var dy = 4f - u * 12f;
        canvas.StrokeColor = Steam.WithAlpha(Math.Clamp(opacity, 0f, 1f));
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineCap = LineCap.Round;
        var path = new PathF();
        path.MoveTo(x, y + dy);
        path.QuadTo(x + 4, y + dy - 6, x, y + dy - 11);
        canvas.DrawPath(path);
    }

    private static void DrawFood(ICanvas canvas, float t, bool onion)
    {
        var delay = onion ? 0f : 0.08f / 1.4f;
        var local = (t - delay + 1f) % 1f;
        float opacity;
        float ty;
        float scale;
        float rot;
        if (local < 0.20f || local > 0.78f)
        {
            opacity = local < 0.20f ? local / 0.20f : (1f - local) / 0.22f;
            ty = 8f;
            scale = 0.82f;
            rot = 0f;
        }
        else if (local < 0.52f)
        {
            var p = (local - 0.20f) / 0.32f;
            opacity = 1f;
            ty = 8f + (onion ? -21f : -23f) * p;
            scale = 0.82f + 0.18f * p;
            rot = (onion ? 11f : -13f) * p;
        }
        else
        {
            var p = (local - 0.52f) / 0.26f;
            opacity = 1f;
            ty = (onion ? -13f : -15f) + (onion ? 21f : 23f) * p;
            scale = 1f - 0.18f * p;
            rot = (onion ? 11f : -13f) * (1f - p);
        }

        canvas.SaveState();
        canvas.Translate(onion ? 93 : 109, onion ? 52 : 49);
        canvas.Translate(0, ty);
        canvas.Rotate(rot);
        canvas.Scale(scale, scale);

        if (onion)
        {
            canvas.StrokeColor = Onion.WithAlpha(Math.Clamp(opacity, 0f, 1f));
            canvas.StrokeSize = 3.2f;
            canvas.DrawCircle(0, 0, 5.6f);
            canvas.StrokeColor = OnionInner.WithAlpha(Math.Clamp(opacity, 0f, 1f));
            canvas.StrokeSize = 1f;
            canvas.DrawCircle(0, 0, 2.4f);
        }
        else
        {
            canvas.FillColor = Chilli.WithAlpha(Math.Clamp(opacity, 0f, 1f));
            canvas.StrokeColor = ChilliEdge.WithAlpha(Math.Clamp(opacity, 0f, 1f));
            canvas.StrokeSize = 0.9f;
            var chilli = new PathF();
            chilli.MoveTo(0, 0);
            chilli.QuadTo(7.5f, -1f, 5.5f, 7.2f);
            chilli.QuadTo(4.3f, 11.4f, -0.7f, 9.2f);
            chilli.QuadTo(-3.9f, 7.2f, -2.9f, 3f);
            chilli.QuadTo(-1.9f, 0f, 0, 0);
            chilli.Close();
            canvas.FillPath(chilli);
            canvas.DrawPath(chilli);
            canvas.StrokeColor = Color.FromArgb("#4B7F2A").WithAlpha(Math.Clamp(opacity, 0f, 1f));
            canvas.StrokeSize = 1.3f;
            canvas.StrokeLineCap = LineCap.Round;
            var stem = new PathF();
            stem.MoveTo(0, 0);
            stem.QuadTo(-2.4f, -2.2f, -3.6f, -1.2f);
            canvas.DrawPath(stem);
        }

        canvas.RestoreState();
    }

    private static void DrawBody(ICanvas canvas)
    {
        canvas.FillColor = White;
        canvas.StrokeColor = Line;
        canvas.StrokeSize = 1.4f;
        var coat = new PathF();
        coat.MoveTo(28, 110);
        coat.QuadTo(27, 80, 47, 80);
        coat.QuadTo(67, 80, 66, 110);
        coat.Close();
        canvas.FillPath(coat);
        canvas.DrawPath(coat);

        canvas.FillColor = Shade.WithAlpha(0.75f);
        var apron = new PathF();
        apron.MoveTo(34, 95);
        apron.QuadTo(47, 99, 60, 95);
        apron.LineTo(60, 110);
        apron.LineTo(34, 110);
        apron.Close();
        canvas.FillPath(apron);

        canvas.FillColor = Red;
        var scarf = new PathF();
        scarf.MoveTo(38, 75);
        scarf.QuadTo(47, 84, 56, 75);
        scarf.QuadTo(47, 69.5f, 38, 75);
        scarf.Close();
        canvas.FillPath(scarf);

        canvas.FillColor = RedDark;
        var knot = new PathF();
        knot.MoveTo(47, 80);
        knot.QuadTo(43, 84, 46, 87);
        knot.QuadTo(50, 86, 52, 81);
        knot.Close();
        canvas.FillPath(knot);

        canvas.FillColor = Ink;
        canvas.FillCircle(43, 89, 1.7f);
        canvas.FillCircle(43, 97, 1.7f);

        canvas.StrokeColor = White;
        canvas.StrokeSize = 8f;
        canvas.StrokeLineCap = LineCap.Round;
        var leftArm = new PathF();
        leftArm.MoveTo(32, 86);
        leftArm.QuadTo(28, 94, 30, 101);
        canvas.DrawPath(leftArm);
        canvas.FillColor = Skin;
        canvas.FillCircle(30.5f, 102, 4f);
    }

    private static void DrawHead(ICanvas canvas)
    {
        canvas.FillColor = White;
        canvas.StrokeColor = Line;
        canvas.StrokeSize = 1.4f;
        canvas.FillCircle(34, 24, 10);
        canvas.DrawCircle(34, 24, 10);
        canvas.FillCircle(47, 17, 12);
        canvas.DrawCircle(47, 17, 12);
        canvas.FillCircle(60, 24, 10);
        canvas.DrawCircle(60, 24, 10);
        canvas.FillRoundedRectangle(30, 27, 34, 15, 5.5f);
        canvas.DrawRoundedRectangle(30, 27, 34, 15, 5.5f);

        canvas.FillColor = SkinShade;
        canvas.FillCircle(30, 55, 4.2f);
        canvas.FillCircle(64, 55, 4.2f);

        canvas.FillColor = Skin;
        canvas.FillCircle(47, 54, 16);

        canvas.FillColor = Hair;
        var sideL = new PathF();
        sideL.MoveTo(32, 49);
        sideL.QuadTo(34.5f, 41, 42, 38.5f);
        sideL.QuadTo(36, 45, 35.5f, 51);
        sideL.Close();
        canvas.FillPath(sideL);
        var sideR = new PathF();
        sideR.MoveTo(62, 49);
        sideR.QuadTo(59.5f, 41, 52, 38.5f);
        sideR.QuadTo(58, 45, 58.5f, 51);
        sideR.Close();
        canvas.FillPath(sideR);

        canvas.StrokeColor = Hair;
        canvas.StrokeSize = 1.8f;
        canvas.StrokeLineCap = LineCap.Round;
        var brows = new PathF();
        brows.MoveTo(38, 45.5f);
        brows.QuadTo(42, 43, 46, 45);
        canvas.DrawPath(brows);
        var brows2 = new PathF();
        brows2.MoveTo(52, 45);
        brows2.QuadTo(56, 43, 60, 45.5f);
        canvas.DrawPath(brows2);

        canvas.StrokeSize = 2.3f;
        var eyeL = new PathF();
        eyeL.MoveTo(38.5f, 51);
        eyeL.QuadTo(42.3f, 46.8f, 45.9f, 51);
        canvas.DrawPath(eyeL);
        var eyeR = new PathF();
        eyeR.MoveTo(51.5f, 51);
        eyeR.QuadTo(55.3f, 46.8f, 58.9f, 51);
        canvas.DrawPath(eyeR);

        canvas.FillColor = Cheek.WithAlpha(0.7f);
        canvas.FillCircle(35.5f, 59.5f, 3.1f);
        canvas.FillCircle(59, 59.5f, 3.1f);

        canvas.FillColor = SkinShade;
        canvas.FillCircle(47.5f, 57.5f, 3.6f);

        canvas.FillColor = Hair;
        var stache = new PathF();
        stache.MoveTo(47.5f, 61.5f);
        stache.QuadTo(38, 58, 35, 64.5f);
        stache.QuadTo(41.5f, 69, 47.5f, 63.5f);
        stache.QuadTo(53.5f, 69, 60, 64.5f);
        stache.QuadTo(57, 58, 47.5f, 61.5f);
        stache.Close();
        canvas.FillPath(stache);

        canvas.FillColor = Smile;
        var mouth = new PathF();
        mouth.MoveTo(42, 66.5f);
        mouth.QuadTo(47.5f, 73.5f, 53, 66.5f);
        mouth.Close();
        canvas.FillPath(mouth);
    }

    private static void DrawArmAndPan(ICanvas canvas)
    {
        canvas.StrokeColor = White;
        canvas.StrokeSize = 8.5f;
        canvas.StrokeLineCap = LineCap.Round;
        var arm = new PathF();
        arm.MoveTo(62, 87);
        arm.QuadTo(73, 86, 79, 79);
        canvas.DrawPath(arm);

        canvas.StrokeColor = Skin;
        canvas.StrokeSize = 7f;
        var forearm = new PathF();
        forearm.MoveTo(72, 82);
        forearm.QuadTo(78, 80, 81, 76);
        canvas.DrawPath(forearm);

        canvas.FillColor = Skin;
        canvas.FillCircle(82, 75.5f, 4.6f);
        canvas.StrokeColor = SkinShade;
        canvas.StrokeSize = 0.8f;
        canvas.DrawCircle(82, 75.5f, 4.6f);

        canvas.SaveState();
        canvas.Translate(83, 74.6f);
        canvas.Rotate(-10);
        canvas.FillColor = PanDark;
        canvas.FillRoundedRectangle(-7, -2.1f, 14, 4.2f, 2.1f);
        canvas.RestoreState();

        canvas.FillColor = PanDark;
        var panBody = new PathF();
        panBody.MoveTo(88, 71);
        panBody.CurveTo(88, 71, 103, 79, 118, 71);
        panBody.Close();
        // Approximate ellipse bowl with fill ellipse bottom.
        canvas.FillEllipse(88, 67, 30, 12);

        canvas.FillColor = Pan;
        canvas.FillEllipse(88, 66.6f, 30, 8.8f);
        canvas.FillColor = PanRim.WithAlpha(0.55f);
        canvas.FillEllipse(91.5f, 68.1f, 23, 5.8f);
    }

    private static float Sample(float t, params (float At, float Value)[] keys)
    {
        if (keys.Length == 0)
        {
            return 0;
        }

        if (t <= keys[0].At)
        {
            return keys[0].Value;
        }

        for (var i = 1; i < keys.Length; i++)
        {
            if (t <= keys[i].At)
            {
                var a = keys[i - 1];
                var b = keys[i];
                var span = b.At - a.At;
                var p = span <= 0 ? 1f : (t - a.At) / span;
                // Smoothstep-ish ease.
                p = p * p * (3f - 2f * p);
                return a.Value + (b.Value - a.Value) * p;
            }
        }

        return keys[^1].Value;
    }
}
