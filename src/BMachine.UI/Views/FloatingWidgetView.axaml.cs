using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media; 
using Avalonia.Layout;
using System;
using System.ComponentModel;
using System.Linq;
using BMachine.UI.ViewModels;
using Avalonia.Data;
using Avalonia.Animation;
using Avalonia.Styling;

namespace BMachine.UI.Views;

public partial class FloatingWidgetView : Window
{
    // Manual Drag Fields
    // Manual Drag Fields
    private bool _isDragging = false;
    private PixelPoint _startScreenPoint;
    private PixelPoint _startWindowPosition;
    
    // Orb Drag Fields (Left Click)
    private bool _isOrbDragging = false;
    private bool _isOrbPossibleClick = false;
    private Point _orbDragStartPoint;
    
    // Resize Fields
    private bool _isResizing = false;
    private Point _resizeStartPoint;
    private Size _startWindowSize;
    private Size _lastExpandedSize = new Size(440, 210); // Default Expanded Size

    // UI Elements
    private Panel _orbRootPanel;
    private Border _glowBorder; // Back Layer (Animated)
    private Border _orbBorder; // Front Layer (Static)
    private Border _menuContainer;
    private Panel _menuContent; 

    public FloatingWidgetView()
    {
        this.Bind(IsVisibleProperty, new Binding("IsVisible"));
        this.Opened += OnOpened;
        this.DataContextChanged += OnDataContextChanged;
        
        // Monitor Visibility Changes using PropertyChanged event (Safe ref)
        this.PropertyChanged += (s, e) => 
        {
            if (e.Property == IsVisibleProperty && (bool)e.NewValue == true)
            {
                 var vm = DataContext as FloatingWidgetViewModel;
                 if (vm != null)
                 {
                     Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => 
                     {
                         var savedPos = await vm.GetSavedPosition();
                         if (savedPos != null)
                         {
                             this.Position = new PixelPoint(savedPos.Value.X, savedPos.Value.Y);
                         }
                     });
                 }
            }
        };
        
        // Window Properties
        this.SystemDecorations = SystemDecorations.None;
        this.Background = Brushes.Transparent;
        this.TransparencyLevelHint = new System.Collections.Generic.List<WindowTransparencyLevel> { WindowTransparencyLevel.Transparent };
        this.Topmost = true;
        this.ShowInTaskbar = false;
        this.CanResize = false;
        this.Width = 100; // Larger to accommodate glow spread
        this.Height = 100;
        this.WindowStartupLocation = WindowStartupLocation.Manual;
        
        // Fix for Windows 11 Square Border
        this.ExtendClientAreaToDecorationsHint = true;
        this.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        this.ExtendClientAreaTitleBarHeightHint = 0;

        // --- Root Grid ---
        var rootGrid = new Grid();
        
        // --- 1. The Orb Group ---
        _orbRootPanel = new Panel
        {
            Width = 60, Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left, 
            VerticalAlignment = VerticalAlignment.Top,    
            Margin = new Thickness(20), // Centered with more padding for glow
            Background = Brushes.Transparent
        };
        
        // A. Glow Layer (Back)
        _glowBorder = new Border
        {
            Width = 60, Height = 60,
            CornerRadius = new CornerRadius(30),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 0, Blur = 20, Spread = 2, Color = Color.Parse("#003b82f6") // Initial transparent, updated by VM
            }),
            Opacity = 1.0,
            IsHitTestVisible = false // Pass clicks through
        };
        
        // B. Main Orb Layer (Front)
        _orbBorder = new Border
        {
            Width = 60, Height = 60,
            CornerRadius = new CornerRadius(30),
            // Background is bound below
            BorderBrush = SolidColorBrush.Parse("#3b82f6"), // Updated by VM
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand), 
            IsHitTestVisible = true
        };
        // Dynamic Binding for Background using GetResourceObservable
        _orbBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBackgroundBrush"));
        
        _orbRootPanel.Children.Add(_glowBorder);
        _orbRootPanel.Children.Add(_orbBorder);
        
        // Will be started in OnOpened now with correct speed
        // StartBreathingAnimation();

        // --- 2. Menu Container ---
        _menuContainer = new Border
        {
            Margin = new Thickness(0, 100, 0, 0), // Position below Orb (adjusted for new size)
            // Background bound dynamically below
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            IsVisible = false 
        };
        _menuContainer.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBackgroundBrush"));
        _menuContainer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SeparatorBrush"));
        
        // Right-Click to Collapse Logic
        _menuContainer.PointerPressed += (s, e) => 
        {
             if (e.GetCurrentPoint(_menuContainer).Properties.IsRightButtonPressed)
             {
                 if (DataContext is FloatingWidgetViewModel vm)
                 {
                     vm.GoHomeCommand.Execute(null);
                 }
                 e.Handled = true;
             }
        };

        _menuContent = new Grid();
        
        // --- 3. Resize Handle ---
        var resizeHandle = new Grid
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Background = Brushes.Transparent // Hit test visible
        };
        // Visual indicator (triangle corner)
        var resizePath = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M16,16 L16,6 L6,16 Z"), 
            Fill = SolidColorBrush.Parse("#40FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2)
        };
        resizeHandle.Children.Add(resizePath);
        
        resizeHandle.PointerPressed += OnResizeHandlePointerPressed;
        resizeHandle.PointerMoved += OnResizeHandlePointerMoved;
        resizeHandle.PointerReleased += OnResizeHandlePointerReleased;

        // Overlay Grid to hold Menu Content + Resize Handle
        var overlaysGrid = new Grid();
        overlaysGrid.Children.Add(_menuContent);
        overlaysGrid.Children.Add(resizeHandle);

        _menuContainer.Child = overlaysGrid;

        // Assemble Root
        rootGrid.Children.Add(_orbRootPanel);
        rootGrid.Children.Add(_menuContainer);

        this.Content = rootGrid;
        
        // Attach Drag Events to _orbRootPanel (larger hit area) or _orbBorder
        // The logic below (not shown in this replacement block) attaches to internal events, 
        // need to ensure _orbBorder event handlers are attached in OnOpened or setup here.
    }
    
    private System.Threading.CancellationTokenSource? _animationCts;
    
    private void StartBreathingAnimation(TimeSpan duration, bool enabled)
    {
        _animationCts?.Cancel();
        
        if (!enabled)
        {
            _glowBorder.Opacity = 0;
            return;
        }
        
        _animationCts = new System.Threading.CancellationTokenSource();
        var token = _animationCts.Token;
        
        var animation = new Avalonia.Animation.Animation
        {
            Duration = duration,
            IterationCount = Avalonia.Animation.IterationCount.Infinite,
            PlaybackDirection = Avalonia.Animation.PlaybackDirection.Alternate,
            Children =
            {
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(0.0),
                    Setters = { new Setter(Visual.OpacityProperty, 0.4) }
                },
                new Avalonia.Animation.KeyFrame
                {
                    Cue = new Avalonia.Animation.Cue(1.0),
                    Setters = { new Setter(Visual.OpacityProperty, 1.0) } 
                }
            }
        };
        
        // RunAsync with token to allow cancellation
        animation.RunAsync(_glowBorder, token);
    }
    
    private void UpdateSpeed(FloatingWidgetViewModel vm)
    {
        TimeSpan duration = vm.OrbSpeedIndex switch
        {
            0 => TimeSpan.FromSeconds(4.0), // Slow
            2 => TimeSpan.FromSeconds(1.0), // Fast
            _ => TimeSpan.FromSeconds(2.5)  // Normal
        };
        StartBreathingAnimation(duration, vm.IsOrbBreathing);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is FloatingWidgetViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FloatingWidgetViewModel vm) return;

        if (e.PropertyName == nameof(FloatingWidgetViewModel.IsExpanded))
        {
            UpdateExpandedState(vm);
        }
        else if (e.PropertyName == nameof(FloatingWidgetViewModel.ShowMasterMenu) ||
                 e.PropertyName == nameof(FloatingWidgetViewModel.ShowActionMenu) ||
                 e.PropertyName == nameof(FloatingWidgetViewModel.ShowOthersMenu))
        {
            RebuildMenu(vm);
        }
        else if (e.PropertyName == nameof(FloatingWidgetViewModel.AccentColor))
        {
            UpdateColors(vm);
        }
        else if (e.PropertyName == nameof(FloatingWidgetViewModel.OrbSpeedIndex))
        {
            UpdateSpeed(vm);
        }
        else if (e.PropertyName == nameof(FloatingWidgetViewModel.IsOrbBreathing))
        {
            UpdateSpeed(vm);
        }
        else if (e.PropertyName == nameof(FloatingWidgetViewModel.OrbButtonWidth) ||
                 e.PropertyName == nameof(FloatingWidgetViewModel.OrbButtonHeight))
        {
            RebuildMenu(vm);
        }
    }
    
    private void UpdateColors(FloatingWidgetViewModel vm)
    {
        if (vm.AccentColor is ISolidColorBrush solid)
        {
            _orbBorder.BorderBrush = solid;
            // No shadow on main orb anymore (or very subtle inner?)
            // _orbBorder.BoxShadow = ...
            
            // Update Glow
            _glowBorder.BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 0, Blur = 20, Spread = 2, Color = solid.Color
            });
            
            // If Menu is open, rebuild to update active button colors
            if (vm.IsExpanded)
            {
                RebuildMenu(vm);
            }
        }
    }

    private void UpdateExpandedState(FloatingWidgetViewModel vm)
    {
        try
        {
            if (vm.IsExpanded)
            {
                // Expand Window - Landscape Mode (Compacted Width)
                // Use ViewModel size if available (and > small), otherwise _lastExpandedSize
                double w = vm.OrbExpandedWidth > 100 ? vm.OrbExpandedWidth : _lastExpandedSize.Width;
                double h = vm.OrbExpandedHeight > 100 ? vm.OrbExpandedHeight : _lastExpandedSize.Height;
                
                this.Width = w;
                this.Height = h;
                
                // Sync local tracking
                _lastExpandedSize = new Size(w, h); 
                
                // Switch Views
                _orbBorder.IsVisible = false;
                
                _menuContainer.Margin = new Thickness(0); 
                _menuContainer.IsVisible = true;
                
                RebuildMenu(vm);
            }
            else
            {
                // Collapse Window
                this.Width = 100;
                this.Height = 100;
                
                // Switch Views
                _menuContainer.IsVisible = false;
                _orbBorder.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FloatingWidget] Error updating state: {ex}");
        }
    }

    private void RebuildMenu(FloatingWidgetViewModel vm)
    {
        try
        {
            _menuContent.Children.Clear();
    
            // Use a Grid for Landscape Layout: [Nav] [Sep] [Content]
            var mainGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("64, 1, *"), // 3 Columns: Nav, Separator, Content
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent 
            };

            // 1. Navigation Column (Left) - Distributed Layout
            // 1. Navigation Column (Left) - Distributed Layout
            var navGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("*, *"), // Distribute Top, Bottom (Removed 3rd row)
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            
            // Add Buttons to Grid Rows
            var btn1 = CreateNavButton(vm.ToggleMasterCommand, "IconPython", vm.ShowMasterMenu, vm);
            Grid.SetRow(btn1, 0);
            navGrid.Children.Add(btn1);

            var btn2 = CreateNavButton(vm.ToggleActionCommand, "IconBolt", vm.ShowActionMenu, vm);
            Grid.SetRow(btn2, 1);
            navGrid.Children.Add(btn2);
            
            // Removed btn3 (Others/GDrive)
            
            Grid.SetColumn(navGrid, 0);
            mainGrid.Children.Add(navGrid);

            // 2. Separator (Middle Column) - Full Height
            // Explicitly set VerticalAlignment to Stretch to fill the column
            var sep = new Border 
            { 
                Background = SolidColorBrush.Parse("#30FFFFFF"), // Slightly brighter line
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(sep, 1);
            mainGrid.Children.Add(sep);

            // 3. Content Area (Right)
            var contentArea = new Panel { Margin = new Thickness(10) };
            Grid.SetColumn(contentArea, 2);
            
            if (vm.ShowMasterMenu) contentArea.Children.Add(CreateScrollList(vm.MasterItems, vm));
            else if (vm.ShowActionMenu) contentArea.Children.Add(CreateScrollList(vm.ActionItems, vm));
            else if (vm.ShowOthersMenu) contentArea.Children.Add(CreateScrollList(vm.OtherItems, vm));
            else 
            {
                 var txt = new TextBlock { Text = "Select Category", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                 contentArea.Children.Add(txt);
            }
            
            mainGrid.Children.Add(contentArea);
            _menuContent.Children.Add(mainGrid);
        }
        catch (Exception ex)
        {
             Console.WriteLine($"[FloatingWidget] Error rebuilding menu: {ex}");
             _menuContent.Children.Add(new TextBlock { Text = "Menu Error", Foreground = Brushes.Red });
        }
    }

    private Control CreateScrollList(System.Collections.Generic.IEnumerable<FloatingItem> items, FloatingWidgetViewModel vm)
    {
        try {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            
            var listPanel = new WrapPanel 
            { 
                Orientation = Orientation.Horizontal,
                ItemWidth = vm.OrbButtonWidth + 6, // Margin
                ItemHeight = vm.OrbButtonHeight + 6 
            };
            DragDrop.SetAllowDrop(listPanel, true);
            
            // Container Drop Handler
            listPanel.AddHandler(DragDrop.DropEvent, (s, e) => 
            {
            });
    
            // Get accent color (full color, not transparent)
            var accentBrush = vm.AccentColor ?? SolidColorBrush.Parse("#3b82f6");
    
            foreach (var item in items)
            {
                // Use a Border instead of Button for full input control
                var btnContainer = new Border
                {
                    Width = vm.OrbButtonWidth, 
                    Height = vm.OrbButtonHeight,
                    Background = accentBrush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 0), 
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Margin = new Thickness(2),
                    DataContext = item,
                    Focusable = true, // Allow focus
                    [ToolTip.TipProperty] = item.Name // Tooltip
                };
                
                // Enable Drag & Drop
                DragDrop.SetAllowDrop(btnContainer, true);
                
                // Handlers
                btnContainer.PointerPressed += OnItemPointerPressed;
                btnContainer.PointerMoved += OnItemPointerMoved;
                btnContainer.PointerReleased += OnItemPointerReleased;
                btnContainer.AddHandler(DragDrop.DragOverEvent, OnItemDragOver);
                btnContainer.AddHandler(DragDrop.DropEvent, OnItemDrop);

                var contentStack = new StackPanel 
                { 
                    Orientation = Orientation.Horizontal, 
                    Spacing = 0, // No spacing needed if only text
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Center, // Center Content
                    VerticalAlignment = VerticalAlignment.Center
                }; 
                
                // Determine Text Color based on Accent Brightness
                var textColor = Brushes.White;
                if (accentBrush is SolidColorBrush solid)
                {
                    // Calculate relative luminance or brightness
                    // Perceptive Luminance = 0.299*R + 0.587*G + 0.114*B
                    var color = solid.Color;
                    double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                    if (brightness > 160) // Threshold for "Light" background
                    {
                        textColor = Brushes.Black; // Use Black text for Light backgrounds
                    }
                }

                var txt = new TextBlock 
                { 
                    Text = item.Name, 
                    VerticalAlignment = VerticalAlignment.Center, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = vm.OrbButtonWidth - 20, // Wider since no icon
                    TextAlignment = TextAlignment.Center,
                    Foreground = textColor
                };
                
                contentStack.Children.Add(txt);
    
                btnContainer.Child = contentStack;
                listPanel.Children.Add(btnContainer);
            }
    
            scroll.Content = listPanel;
            return scroll;
        } catch { return new TextBlock { Text = "List Error" }; }
    }
    
    // Drag State
    private Point _dragStartPoint;
    private bool _isPossibleClick;
    private static FloatingItem? _draggedItem; // Static reference for in-process drag

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not FloatingItem item) return;
        
        var point = e.GetCurrentPoint(control);
        if (point.Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(null);
            _isPossibleClick = true;
            e.Handled = true; 
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
         if (_isPossibleClick)
         {
             if (sender is Control control && control.DataContext is FloatingItem item)
             {
                 item.ActionCommand?.Execute(null);
             }
             _isPossibleClick = false;
         }
         e.Handled = true;
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPossibleClick) return; 
        if (sender is not Control control || control.DataContext is not FloatingItem item) return;
        
        var currentPoint = e.GetPosition(null);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) > 10 || 
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 10)
        {
             // Start Drag
             _isPossibleClick = false;
             _draggedItem = item; // Store reference
             
             var data = new DataObject();
             data.Set(DataFormats.Text, item.Name); // Just dummy data to make DragDrop happy
             
             Console.WriteLine($"[Drag] Starting drag for {item.Name}");
             
             try 
             {
                 var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"[Drag] Error: {ex.Message}");
             }
             finally
             {
                 _draggedItem = null; // Clear after drag
             }
        }
    }

    private void OnItemDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnItemDrop(object? sender, DragEventArgs e)
    {
        if (_draggedItem == null) return;
        
        e.Handled = true;
        var vm = DataContext as FloatingWidgetViewModel;
        if (vm == null) return;

        if (sender is Control targetControl && targetControl.DataContext is FloatingItem targetItem)
        {
            if (_draggedItem == targetItem) return;
            Console.WriteLine($"[Drop] Dropping {_draggedItem.Name} on {targetItem.Name}");
            vm.ReorderItem(_draggedItem, targetItem, false);
            
            // Immediately refresh the menu to show new order
            RebuildMenu(vm);
        }
        else if (sender is Panel) 
        {
             // Dropped on container -> Move to end ??
             // Actually, we don't know "end" easily without reference to last item.
             // But if we just want to ensure it works, we can ignore or try to find collection.
        }
    }

    private Button CreateNavButton(System.Windows.Input.ICommand? command, string? iconKey, bool isActive, FloatingWidgetViewModel vm)
    {
        var btn = new Button
        {
            Width = 42, Height = 42, 
            CornerRadius = new CornerRadius(21), 
            Padding = new Thickness(0), 
            Command = command,
            Background = isActive ? (vm.AccentColor ?? SolidColorBrush.Parse("#3b82f6")) : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center, 
            VerticalContentAlignment = VerticalAlignment.Center,
            
            // Fix: Center the button itself within the Grid Cell
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        btn.Classes.Add("SidebarIcon"); // Apply the hover fix style

        // Right Click to Go Home behavior
        btn.PointerPressed += (s, e) => 
        {
            if (e.GetCurrentPoint(btn).Properties.IsRightButtonPressed)
            {
                vm.GoHomeCommand.Execute(null);
                e.Handled = true;
            }
        };

        var viewbox = new Avalonia.Controls.Viewbox
        {
            Width = 20, Height = 20,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = iconKey == "IconExtension" ? new Thickness(0, 2, 0, 0) : new Thickness(0) // Fix: Push Extension icon down slightly
        };

        var path = new Avalonia.Controls.Shapes.Path
        {
            Stretch = Stretch.Uniform 
        };
        
        // Bind Fill to TextPrimaryBrush or Accent
        if (isActive)
        {
             path.Fill = Brushes.White; // Active items usually white on Accent BG
        }
        else
        {
             path.Bind(Avalonia.Controls.Shapes.Path.FillProperty, this.GetResourceObservable("TextPrimaryBrush"));
        }

        if (!string.IsNullOrEmpty(iconKey) && Application.Current?.TryFindResource(iconKey, out var res) == true && res is StreamGeometry geom)
        {
            path.Data = geom;
        }

        viewbox.Child = path;
        btn.Content = viewbox; 
        return btn;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is FloatingWidgetViewModel vm)
        {
            var savedPos = await vm.GetSavedPosition();
            if (savedPos != null)
            {
                this.Position = new PixelPoint(savedPos.Value.X, savedPos.Value.Y);
            }
            else
            {
                this.Position = new PixelPoint(100, 100); // Default
            }
            
            // Sync Color
            UpdateColors(vm);
            // Sync Speed
            UpdateSpeed(vm);
        }
        else
        {
             this.Position = new PixelPoint(100, 100);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        
        // Only allow drag if clicking the ORB (Top Left area)
        var pos = e.GetPosition(this);
        bool clickableArea = pos.Y <= 70; // Approximate Orb Area

        if (clickableArea)
        {
            if (props.IsLeftButtonPressed)
            {
                // Init Left-Click Drag Logic
                _isOrbDragging = false;
                _isOrbPossibleClick = true;
                _orbDragStartPoint = e.GetPosition(null);
                _startWindowPosition = this.Position;
                _startScreenPoint = this.PointToScreen(e.GetPosition(this)); // Needed for calc
                e.Handled = true;
            }
        }
        // If clicking Menu (Y > 60), let standard Bubbling handle it (Button Clicks)
        
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // Orb Drag Check (Threshold)
        if (_isOrbPossibleClick)
        {
            var currentPoint = e.GetPosition(null);
            if (Math.Abs(currentPoint.X - _orbDragStartPoint.X) > 5 || 
                Math.Abs(currentPoint.Y - _orbDragStartPoint.Y) > 5)
            {
                 _isOrbDragging = true;
                 _isOrbPossibleClick = false; // It's a drag now
            }
        }

        if (_isOrbDragging)
        {
            var currentScreenPoint = this.PointToScreen(e.GetPosition(this));
            var deltaX = currentScreenPoint.X - _startScreenPoint.X;
            var deltaY = currentScreenPoint.Y - _startScreenPoint.Y;

            this.Position = new PixelPoint(
                 _startWindowPosition.X + deltaX, 
                 _startWindowPosition.Y + deltaY
            );
        }
        
        // Old Manual Drag (Keep if used elsewhere, but mainly Orb Logic above)
        if (_isDragging)
        {
             // ... existing manual drag logic if needed ...
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isOrbDragging)
        {
            _isOrbDragging = false;
            
            // Save Position on Drag End
            if (DataContext is FloatingWidgetViewModel vm)
            {
                _ = vm.SavePosition(this.Position.X, this.Position.Y);
            }
            
            e.Handled = true;
        }
        else if (_isOrbPossibleClick)
        {
            // Valid Click!
            _isOrbPossibleClick = false;
            if (DataContext is FloatingWidgetViewModel vm)
            {
                vm.ToggleExpandCommand.Execute(null);
            }
            e.Handled = true;
        }
        
        if (_isDragging)
        {
            _isDragging = false;
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    // --- Resize Logic ---
    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(this);
            _startWindowSize = new Size(this.Width, this.Height);
            e.Pointer.Capture(sender as Control);
            e.Handled = true;
        }
    }

    private void OnResizeHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;
        
        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _resizeStartPoint.X;
        var deltaY = currentPoint.Y - _resizeStartPoint.Y;
        
        var newWidth = Math.Max(300, _startWindowSize.Width + deltaX); // Min Width 300
        var newHeight = Math.Max(150, _startWindowSize.Height + deltaY); // Min Height 150
        
        this.Width = newWidth;
        this.Height = newHeight;
        
        // Update last known size
        _lastExpandedSize = new Size(newWidth, newHeight);
    }

    private void OnResizeHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
             _isResizing = false;
             e.Pointer.Capture(null);
             e.Handled = true;
             
             // Save new size to VM (Persistence)
             if (DataContext is FloatingWidgetViewModel vm)
             {
                 _ = vm.SaveExpandedSize(this.Width, this.Height);
             }
        }
    }
}
