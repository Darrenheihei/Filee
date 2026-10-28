using Avalonia.Controls;
using Avalonia.Interactivity;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class VideoCompressWindow : Window
{
    public VideoCompressWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        UiZoom.Track(this);
    }

    /// <summary>The percentage chosen, or null when the window was closed or cancelled.</summary>
    public double? Result { get; private set; }

    private void OnCompress(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoCompressViewModel vm) return;
        Result = vm.Percent;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}