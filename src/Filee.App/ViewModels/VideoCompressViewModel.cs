// The percentage window of "Compress video": how large the videos may stay, as a percentage each one keeps of its
// own size. The window only chooses the percentage; the queue turns it into every video's own size limit when the
// conversion runs, so one batch may hold videos of any sizes.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Engines.Media;

namespace Filee.App.ViewModels;

/// <summary>One video of the batch: its name, and how large it is now.</summary>
public sealed class VideoCompressSource(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>What the video takes now; 0 for a file that is gone (its row shows no size, the hint counts the rest).</summary>
    public long Bytes { get; } = File.Exists(path) ? new FileInfo(path).Length : 0;
    public string Size => VideoCompressViewModel.Megabytes(Bytes);
}

public sealed partial class VideoCompressViewModel : ObservableObject
{
    /// <summary>The window keeps a part of each video: at most 95 %, at least 5 %.</summary>
    public const double MinPercent = 5, MaxPercent = 95;

    /// <summary>About half: the answer most drops want is somewhere near it, and the slider is right there.</summary>
    public const double DefaultPercent = 50;

    private readonly ILocalizer _loc;

    public VideoCompressViewModel(IReadOnlyList<string> files, ILocalizer loc)
    {
        _loc = loc;
        Sources = new(files.Select(path => new VideoCompressSource(path)));
        _percent = DefaultPercent;
    }

    public ObservableCollection<VideoCompressSource> Sources { get; }

    /// <summary>What the videos take together now.</summary>
    public long SourceBytes => Sources.Sum(source => source.Bytes);

    /// <summary>The most they may take together at <see cref="Percent"/>.</summary>
    public long TargetBytes => SizeLimit.FromPercent(SourceBytes, Percent);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    [NotifyPropertyChangedFor(nameof(SizeHint))]
    [NotifyPropertyChangedFor(nameof(BelowFloor))]
    [NotifyPropertyChangedFor(nameof(CanCompress))]
    private double _percent;

    /// <summary>
    /// The smallest the videos can get, asked from ffprobe once the window is open (<see cref="ProbeMinimumsAsync"/>):
    /// the sound alone takes most of a very low limit, and below the floor the queue refuses rather than write a file
    /// nobody can watch. 0 while it is unknown (still asking, or ffprobe answered nothing usable).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFloor))]
    [NotifyPropertyChangedFor(nameof(FloorText))]
    [NotifyPropertyChangedFor(nameof(BelowFloor))]
    [NotifyPropertyChangedFor(nameof(CanCompress))]
    private long _minimumBytes;

    /// <summary>The percentage of the original at which the floor is reached: the highest one any video of the batch needs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFloor))]
    [NotifyPropertyChangedFor(nameof(FloorText))]
    [NotifyPropertyChangedFor(nameof(BelowFloor))]
    [NotifyPropertyChangedFor(nameof(CanCompress))]
    private double _minimumPercent;

    /// <summary>A floor is known: the line under the hint says what it is.</summary>
    public bool HasFloor => MinimumPercent > 0;

    /// <summary>The slider is under the floor: these videos can never get this small, so Compress waits.</summary>
    public bool BelowFloor => HasFloor && Percent < MinimumPercent;

    /// <summary>Compress works, also while the floor is still being asked.</summary>
    public bool CanCompress => !BelowFloor;

    public string PercentText => string.Create(CultureInfo.CurrentUICulture, $"{Percent:0} %");

    /// <summary>The hint under the slider: the most these videos may take, and what percentage that is.</summary>
    public string SizeHint
    {
        get
        {
            var percent = string.Create(CultureInfo.CurrentUICulture, $"{Percent:0}");
            return Sources.Count == 1
                ? _loc.Format("video_compress.size_hint_one", Megabytes(TargetBytes), percent, Megabytes(SourceBytes))
                : _loc.Format("video_compress.size_hint_many", Sources.Count, Megabytes(TargetBytes), percent, Megabytes(SourceBytes));
        }
    }

    /// <summary>The line under the hint: the size and percentage no setting can go below.</summary>
    public string FloorText
    {
        get
        {
            var percent = string.Create(CultureInfo.CurrentUICulture, $"{MinimumPercent:0}");
            return HasFloor ? _loc.Format("video_compress.floor", Megabytes(MinimumBytes), percent) : "";
        }
    }

    /// <summary>
    /// Asks ffprobe for every video's length and works out the smallest size each one can reach
    /// (<see cref="MediaEncoding.MinSizeBytes"/>). Started when the window opens; a video that can't be probed
    /// (broken data, no FFmpeg) adds no floor instead of keeping the others back.
    /// </summary>
    public async Task ProbeMinimumsAsync()
    {
        var floors = await Task.Run(async () =>
        {
            if (FfmpegConverter.Locate() is not { } tools)
                return [];
            return await Task.WhenAll(Sources.Select(async source =>
            {
                try
                {
                    var info = await MediaInfo.ProbeAsync(tools.Ffprobe, source.Path, CancellationToken.None);
                    if (info.Duration is not { TotalSeconds: > 0 } seconds || source.Bytes <= 0)
                        return (Bytes: 0L, Percent: 0.0);
                    var bytes = MediaEncoding.MinSizeBytes(seconds.TotalSeconds, info.AudioTracks.Count);
                    return (Bytes: bytes, Percent: Math.Clamp(Math.Ceiling(bytes * 100.0 / source.Bytes), 0, 100));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return (Bytes: 0L, Percent: 0.0);
                }
            }));
        });
        MinimumBytes = floors.Sum(floor => floor.Bytes);
        MinimumPercent = floors.Max(floor => floor.Percent);
    }

    /// <summary>A size the way Filee shows limits: megabytes of 1,000,000 bytes, as <see cref="SizeLimit.Describe"/>.</summary>
    internal static string Megabytes(long bytes) => string.Create(CultureInfo.CurrentUICulture, $"{bytes / 1e6:0.##} MB");
}