using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.System;
using Windows.UI;

namespace OmniConvert.Controls;

public sealed partial class CustomComboBox : UserControl
{
    private static readonly TimeSpan DropDownAnimationDuration = TimeSpan.FromMilliseconds(200);

    private readonly SolidColorBrush _bodyBrush = new(Color.FromArgb(0, 255, 255, 255));
    private readonly List<object> _itemMap = new();
    private readonly Storyboard _openStoryboard = new();
    private readonly Storyboard _closeStoryboard = new();
    private bool _isPointerOver;
    private bool _isPopupOpen;
    private bool _closeAnimationRunning;
    private bool _suppressSelectionSync;

    public CustomComboBox()
    {
        InitializeComponent();
        Body.Background = _bodyBrush;
        SetupPopupAnimations();
        ItemList.Loaded += (_, _) => SyncSelectedItemFromProperty();
        Loaded += (_, _) => HookOutsideClickClose();
        DropDownPopup.Closed += (_, _) =>
        {
            _isPopupOpen = false;
            _closeAnimationRunning = false;
            ChevronTransform.Angle = 0;
            PopupBorder.Opacity = 1;
            PopupBorderTransform.Y = 0;
            _openStoryboard.Stop();
            _closeStoryboard.Stop();
            UpdateBodyColor();
        };
        UpdateBodyColor();
    }

    private void SetupPopupAnimations()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var openOpacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = DropDownAnimationDuration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(openOpacity, PopupBorder);
        Storyboard.SetTargetProperty(openOpacity, "Opacity");
        _openStoryboard.Children.Add(openOpacity);

        var openY = new DoubleAnimation
        {
            From = -8,
            To = 0,
            Duration = DropDownAnimationDuration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(openY, PopupBorderTransform);
        Storyboard.SetTargetProperty(openY, "Y");
        _openStoryboard.Children.Add(openY);

        var closeOpacity = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = DropDownAnimationDuration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(closeOpacity, PopupBorder);
        Storyboard.SetTargetProperty(closeOpacity, "Opacity");
        _closeStoryboard.Children.Add(closeOpacity);

        var closeY = new DoubleAnimation
        {
            From = 0,
            To = -8,
            Duration = DropDownAnimationDuration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(closeY, PopupBorderTransform);
        Storyboard.SetTargetProperty(closeY, "Y");
        _closeStoryboard.Children.Add(closeY);

        _closeStoryboard.Completed += (_, _) =>
        {
            _closeAnimationRunning = false;
            DropDownPopup.IsOpen = false;
        };
    }

    private void HookOutsideClickClose()
    {
        DependencyObject? current = this;
        while (VisualTreeHelper.GetParent(current) is DependencyObject parent)
        {
            current = parent;
        }
        if (current is UIElement root)
        {
            root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);
            root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), true);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isPopupOpen) return;
        switch (e.Key)
        {
            case VirtualKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                ApplyCurrentSelection();
                ClosePopup();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_itemMap.Count == 0) return;
        var index = ItemList.SelectedIndex;
        if (index < 0)
        {
            index = delta > 0 ? 0 : _itemMap.Count - 1;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, _itemMap.Count - 1);
        }
        ItemList.SelectedIndex = index;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if ((_isPopupOpen || _closeAnimationRunning) && !IsWithinControl(e.OriginalSource as DependencyObject))
        {
            ClosePopup();
        }
    }

    private bool IsWithinControl(DependencyObject? source)
    {
        while (source != null)
        {
            if (source == this || source == Body) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void BeginCloseAnimation()
    {
        if (_closeAnimationRunning) return;
        _closeAnimationRunning = true;
        _isPopupOpen = false;
        _openStoryboard.Stop();
        _closeStoryboard.Begin();
        ChevronTransform.Angle = 0;
        UpdateBodyColor();
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(CustomComboBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(CustomComboBox),
            new PropertyMetadata(null, OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(CustomComboBox),
            new PropertyMetadata(null, OnDisplayMemberPathChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomComboBox c) c.ApplyItemsSource();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomComboBox c) c.SyncSelectedItemFromProperty();
    }

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CustomComboBox c) c.ApplyDisplayMemberPath();
    }

    private void ApplyItemsSource()
    {
        _itemMap.Clear();
        if (ItemsSource != null)
        {
            foreach (var item in ItemsSource)
            {
                _itemMap.Add(item);
            }
        }

        _suppressSelectionSync = true;
        ItemList.ItemsSource = _itemMap.Select(GetDisplayText).ToList();
        _suppressSelectionSync = false;
        SyncSelectedItemFromProperty();
    }

    private void ApplyDisplayMemberPath()
    {
        ApplyItemsSource();
        UpdateDisplayText();
    }

    private void SyncSelectedItemFromProperty()
    {
        UpdateDisplayText();
        if (_suppressSelectionSync) return;
        _suppressSelectionSync = true;
        ItemList.SelectedIndex = SelectedItem is null ? -1 : _itemMap.IndexOf(SelectedItem);
        _suppressSelectionSync = false;
    }

    private void UpdateDisplayText()
    {
        DisplayText.Text = GetDisplayText(SelectedItem);
    }

    private string GetDisplayText(object? item)
    {
        if (item == null) return string.Empty;
        if (!string.IsNullOrEmpty(DisplayMemberPath))
        {
            var prop = item.GetType().GetProperty(DisplayMemberPath, BindingFlags.Public | BindingFlags.Instance);
            var value = prop?.GetValue(item);
            if (value != null) return value.ToString() ?? string.Empty;
        }
        return item.ToString() ?? string.Empty;
    }

    private void ItemList_ItemClick(object sender, ItemClickEventArgs e)
    {
        ApplySelectionByItem(e.ClickedItem);
        ClosePopup();
    }

    private void ApplySelectionByItem(object clickedItem)
    {
        var index = ItemList.Items.IndexOf(clickedItem);
        if (index < 0 || index >= _itemMap.Count) return;
        _suppressSelectionSync = true;
        SelectedItem = _itemMap[index];
        _suppressSelectionSync = false;
    }

    private void ApplyCurrentSelection()
    {
        var index = ItemList.SelectedIndex;
        if (index < 0 || index >= _itemMap.Count) return;
        _suppressSelectionSync = true;
        SelectedItem = _itemMap[index];
        _suppressSelectionSync = false;
    }

    private void Body_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        UpdateBodyColor();
    }

    private void Body_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        UpdateBodyColor();
    }

    private void Body_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Body.CapturePointer(e.Pointer);
        if (_isPopupOpen)
        {
            ClosePopup();
        }
        else if (!_closeAnimationRunning)
        {
            OpenPopup();
        }
    }

    private void Body_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Body.ReleasePointerCapture(e.Pointer);
        var pos = e.GetCurrentPoint(Body).Position;
        var isOver = pos.X >= 0 && pos.Y >= 0 && pos.X <= Body.ActualWidth && pos.Y <= Body.ActualHeight;
        if (!isOver)
        {
            UpdateBodyColor();
        }
    }

    private void OpenPopup()
    {
        _isPopupOpen = true;
        _closeAnimationRunning = false;
        Focus(FocusState.Programmatic);
        DropDownPopup.VerticalOffset = Body.ActualHeight + 4;
        DropDownPopup.HorizontalOffset = 0;
        PopupBorder.Opacity = 0;
        PopupBorderTransform.Y = -8;
        DropDownPopup.IsOpen = true;
        _closeStoryboard.Stop();
        _openStoryboard.Begin();
        SetBodyColor(0xFF, 0xEA, 0xEA, 0xEA);
        ChevronTransform.Angle = 180;
    }

    private void ClosePopup()
    {
        BeginCloseAnimation();
    }

    private void SetBodyColor(byte a, byte r, byte g, byte b)
    {
        _bodyBrush.Color = Color.FromArgb(a, r, g, b);
    }

    private void UpdateBodyColor()
    {
        if (_isPopupOpen)
        {
            SetBodyColor(0xFF, 0xEA, 0xEA, 0xEA);
        }
        else if (_isPointerOver)
        {
            SetBodyColor(0xFF, 0xF5, 0xF5, 0xF5);
        }
        else
        {
            SetBodyColor(0x00, 0xFF, 0xFF, 0xFF);
        }
    }
}
