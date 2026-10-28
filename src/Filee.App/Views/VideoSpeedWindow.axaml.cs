using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class VideoSpeedWindow : Window
{
    public VideoSpeedWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        UiZoom.Track(this);
    }

    /// <summary>The speed chosen, or null when the window was closed or cancelled.</summary>
    public double? Result { get; private set; }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoSpeedViewModel vm) return;
        Result = vm.Speed;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    /// <summary>One of the speeds people ask for first; the slider keeps the 0.01 steps between them.</summary>
    private void OnQuick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VideoSpeedViewModel vm && sender is Button { Tag: string text }
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            vm.Speed = speed;
    }
}