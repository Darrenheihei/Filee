// The speed window of "Change video speed": how fast the videos play, from x0.1 to x10, with the sound's pitch
// kept. The window also asks ffprobe how long every video is, so the hint reads how long the results will be
// before anything is converted.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Engines.Media;

namespace Filee.App.ViewModels;

/// <summary>One video of the batch: its name, and (once ffprobe answered) how long it plays.</summary>
public sealed partial class VideoSpeedSource : ObservableObject
{
    public VideoSpeedSource(string path) => Path = path;

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>How long the video plays, in seconds; 0 while ffprobe has not answered (or it would not say).</summary>
    [ObservableProperty]
    private double _seconds;

    public string Duration => VideoSpeedViewModel.Display(Seconds);
    partial void OnSecondsChanged(double value) => OnPropertyChanged(nameof(Duration));
}

public sealed partial class VideoSpeedViewModel : ObservableObject
{
    public const double MinSpeed = 0.1, MaxSpeed = 10;

    /// <summary>Twice as fast: the speed change people ask for first, and the slider starts there.</summary>
    public const double DefaultSpeed = 2;

    private readonly ILocalizer _loc;

    public VideoSpeedViewModel(IReadOnlyList<string> files, ILocalizer loc)
    {
        _loc = loc;
        Sources = new(files.Select(path => new VideoSpeedSource(path)));
        _speed = DefaultSpeed;
    }

    public ObservableCollection<VideoSpeedSource> Sources { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    [NotifyPropertyChangedFor(nameof(DurationHint))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private double _speed;

    /// <summary>How long the videos play in all, from ffprobe; 0 while it is unknown (still asking, or it would not say).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuration))]
    [NotifyPropertyChangedFor(nameof(DurationHint))]
    private double _inputSeconds;

    /// <summary>A length is known: the line under the slider reads how long the results will be.</summary>
    public bool HasDuration => InputSeconds > 0;

    /// <summary><see cref="Speed"/> kept inside what FFmpeg does well.</summary>
    public double SpeedFactor => Math.Clamp(Speed, MinSpeed, MaxSpeed);

    public string SpeedText => string.Create(CultureInfo.CurrentUICulture, $"{Speed:0.00} ×");

    /// <summary>A video played at its own speed comes out the same: that choice is greyed out rather than re-encoded for nothing.</summary>
    public bool CanApply => Math.Abs(Speed - 1) > 1e-9;

    /// <summary>The line under the slider: how long the results play, and how long the videos are now.</summary>
    public string DurationHint
    {
        get
        {
            if (!HasDuration)
                return "";
            var factor = string.Create(CultureInfo.CurrentUICulture, $"{SpeedFactor:0.00}");
            return Sources.Count == 1
                ? _loc.Format("video_speed.duration_one", Display(InputSeconds / SpeedFactor), factor, Display(InputSeconds))
                : _loc.Format("video_speed.duration_many", Sources.Count, Display(InputSeconds / SpeedFactor), factor, Display(InputSeconds));
        }
    }

    /// <summary>
    /// Asks ffprobe for every video's length, for the hint (and the per-row durations). Started when the window
    /// opens; a video that can't be probed adds nothing instead of keeping the others back.
    /// </summary>
    public async Task ProbeDurationsAsync()
    {
        var durations = await Task.Run(async () =>
        {
            if (FfmpegConverter.Locate() is not { } tools)
                return [];
            return await Task.WhenAll(Sources.Select(async source =>
            {
                try
                {
                    var info = await MediaInfo.ProbeAsync(tools.Ffprobe, source.Path, CancellationToken.None);
                    return info.Duration is { TotalSeconds: > 0 } length ? length.TotalSeconds : 0.0;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return 0.0;
                }
            }));
        });
        for (var i = 0; i < Sources.Count && i < durations.Length; i++)
            Sources[i].Seconds = durations[i];
        InputSeconds = durations.Sum();
    }

    /// <summary>A length as the clip window shows it: <c>0:07.5</c>, <c>4:05.0</c>, <c>1:02:05.3</c>.</summary>
    internal static string Display(double seconds) => ClipRange.Display(seconds);
}