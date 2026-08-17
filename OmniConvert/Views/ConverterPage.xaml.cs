using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace OmniConvert.Views;

public sealed partial class ConverterPage : Page
{
    private static readonly TimeSpan DragAnimationDuration = TimeSpan.FromMilliseconds(150);

    private static readonly CubicEase DragAnimationEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public ConverterPage()
    {
        InitializeComponent();
        TargetFormatBox.ItemsSource = new[] { "简体中文", "English", "日本語" };
    }

    private static Color GetResourceColor(string key)
    {
        return ((SolidColorBrush)App.Current.Resources[key]).Color;
    }

    private static void AnimateTo(Storyboard storyboard, DependencyObject target, string propertyPath, Color from, Color to)
    {
        var animation = new ColorAnimation
        {
            From = from,
            To = to,
            Duration = DragAnimationDuration,
            EasingFunction = DragAnimationEase
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }

    private static void AnimateTo(Storyboard storyboard, DependencyObject target, string propertyPath, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = DragAnimationDuration,
            EasingFunction = DragAnimationEase
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }

    private void AnimateCardVisuals(bool enterDrop)
    {
        // 左右卡片共用 Colors.xaml 里的笔刷资源，动画前先给 MainCard 换上独立实例，
        // 避免直接动画共享笔刷把 SideCard 一起染色。
        var borderBrush = new SolidColorBrush(((SolidColorBrush)MainCard.BorderBrush).Color);
        var backgroundBrush = new SolidColorBrush(((SolidColorBrush)MainCard.Background).Color);
        MainCard.BorderBrush = borderBrush;
        MainCard.Background = backgroundBrush;

        var storyboard = new Storyboard();

        AnimateTo(storyboard, MainCard, "(Border.BorderBrush).(SolidColorBrush.Color)",
            borderBrush.Color,
            GetResourceColor(enterDrop ? "DropCardBorderBrush" : "CardBorderBrush"));
        AnimateTo(storyboard, MainCard, "(Border.Background).(SolidColorBrush.Color)",
            backgroundBrush.Color,
            GetResourceColor(enterDrop ? "DropCardBackgroundBrush" : "CardBackgroundBrush"));
        AnimateTo(storyboard, DropHintText, "Opacity", DropHintText.Opacity, enterDrop ? 0 : 1);
        AnimateTo(storyboard, DropIcon, "Opacity", DropIcon.Opacity, enterDrop ? 1 : 0);

        storyboard.Begin();
    }

    private void MainCard_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            AnimateCardVisuals(true);
        }
    }

    private void MainCard_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void MainCard_DragLeave(object sender, DragEventArgs e)
    {
        AnimateCardVisuals(false);
    }

    private void MainCard_Drop(object sender, DragEventArgs e)
    {
        AnimateCardVisuals(false);
    }
}

