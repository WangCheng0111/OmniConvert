using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace OmniConvert
{
    public sealed partial class MainWindow : Window
    {
        private const string StateNormal = "Normal";
        private const string StatePointerOver = "PointerOver";
        private const string StatePressed = "Pressed";

        private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(200);

        private static readonly Color IconBaseColor = Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D);
        private static readonly Color IconDimmedColor = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0);
        private static readonly Color IconWhiteColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        private static readonly Color TransparentColor = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF);
        private static readonly Color HoverBackground = Color.FromArgb(0xFF, 0xD9, 0xDD, 0xE2);
        private static readonly Color PressedBackground = Color.FromArgb(0xFF, 0xC2, 0xC5, 0xCA);
        private static readonly Color CloseHoverBackground = Color.FromArgb(0xFF, 0xE8, 0x11, 0x23);
        private static readonly Color ClosePressedBackground = Color.FromArgb(0xFF, 0xEC, 0x6D, 0x7A);

        private AppWindow _appWindow;
        private InputNonClientPointerSource _nonClientInputSrc = null!;
        private bool _isApplyingRegions;
        private bool _isClosed;
        private bool _isWindowDeactivated;
        private Button? _hoveredButton;
        private Button? _pressedButton;
        private Point _lastPointerPoint;
        private RectInt32 _minimizeRect;
        private RectInt32 _maximizeRect;
        private RectInt32 _closeRect;

        private Color BaseIconColor => _isWindowDeactivated ? IconDimmedColor : IconBaseColor;

        private readonly Button[] _captionButtons = null!;

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        private void InitializeNonClientInput()
        {
            _nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
            _nonClientInputSrc.RegionsChanged += OnRegionsChanged;
            _nonClientInputSrc.ExitedMoveSize += (_, _) => SetRegionsForCustomTitleBar();
            _nonClientInputSrc.PointerEntered += OnNonClientPointerEntered;
            _nonClientInputSrc.PointerMoved += OnNonClientPointerMoved;
            _nonClientInputSrc.PointerPressed += OnNonClientPointerPressed;
            _nonClientInputSrc.PointerReleased += OnNonClientPointerReleased;
            _nonClientInputSrc.PointerExited += OnNonClientPointerExited;
            Closed += (_, _) => _isClosed = true;
        }

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            if (!ExtendsContentIntoTitleBar)
            {
                return;
            }

            SetRegionsForCustomTitleBar();
            UpdateMaximizeButtonIcon();
        }

        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!ExtendsContentIntoTitleBar)
            {
                return;
            }

            SetRegionsForCustomTitleBar();
            UpdateMaximizeButtonIcon();
        }

        private void OnRegionsChanged(InputNonClientPointerSource sender, NonClientRegionsChangedEventArgs args)
        {
            if (_isClosed || _isApplyingRegions)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || !ExtendsContentIntoTitleBar)
                {
                    return;
                }
                SetRegionsForCustomTitleBar();
            });
        }

        private void ApplyRegionRects(NonClientRegionKind kind, RectInt32[] rects)
        {
            var current = _nonClientInputSrc.GetRegionRects(kind);
            if (current != null && current.Length == rects.Length)
            {
                bool same = true;
                for (int i = 0; i < rects.Length; i++)
                {
                    if (current[i].X != rects[i].X || current[i].Y != rects[i].Y ||
                        current[i].Width != rects[i].Width || current[i].Height != rects[i].Height)
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                {
                    return;
                }
            }

            _isApplyingRegions = true;
            try
            {
                _nonClientInputSrc.SetRegionRects(kind, rects);
            }
            finally
            {
                _isApplyingRegions = false;
            }
        }

        private void SetRegionsForCustomTitleBar()
        {
            if (_isClosed || AppTitleBar.XamlRoot == null)
            {
                return;
            }

            double scaleAdjustment = AppTitleBar.XamlRoot.RasterizationScale;

            double rightInset = _appWindow.TitleBar.RightInset / scaleAdjustment;
            double leftInset = _appWindow.TitleBar.LeftInset / scaleAdjustment;
            if (!double.IsFinite(rightInset) || rightInset < 0)
            {
                rightInset = 0;
            }
            if (!double.IsFinite(leftInset) || leftInset < 0)
            {
                leftInset = 0;
            }
            RightPaddingColumn.Width = new GridLength(rightInset);
            LeftPaddingColumn.Width = new GridLength(leftInset);

            var titleBarBounds = AppTitleBar.TransformToVisual(null)
                .TransformBounds(new Rect(0, 0, AppTitleBar.ActualWidth, AppTitleBar.ActualHeight));
            var minimizeLeft = MinimizeButton.TransformToVisual(null).TransformPoint(new Point(0, 0));
            var captionRect = GetRect(
                new Rect(titleBarBounds.X, titleBarBounds.Y,
                         minimizeLeft.X - titleBarBounds.X,
                         titleBarBounds.Height),
                scaleAdjustment);

            _minimizeRect = GetRegionRect(MinimizeButton, scaleAdjustment);
            _maximizeRect = GetRegionRect(MaximizeButton, scaleAdjustment);
            _closeRect = GetRegionRect(CloseButton, scaleAdjustment);

            ApplyRegionRects(NonClientRegionKind.Caption, new[] { captionRect });
            ApplyRegionRects(NonClientRegionKind.Minimize, new[] { _minimizeRect });
            ApplyRegionRects(NonClientRegionKind.Maximize, new[] { _maximizeRect });
            ApplyRegionRects(NonClientRegionKind.Close, new[] { _closeRect });
        }

        private RectInt32 GetRegionRect(FrameworkElement element, double scaleAdjustment)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return GetRect(bounds, scaleAdjustment);
        }

        private RectInt32 GetRect(Rect bounds, double scale)
        {
            return new RectInt32(
                _X: (int)Math.Round(bounds.X * scale),
                _Y: (int)Math.Round(bounds.Y * scale),
                _Width: (int)Math.Round(bounds.Width * scale),
                _Height: (int)Math.Round(bounds.Height * scale)
            );
        }

        private void UpdateMaximizeButtonIcon()
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            MaximizeButtonIcon.Glyph = IsZoomed(hWnd) ? "\uE923" : "\uE922";
        }

        // 按下期间系统会捕获指针，此时 RegionKind 仍报告按下时的区域，
        // 因此一律用 Point + 区域矩形做命中测试（而非 RegionKind）。
        private Button? GetButtonAtPoint(Point point)
        {
            if (ContainsPoint(_minimizeRect, point)) return MinimizeButton;
            if (ContainsPoint(_maximizeRect, point)) return MaximizeButton;
            if (ContainsPoint(_closeRect, point)) return CloseButton;
            return null;
        }

        private static bool ContainsPoint(RectInt32 rect, Point point)
        {
            return point.X >= rect.X && point.X < rect.X + rect.Width &&
                   point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
        }

        private void UpdateButtonHover(Point point)
        {
            var button = GetButtonAtPoint(point);
            if (_hoveredButton == button)
            {
                return;
            }

            if (_hoveredButton != null)
            {
                ApplyButtonVisual(_hoveredButton, StateNormal, AnimationDuration);
            }

            _hoveredButton = button;

            if (_hoveredButton != null)
            {
                ApplyButtonVisual(_hoveredButton, StatePointerOver, AnimationDuration);
            }
        }

        private void OnNonClientPointerEntered(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
        {
            // 进入非客户区时 Point 不可靠，悬停状态交给后续的 PointerMoved 处理
        }

        private void OnNonClientPointerMoved(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
        {
            _lastPointerPoint = args.Point;

            if (_pressedButton == null)
            {
                UpdateButtonHover(args.Point);
                return;
            }

            bool inside = GetButtonAtPoint(args.Point) == _pressedButton;
            ApplyButtonVisual(_pressedButton, inside ? StatePressed : StateNormal, TimeSpan.Zero);
        }

        private void OnNonClientPointerPressed(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
        {
            _lastPointerPoint = args.Point;

            var button = GetButtonAtPoint(args.Point);
            if (button == null)
            {
                return;
            }

            _pressedButton = button;
            ApplyButtonVisual(button, StatePressed, TimeSpan.Zero);
        }

        private void OnNonClientPointerReleased(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
        {
            _pressedButton = null;
            // 松开事件上报的 Point 是按下捕获点而非真实位置，
            // 必须用最近一次 PointerMoved 的位置恢复悬停状态，否则会造成状态闪跳。
            UpdateButtonHover(_lastPointerPoint);
        }

        private void OnNonClientPointerExited(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
        {
            _pressedButton = null;
            if (_hoveredButton == null)
            {
                return;
            }

            ApplyButtonVisual(_hoveredButton, StateNormal, AnimationDuration);
            _hoveredButton = null;
        }

        private void ApplyButtonVisual(Button button, string stateName, TimeSpan duration)
        {
            Color background;
            Color foreground;

            switch (stateName)
            {
                case StatePressed:
                    background = button == CloseButton ? ClosePressedBackground : PressedBackground;
                    foreground = button == CloseButton ? IconWhiteColor : BaseIconColor;
                    break;
                case StatePointerOver:
                    background = button == CloseButton ? CloseHoverBackground : HoverBackground;
                    foreground = button == CloseButton ? IconWhiteColor : BaseIconColor;
                    break;
                default:
                    background = TransparentColor;
                    foreground = BaseIconColor;
                    break;
            }

            AnimateButtonBackground(button, background, duration);
            AnimateButtonForeground(button, foreground, duration);
        }

        private void AnimateButtonBackground(Button button, Color to, TimeSpan duration)
        {
            AnimateColor(button, "(Control.Background).(SolidColorBrush.Color)",
                ((SolidColorBrush)button.Background).Color, to, duration);
        }

        private void AnimateButtonForeground(Button button, Color to, TimeSpan duration)
        {
            AnimateColor(button, "(Control.Foreground).(SolidColorBrush.Color)",
                ((SolidColorBrush)button.Foreground).Color, to, duration);
        }

        private static void AnimateColor(DependencyObject target, string propertyPath, Color from, Color to, TimeSpan duration)
        {
            var storyboard = new Storyboard();
            var animation = new ColorAnimation
            {
                From = from,
                To = to,
                Duration = duration
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, propertyPath);
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            bool deactivated = args.WindowActivationState == WindowActivationState.Deactivated;
            _isWindowDeactivated = deactivated;

            // 推迟到激活流程稳定后再启动动画：
            // 在窗口激活瞬间启动的 Storyboard 可能被系统吞掉或快速推进。
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed || _isWindowDeactivated != deactivated)
                {
                    return;
                }
                AnimateWindowActivation(deactivated);
            });
        }

        private void AnimateWindowActivation(bool isDeactivated)
        {
            var targetBrush = (SolidColorBrush)App.Current.Resources[
                isDeactivated ? "WindowCaptionForegroundDisabled" : "WindowCaptionForeground"];
            var targetOpacity = isDeactivated ? 0.4 : 1.0;
            var targetButtonColor = isDeactivated ? IconDimmedColor : IconBaseColor;

            // 每次使用全新 Storyboard：对同一属性启动新动画会自动替换旧动画，
            // 避免复用实例 Stop() 造成值回跳或时序异常。
            var storyboard = new Storyboard();

            AddAnimationTo(storyboard, TitleBarTextBlock,
                "(TextBlock.Foreground).(SolidColorBrush.Color)",
                ((SolidColorBrush)TitleBarTextBlock.Foreground).Color,
                targetBrush.Color);

            AddAnimationTo(storyboard, TitleBarIcon,
                "Opacity",
                TitleBarIcon.Opacity,
                targetOpacity);

            foreach (var button in _captionButtons)
            {
                AddAnimationTo(storyboard, button,
                    "(Control.Foreground).(SolidColorBrush.Color)",
                    ((SolidColorBrush)button.Foreground).Color,
                    targetButtonColor);
            }

            storyboard.Begin();
        }

        private static void AddAnimationTo(Storyboard storyboard, DependencyObject target, string propertyPath, Color from, Color to)
        {
            var animation = new ColorAnimation
            {
                From = from,
                To = to,
                Duration = AnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, propertyPath);
            storyboard.Children.Add(animation);
        }

        private static void AddAnimationTo(Storyboard storyboard, DependencyObject target, string propertyPath, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = AnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, propertyPath);
            storyboard.Children.Add(animation);
        }
    }
}
