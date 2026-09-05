using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Floors;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Shared Mother-style floor/table plan used by Mother and Client hosts.
/// Hosts map services into <see cref="FloorPlanDto"/> and handle action events.
/// </summary>
public sealed class FloorPlanView : ContentView
{
    private readonly HorizontalStackLayout _floorTabs = new() { Spacing = 8, VerticalOptions = LayoutOptions.Center };
    private readonly Label _syncLabel = new() { FontSize = 11, VerticalOptions = LayoutOptions.Center };
    private readonly Border _syncChip = new()
    {
        StrokeThickness = 1,
        Padding = new Thickness(10, 7),
        MinimumHeightRequest = 44,
        StrokeShape = new RoundRectangle { CornerRadius = 10 }
    };
    private readonly HorizontalStackLayout _adminTools = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Center
    };
    private readonly Border _statusBanner = new() { IsVisible = false, Padding = new Thickness(15, 10) };
    private readonly Label _statusBannerLabel = new()
    {
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };
    private readonly AbsoluteLayout _canvas = new() { BackgroundColor = Colors.White };
    private readonly Image _backgroundImage = new() { Aspect = Aspect.Fill, Opacity = 0.82, IsVisible = false };
    private readonly Image _backgroundBlur = new()
    {
        Aspect = Aspect.Fill,
        Opacity = 0.12,
        TranslationX = 1.5,
        TranslationY = 1.5,
        IsVisible = false
    };
    private readonly VerticalStackLayout _emptyState = new()
    {
        Spacing = 15,
        IsVisible = false,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };
    private readonly Grid _loadingOverlay = new()
    {
        IsVisible = false,
        BackgroundColor = Color.FromArgb("#80FFFFFF"),
        ZIndex = 900
    };
    private readonly Label _loadingLabel = new()
    {
        Text = "Loading layout...",
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        HorizontalOptions = LayoutOptions.Center
    };
    private readonly Grid _coverOverlay = new()
    {
        IsVisible = false,
        BackgroundColor = Color.FromArgb("#80000000"),
        ZIndex = 100
    };
    private readonly Label _coverTitle = new() { FontSize = 20, FontAttributes = FontAttributes.Bold };
    private readonly Label _customCoverLabel = new()
    {
        Text = "Other number...",
        FontSize = 15,
        TextColor = Color.FromArgb("#9CA3AF"),
        VerticalOptions = LayoutOptions.Center
    };
    private readonly Entry _customCoverEntry = new()
    {
        Keyboard = Keyboard.Numeric,
        Placeholder = "Other number...",
        FontSize = 15,
        IsVisible = false,
        HeightRequest = 40
    };
    private readonly Grid _actionOverlay = new()
    {
        IsVisible = false,
        BackgroundColor = Color.FromArgb("#80000000"),
        ZIndex = 110
    };
    private readonly Label _actionTitle = new()
    {
        FontSize = 20,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Label _actionSubtitle = new()
    {
        FontSize = 14,
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly VerticalStackLayout _actionButtons = new() { Spacing = 10 };
    private readonly Grid _targetPickerOverlay = new()
    {
        IsVisible = false,
        BackgroundColor = Color.FromArgb("#80000000"),
        ZIndex = 120
    };
    private readonly Label _targetPickerTitle = new()
    {
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly VerticalStackLayout _targetPickerList = new() { Spacing = 8 };

    private FloorPlanDto _state = CreateEmptyState();
    private FloorTableDto? _pendingTable;
    private int _customCoverValue;
    private readonly Dictionary<int, Border> _tableViews = new();
    private double _dragOriginX;
    private double _dragOriginY;
    private double _dragStartX;
    private double _dragStartY;

    public FloorPlanView()
    {
        BuildEmptyState();
        BuildLoadingOverlay();
        BuildCoverOverlay();
        BuildActionOverlay();
        BuildTargetPickerOverlay();
        _syncChip.Content = _syncLabel;
        _statusBanner.Content = _statusBannerLabel;
        Content = BuildLayout();
        this.Use(BackgroundColorProperty, "PosBackground");
        _loadingLabel.Use(Label.TextColorProperty, "PosTextStrong");
        SizeChanged += (_, _) => ApplyResponsiveLayout(Width, Height);
    }

    public event EventHandler<int>? FloorSelected;
    public event EventHandler<FloorTableActionRequest>? TableActionRequested;
    public event EventHandler<FloorTableMovedEvent>? TableMoved;
    public event EventHandler? FloorManagementRequested;
    public event EventHandler? TableManagementRequested;
    public event EventHandler? SetBackgroundRequested;
    public event EventHandler? RemoveBackgroundRequested;
    public event EventHandler? SaveLayoutRequested;

    public FloorPlanDto CurrentState => _state;

    public void Apply(FloorPlanDto state)
    {
        _state = state ?? CreateEmptyState();
        Render();
    }

    public void SetLoading(bool isLoading, string? message = null)
    {
        _loadingOverlay.IsVisible = isLoading;
        _loadingLabel.Text = string.IsNullOrWhiteSpace(message) ? "Loading layout..." : message!;
    }

    public void SetStatusBanner(string? message, string tone = "warning")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _statusBanner.IsVisible = false;
            return;
        }

        _statusBannerLabel.Text = message;
        ApplyBannerTone(tone);
        _statusBanner.IsVisible = true;
    }

    private void Render()
    {
        RenderSyncChip();
        RenderFloorTabs();
        RenderAdminTools();
        RenderBackground();
        RenderTables();
        RenderStatusBanner();
        SetLoading(_state.IsLoading, _state.LoadingMessage);
        ApplyResponsiveLayout(Width, Height);
    }

    private View BuildLayout()
    {
        AbsoluteLayout.SetLayoutBounds(_backgroundImage, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_backgroundImage, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_backgroundBlur, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_backgroundBlur, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_emptyState, new Rect(0.5, 0.5, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(_emptyState, AbsoluteLayoutFlags.PositionProportional);

        _canvas.Children.Add(_backgroundImage);
        _canvas.Children.Add(_backgroundBlur);
        _canvas.Children.Add(_emptyState);

        var toolbarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star))
            },
            ColumnSpacing = 12
        };
        toolbarGrid.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            MaximumHeightRequest = 48,
            Content = _floorTabs
        });
        toolbarGrid.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalOptions = LayoutOptions.Fill,
            MaximumHeightRequest = 48,
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Children = { _syncChip, _adminTools }
            }
        }, 1);

        var toolbar = new Border
        {
            StrokeThickness = 0,
            MinimumHeightRequest = 64,
            MaximumHeightRequest = 72,
            Padding = new Thickness(12, 9),
            Content = toolbarGrid
        };
        toolbar.Use(Border.BackgroundColorProperty, "PosSurface");

        var canvasHost = new Grid
        {
            Padding = new Thickness(12, 10, 12, 12),
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            Children = { _canvas, _loadingOverlay }
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        root.Add(toolbar);
        root.Add(_statusBanner, 0, 1);
        root.Add(canvasHost, 0, 2);
        root.Add(_coverOverlay);
        root.Add(_actionOverlay);
        root.Add(_targetPickerOverlay);
        Grid.SetRowSpan(_coverOverlay, 3);
        Grid.SetRowSpan(_actionOverlay, 3);
        Grid.SetRowSpan(_targetPickerOverlay, 3);
        root.Use(Grid.BackgroundColorProperty, "PosBackground");
        return root;
    }

    private void BuildEmptyState()
    {
        _emptyState.Children.Add(new Border
        {
            Padding = 20,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 50 },
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            HorizontalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = "table_1.png",
                WidthRequest = 60,
                HeightRequest = 60,
                Opacity = 0.4
            }
        });
        _emptyState.Children.Add(new Label
        {
            Text = "No Tables on This Floor",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#9CA3AF"),
            HorizontalOptions = LayoutOptions.Center
        });
        _emptyState.Children.Add(new Label
        {
            Text = "Add tables from Table Management",
            FontSize = 14,
            TextColor = Color.FromArgb("#D1D5DB"),
            HorizontalOptions = LayoutOptions.Center
        });
    }

    private void BuildLoadingOverlay()
    {
        _loadingOverlay.Children.Add(new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 10,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = Color.FromArgb("#3B82F6"),
                    WidthRequest = 40,
                    HeightRequest = 40
                },
                _loadingLabel
            }
        });
    }

    private void BuildCoverOverlay()
    {
        var close = CreateIconButton("X", Color.FromArgb("#FEE2E2"), Color.FromArgb("#DC2626"), HideCoverPicker);
        var numbers = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            Padding = 20
        };

        for (var i = 1; i <= 12; i++)
        {
            var covers = i;
            var button = new Button
            {
                Text = i.ToString(),
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#1F2937"),
                CornerRadius = 10,
                HeightRequest = 56,
                Padding = 0
            };
            button.Clicked += (_, _) => ConfirmCovers(covers);
            numbers.Add(button, (i - 1) % 4, (i - 1) / 4);
        }

        var go = new SharedButton { Text = "Go", WidthRequest = 64, HeightRequest = 48 };
        go.Clicked += (_, _) =>
        {
            if (_customCoverValue > 0)
            {
                ConfirmCovers(_customCoverValue);
                return;
            }

            if (int.TryParse(_customCoverEntry.Text, out var entered) && entered > 0)
            {
                ConfirmCovers(entered);
            }
        };

        var customTap = new TapGestureRecognizer();
        customTap.Tapped += (_, _) =>
        {
            _customCoverEntry.IsVisible = true;
            _customCoverLabel.IsVisible = false;
            _customCoverEntry.Focus();
        };
        var customField = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#D1D5DB"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Grid { Children = { _customCoverLabel, _customCoverEntry } }
        };
        customField.GestureRecognizers.Add(customTap);
        _customCoverEntry.TextChanged += (_, e) =>
        {
            _ = int.TryParse(e.NewTextValue, out _customCoverValue);
            if (_customCoverValue > 0)
            {
                _customCoverLabel.Text = _customCoverValue.ToString();
                _customCoverLabel.TextColor = Color.FromArgb("#1F2937");
            }
        };

        var footer = new Grid
        {
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            Padding = new Thickness(20, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        footer.Add(customField);
        footer.Add(go, 1);

        var header = new Grid
        {
            Padding = new Thickness(24, 20),
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                _coverTitle,
                new Label
                {
                    Text = "How many guests?",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#6B7280")
                }
            }
        });
        header.Add(close, 1);
        _coverTitle.Use(Label.TextColorProperty, "PosTextStrong");

        var card = new Border
        {
            WidthRequest = 380,
            MaximumWidthRequest = 420,
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Opacity = 0.25f,
                Radius = 24,
                Offset = new Point(0, 8)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children = { header, numbers, footer }
            }
        };
        _coverOverlay.Children.Add(card);
    }

    private void BuildActionOverlay()
    {
        var close = CreateIconButton("X", Color.FromArgb("#FEE2E2"), Color.FromArgb("#DC2626"), HideActionSheet);
        var card = new Border
        {
            WidthRequest = 360,
            MaximumWidthRequest = 420,
            Padding = 20,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Opacity = 0.25f,
                Radius = 24,
                Offset = new Point(0, 8)
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");
        _actionTitle.Use(Label.TextColorProperty, "PosTextStrong");
        _actionSubtitle.Use(Label.TextColorProperty, "PosTextMuted");

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(new VerticalStackLayout { Spacing = 4, Children = { _actionTitle, _actionSubtitle } });
        header.Add(close, 1);

        card.Content = new VerticalStackLayout
        {
            Spacing = 14,
            Children = { header, _actionButtons }
        };
        _actionOverlay.Children.Add(card);
    }

    private void BuildTargetPickerOverlay()
    {
        var close = CreateIconButton("X", Color.FromArgb("#FEE2E2"), Color.FromArgb("#DC2626"), HideTargetPicker);
        var card = new Border
        {
            WidthRequest = 380,
            MaximumWidthRequest = 440,
            MaximumHeightRequest = 520,
            Padding = 20,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");
        _targetPickerTitle.Use(Label.TextColorProperty, "PosTextStrong");

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(_targetPickerTitle);
        header.Add(close, 1);

        card.Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                header,
                new ScrollView
                {
                    MaximumHeightRequest = 400,
                    Content = _targetPickerList
                }
            }
        };
        _targetPickerOverlay.Children.Add(card);
    }

    private void RenderSyncChip()
    {
        var sync = _state.SyncStatus;
        _syncLabel.Text = sync.DisplayText;
        if (sync.IsStale || !sync.IsOnline)
        {
            _syncChip.BackgroundColor = Color.FromArgb("#FEF3C7");
            _syncChip.Stroke = Color.FromArgb("#F59E0B");
            _syncLabel.TextColor = Color.FromArgb("#92400E");
        }
        else
        {
            _syncChip.BackgroundColor = Color.FromArgb("#F0FDFA");
            _syncChip.Stroke = Color.FromArgb("#CCFBF1");
            _syncLabel.TextColor = Color.FromArgb("#0F766E");
        }
    }

    private void RenderFloorTabs()
    {
        _floorTabs.Children.Clear();
        foreach (var floor in _state.Floors)
        {
            var selected = floor.IsSelected;
            var tab = new Border
            {
                BackgroundColor = Color.FromArgb(selected ? "#3B82F6" : "#F3F4F6"),
                Stroke = Color.FromArgb(selected ? "#2563EB" : "#E5E7EB"),
                StrokeThickness = 1,
                Padding = new Thickness(16, 8),
                MinimumHeightRequest = 44,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new Label
                {
                    Text = $"{floor.Name} ({floor.TableCount})",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = selected ? Colors.White : Color.FromArgb("#374151"),
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var floorId = floor.Id;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => FloorSelected?.Invoke(this, floorId);
            tab.GestureRecognizers.Add(tap);
            _floorTabs.Children.Add(tab);
        }
    }

    private void RenderAdminTools()
    {
        _adminTools.Children.Clear();
        if (!_state.Capabilities.ShowAdminTools)
        {
            return;
        }

        _adminTools.Children.Add(CreateToolChip("Floor Management", "#EFF6FF", "#BFDBFE", "#1D4ED8",
            () => FloorManagementRequested?.Invoke(this, EventArgs.Empty)));
        _adminTools.Children.Add(CreateToolChip("Table Management", "#ECFDF5", "#BBF7D0", "#047857",
            () => TableManagementRequested?.Invoke(this, EventArgs.Empty)));

        if (_state.Capabilities.AllowBackgroundEdit)
        {
            _adminTools.Children.Add(CreateToolChip("Set Background", "#F3F4F6", "#E5E7EB", "#374151",
                () => SetBackgroundRequested?.Invoke(this, EventArgs.Empty)));
            if (!string.IsNullOrWhiteSpace(_state.BackgroundImagePath))
            {
                _adminTools.Children.Add(CreateToolChip("Remove Background", "#FEE2E2", "#FECACA", "#DC2626",
                    () => RemoveBackgroundRequested?.Invoke(this, EventArgs.Empty)));
            }
        }

        if (_state.Capabilities.AllowLayoutEdit)
        {
            _adminTools.Children.Add(CreateToolChip("Save Layout", "#3B82F6", "Transparent", "#FFFFFF",
                () => SaveLayoutRequested?.Invoke(this, EventArgs.Empty), filled: true));
        }
    }

    private void RenderBackground()
    {
        var path = _state.BackgroundImagePath;
        var hasBackground = !string.IsNullOrWhiteSpace(path);
        _backgroundImage.IsVisible = hasBackground;
        _backgroundBlur.IsVisible = hasBackground;
        _backgroundImage.Source = hasBackground ? ImageSource.FromFile(path!) : null;
        _backgroundBlur.Source = hasBackground ? ImageSource.FromFile(path!) : null;
    }

    private void RenderTables()
    {
        foreach (var existing in _tableViews.Values.ToList())
        {
            _canvas.Children.Remove(existing);
        }

        _tableViews.Clear();
        var tables = _state.Tables;
        _emptyState.IsVisible = tables.Count == 0;
        if (tables.Count == 0)
        {
            return;
        }

        var index = 0;
        foreach (var table in tables)
        {
            var tile = CreateTableTile(table);
            var (width, height) = FloorTableVisualStyles.SizeFor(table.Shape);
            var x = table.PositionX > 0 ? table.PositionX : 40 + (index % 6) * 140;
            var y = table.PositionY > 0 ? table.PositionY : 40 + (index / 6) * 140;
            AbsoluteLayout.SetLayoutBounds(tile, new Rect(x, y, width, height));
            AbsoluteLayout.SetLayoutFlags(tile, AbsoluteLayoutFlags.None);
            _canvas.Children.Add(tile);
            _tableViews[table.Id] = tile;
            index++;
        }
    }

    private Border CreateTableTile(FloorTableDto table)
    {
        var (bg, border, text) = FloorTableVisualStyles.ColorsFor(table.VisualState);
        var (width, height) = FloorTableVisualStyles.SizeFor(table.Shape);
        var corner = table.Shape == FloorTableShapeKind.Rectangle ? 10 : 12;

        var content = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image
                {
                    Source = string.IsNullOrWhiteSpace(table.DesignIcon) ? "table_1.png" : table.DesignIcon,
                    WidthRequest = 44,
                    HeightRequest = 44,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = table.TableNumber,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(text),
                    HorizontalOptions = LayoutOptions.Center
                },
                new Ellipse
                {
                    WidthRequest = 10,
                    HeightRequest = 10,
                    Fill = Color.FromArgb(border),
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };

        if (table.VisualState != FloorTableVisualState.Free)
        {
            var guestLine = table.GuestCount > 0
                ? $"{table.GuestCount} guests"
                : table.Capacity > 0 ? $"{table.Capacity} seats" : string.Empty;
            if (!string.IsNullOrWhiteSpace(guestLine))
            {
                content.Children.Add(new Label
                {
                    Text = guestLine,
                    FontSize = 10,
                    TextColor = Color.FromArgb(text),
                    HorizontalOptions = LayoutOptions.Center
                });
            }

            var orderLine = table.OrderSummary ?? table.SessionStatus;
            if (!string.IsNullOrWhiteSpace(orderLine))
            {
                content.Children.Add(new Label
                {
                    Text = orderLine,
                    FontSize = 10,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1,
                    TextColor = Color.FromArgb(text),
                    HorizontalOptions = LayoutOptions.Center
                });
            }
        }

        var tile = new Border
        {
            WidthRequest = width,
            HeightRequest = height,
            BackgroundColor = Color.FromArgb(bg),
            Stroke = Color.FromArgb(border),
            StrokeThickness = 2,
            Padding = new Thickness(8),
            StrokeShape = new RoundRectangle { CornerRadius = corner },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(2, 2),
                Radius = 8,
                Opacity = 0.15f
            },
            Content = content,
            BindingContext = table
        };

        if (_state.Capabilities.AllowLayoutEdit)
        {
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += (_, e) => OnTableDrag(table, tile, e);
            tile.GestureRecognizers.Add(pan);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnTableTapped(table, tile);
        tile.GestureRecognizers.Add(tap);
        return tile;
    }

    private void OnTableTapped(FloorTableDto table, Border tile)
    {
        _pendingTable = table;
        if (table.VisualState == FloorTableVisualState.Free)
        {
            ShowCoverPicker(table, tile);
            return;
        }

        ShowActionSheet(table);
    }

    private void ShowCoverPicker(FloorTableDto table, Border tile)
    {
        tile.BackgroundColor = Color.FromArgb("#3B82F6");
        tile.Stroke = Color.FromArgb("#1D4ED8");
        _coverTitle.Text = $"Table {table.TableNumber}";
        _customCoverValue = 0;
        _customCoverEntry.Text = string.Empty;
        _customCoverEntry.IsVisible = false;
        _customCoverLabel.IsVisible = true;
        _customCoverLabel.Text = "Other number...";
        _customCoverLabel.TextColor = Color.FromArgb("#9CA3AF");
        _coverOverlay.IsVisible = true;
    }

    private void HideCoverPicker()
    {
        _coverOverlay.IsVisible = false;
        if (_pendingTable != null && _tableViews.TryGetValue(_pendingTable.Id, out var tile))
        {
            var (bg, border, _) = FloorTableVisualStyles.ColorsFor(_pendingTable.VisualState);
            tile.BackgroundColor = Color.FromArgb(bg);
            tile.Stroke = Color.FromArgb(border);
        }
    }

    private void ConfirmCovers(int covers)
    {
        if (_pendingTable is null || covers <= 0)
        {
            return;
        }

        var table = _pendingTable;
        HideCoverPicker();
        TableActionRequested?.Invoke(this, new FloorTableActionRequest(table, FloorTableActionKind.SelectCovers, covers));
        _pendingTable = null;
    }

    private void ShowActionSheet(FloorTableDto table)
    {
        _actionButtons.Children.Clear();
        _actionTitle.Text = $"Table {table.TableNumber}";
        var details = new List<string>();
        if (table.GuestCount > 0)
        {
            details.Add($"{table.GuestCount} guests");
        }

        if (!string.IsNullOrWhiteSpace(table.OrderSummary))
        {
            details.Add(table.OrderSummary!);
        }
        else if (!string.IsNullOrWhiteSpace(table.SessionStatus))
        {
            details.Add(table.SessionStatus!);
        }

        _actionSubtitle.Text = details.Count > 0 ? string.Join(" • ", details) : table.VisualState.ToString();

        if (table.CanOpen && _state.Capabilities.AllowOpen)
        {
            _actionButtons.Children.Add(CreateActionButton("Open Table", ButtonVariant.Primary, () =>
            {
                HideActionSheet();
                TableActionRequested?.Invoke(this, new FloorTableActionRequest(table, FloorTableActionKind.Open));
            }));
        }

        if (table.CanMove && _state.Capabilities.AllowMove)
        {
            _actionButtons.Children.Add(CreateActionButton("Move Table", ButtonVariant.Secondary, () =>
            {
                HideActionSheet();
                ShowTargetPicker(table, FloorTableActionKind.Move);
            }));
        }

        if (table.CanMerge && _state.Capabilities.AllowMerge)
        {
            _actionButtons.Children.Add(CreateActionButton("Merge Table", ButtonVariant.Secondary, () =>
            {
                HideActionSheet();
                ShowTargetPicker(table, FloorTableActionKind.Merge);
            }));
        }

        _actionOverlay.IsVisible = true;
    }

    private void HideActionSheet() => _actionOverlay.IsVisible = false;

    private void ShowTargetPicker(FloorTableDto source, FloorTableActionKind action)
    {
        _pendingTable = source;
        _targetPickerList.Children.Clear();
        _targetPickerTitle.Text = action == FloorTableActionKind.Move
            ? $"Move Table {source.TableNumber} to..."
            : $"Merge Table {source.TableNumber} with...";

        var candidates = _state.Tables
            .Where(t => t.Id != source.Id)
            .Where(t => action != FloorTableActionKind.Merge || t.VisualState == FloorTableVisualState.Occupied)
            .OrderBy(t => t.TableNumber)
            .ToList();

        if (candidates.Count == 0)
        {
            _targetPickerList.Children.Add(new Label
            {
                Text = action == FloorTableActionKind.Merge
                    ? "No other occupied tables available to merge."
                    : "No other tables available on this floor.",
                FontSize = 14,
                TextColor = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center
            });
        }
        else
        {
            foreach (var candidate in candidates)
            {
                var target = candidate;
                var button = new SharedButton
                {
                    Text = $"Table {target.TableNumber}" +
                           (target.GuestCount > 0 ? $" ({target.GuestCount} guests)" : string.Empty),
                    Variant = ButtonVariant.Secondary,
                    HeightRequest = 48
                };
                button.Clicked += (_, _) =>
                {
                    HideTargetPicker();
                    TableActionRequested?.Invoke(
                        this,
                        new FloorTableActionRequest(source, action, TargetTableId: target.Id));
                    _pendingTable = null;
                };
                _targetPickerList.Children.Add(button);
            }
        }

        _targetPickerOverlay.IsVisible = true;
    }

    private void HideTargetPicker() => _targetPickerOverlay.IsVisible = false;

    private void OnTableDrag(FloorTableDto table, Border tile, PanUpdatedEventArgs e)
    {
        var (width, height) = FloorTableVisualStyles.SizeFor(table.Shape);
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                var bounds = AbsoluteLayout.GetLayoutBounds(tile);
                _dragOriginX = bounds.X;
                _dragOriginY = bounds.Y;
                _dragStartX = bounds.X;
                _dragStartY = bounds.Y;
                tile.Scale = 1.05;
                tile.Opacity = 0.85;
                break;
            case GestureStatus.Running:
                var canvasWidth = _canvas.Width > 0 ? _canvas.Width : 1200;
                var canvasHeight = _canvas.Height > 0 ? _canvas.Height : 800;
                var newX = Math.Max(0, Math.Min(_dragOriginX + e.TotalX, canvasWidth - width));
                var newY = Math.Max(0, Math.Min(_dragOriginY + e.TotalY, canvasHeight - height));
                AbsoluteLayout.SetLayoutBounds(tile, new Rect(newX, newY, width, height));
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                tile.Scale = 1.0;
                tile.Opacity = 1.0;
                var final = AbsoluteLayout.GetLayoutBounds(tile);
                var snappedX = (int)(Math.Round(final.X / FloorTableVisualStyles.SnapGrid) * FloorTableVisualStyles.SnapGrid);
                var snappedY = (int)(Math.Round(final.Y / FloorTableVisualStyles.SnapGrid) * FloorTableVisualStyles.SnapGrid);
                AbsoluteLayout.SetLayoutBounds(tile, new Rect(snappedX, snappedY, width, height));
                if (Math.Abs(_dragStartX - snappedX) > 1 || Math.Abs(_dragStartY - snappedY) > 1)
                {
                    SetStatusBanner("You have unsaved changes", "warning");
                    TableMoved?.Invoke(this, new FloorTableMovedEvent(table.Id, snappedX, snappedY));
                }

                break;
        }
    }

    private void RenderStatusBanner()
    {
        if (!string.IsNullOrWhiteSpace(_state.StatusBanner))
        {
            SetStatusBanner(_state.StatusBanner, _state.StatusBannerTone);
            return;
        }

        if (_state.Capabilities.ShowStaleWarning && (_state.SyncStatus.IsStale || !_state.SyncStatus.IsOnline))
        {
            SetStatusBanner(
                "Cached floor data may be stale while offline. Changes sync when Mother is reachable.",
                "warning");
            return;
        }

        _statusBanner.IsVisible = false;
    }

    private void ApplyBannerTone(string tone)
    {
        switch (tone)
        {
            case "success":
                _statusBanner.BackgroundColor = Color.FromArgb("#DCFCE7");
                _statusBannerLabel.TextColor = Color.FromArgb("#166534");
                break;
            case "error":
                _statusBanner.BackgroundColor = Color.FromArgb("#FEE2E2");
                _statusBannerLabel.TextColor = Color.FromArgb("#B91C1C");
                break;
            case "info":
                _statusBanner.BackgroundColor = Color.FromArgb("#DBEAFE");
                _statusBannerLabel.TextColor = Color.FromArgb("#1D4ED8");
                break;
            default:
                _statusBanner.BackgroundColor = Color.FromArgb("#FEF3C7");
                _statusBannerLabel.TextColor = Color.FromArgb("#92400E");
                break;
        }
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < 900 || height < 700;
        if (_coverOverlay.Children.OfType<Border>().FirstOrDefault() is { } coverCard)
        {
            coverCard.WidthRequest = compact ? Math.Min(340, width - 32) : 380;
            coverCard.Margin = new Thickness(compact ? 12 : 24);
        }

        if (_actionOverlay.Children.OfType<Border>().FirstOrDefault() is { } actionCard)
        {
            actionCard.WidthRequest = compact ? Math.Min(320, width - 32) : 360;
            actionCard.Margin = new Thickness(compact ? 12 : 24);
        }

        if (_targetPickerOverlay.Children.OfType<Border>().FirstOrDefault() is { } targetCard)
        {
            targetCard.WidthRequest = compact ? Math.Min(340, width - 32) : 380;
            targetCard.MaximumHeightRequest = compact ? Math.Max(280, height - 80) : 520;
            targetCard.Margin = new Thickness(compact ? 12 : 24);
        }
    }

    private static Border CreateToolChip(
        string text,
        string background,
        string stroke,
        string textColor,
        Action onTap,
        bool filled = false)
    {
        var chip = new Border
        {
            BackgroundColor = Color.FromArgb(background),
            Stroke = stroke == "Transparent" ? Colors.Transparent : Color.FromArgb(stroke),
            StrokeThickness = filled ? 0 : 1,
            Padding = new Thickness(12, 8),
            MinimumHeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(textColor),
                VerticalOptions = LayoutOptions.Center
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        chip.GestureRecognizers.Add(tap);
        return chip;
    }

    private static Border CreateIconButton(string text, Color background, Color foreground, Action onTap)
    {
        var button = new Border
        {
            BackgroundColor = background,
            StrokeThickness = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new Label
            {
                Text = text,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = foreground,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        button.GestureRecognizers.Add(tap);
        return button;
    }

    private static SharedButton CreateActionButton(string text, ButtonVariant variant, Action onClick)
    {
        var button = new SharedButton { Text = text, Variant = variant, HeightRequest = 52 };
        button.Clicked += (_, _) => onClick();
        return button;
    }

    private static FloorPlanDto CreateEmptyState() =>
        new(
            null,
            null,
            Array.Empty<FloorTabDto>(),
            Array.Empty<FloorTableDto>(),
            new FloorSyncStatusDto(true, false, null, "Not synced yet"),
            FloorPlanCapabilities.MotherStaff);
}
