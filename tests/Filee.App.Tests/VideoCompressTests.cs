// The "Compress video" window: the percentage chosen with the slider, the hint that reads what it makes of the files,
// and the donut flow around it: the window asks before the videos are compressed, and the answer becomes each video's
// own size limit when the queue runs. The donut tests need FFmpeg (elsewhere the slice is greyed out); the window and
// hint tests run without it.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Settings;
using Filee.Engines.Media;
using Xunit;

namespace Filee.App.Tests;

[Collection("MainWindow")]
public class VideoCompressTests
{
    private static void Click(Window window, string name)
    {
        var button = window.FindControl<Button>(name)!;
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        RenderTests.Pump();
    }

    /// <summary>Opens the donut on <paramref name="video"/> and presses the one slice through to the window.</summary>
    private static VideoCompressWindow OpenOn(RadialController radial, WindowService windows, string video)
    {
        radial.ShowForFiles(450, 400, [video]);
        var donut = radial.Window!.DonutControl;
        Assert.True(donut.HandleKey(Key.Right));
        Assert.True(donut.HandleKey(Key.Enter));
        RenderTests.Pump();
        return Assert.IsType<VideoCompressWindow>(windows.VideoCompress);
    }

    [AvaloniaFact]
    public async Task The_donut_asks_how_large_the_videos_may_stay_and_converts_with_the_answer()
    {
        TestServices.EnsureInitialized();
        Assert.SkipUnless(FfmpegConverter.Locate() is not null,
            "The slice needs FFmpeg to be offered (pwsh build/fetch-engines.ps1 -Only ffmpeg).");
        var folder = Directory.CreateTempSubdirectory("filee-video-compress-").FullName;
        var video = Path.Combine(folder, "clip.mp4");
        File.WriteAllBytes(video, new byte[40_000_000]);
        var store = AppHost.Get<UserDataStore>();
        var profile = store.Profiles.Single(p => p.Id == "video");
        var previous = profile.PresetIds;
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var conversions = AppHost.Get<ConversionService>();
        var before = conversions.Jobs.Select(job => job.Job.Id).ToHashSet();
        try
        {
            profile.PresetIds = ["compress-video"];
            var dialog = OpenOn(radial, windows, video);
            var vm = Assert.IsType<VideoCompressViewModel>(dialog.DataContext);
            Assert.False(radial.IsVisible); // the donut has closed for the window, like for a merge or a save dialog
            Assert.Equal([video], vm.Sources.Select(source => source.Path));
            Assert.Equal((40_000_000, 20_000_000), (vm.SourceBytes, vm.TargetBytes)); // the default 50 %
            Assert.DoesNotContain(conversions.Jobs, job => !before.Contains(job.Job.Id)); // nothing converts before the answer

            Click(dialog, "Compress");

            JobViewModel? started = null;
            for (var i = 0; i < 200 && started is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                started = conversions.Jobs.FirstOrDefault(job => !before.Contains(job.Job.Id));
                if (started is null)
                    await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Assert.NotNull(started);
            Assert.Equal(50, started.Job.Preset.Media.MaxSizePercent);
            Assert.Equal(0, started.Job.Preset.Media.MaxSizeMb); // the queue turns the percentage into the video's own limit
        }
        finally
        {
            windows.VideoCompress?.Close();
            radial.Close(animate: false);
            profile.PresetIds = previous;
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task Cancelling_keeps_the_videos_uncompressed()
    {
        TestServices.EnsureInitialized();
        Assert.SkipUnless(FfmpegConverter.Locate() is not null,
            "The slice needs FFmpeg to be offered (pwsh build/fetch-engines.ps1 -Only ffmpeg).");
        var folder = Directory.CreateTempSubdirectory("filee-video-compress-").FullName;
        var video = Path.Combine(folder, "clip.mp4");
        File.WriteAllBytes(video, new byte[40_000_000]);
        var store = AppHost.Get<UserDataStore>();
        var profile = store.Profiles.Single(p => p.Id == "video");
        var previous = profile.PresetIds;
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var conversions = AppHost.Get<ConversionService>();
        var before = conversions.Jobs.Select(job => job.Job.Id).ToHashSet();
        try
        {
            profile.PresetIds = ["compress-video"];
            var dialog = OpenOn(radial, windows, video);

            Click(dialog, "Cancel");
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(windows.VideoCompress);
            Assert.DoesNotContain(conversions.Jobs, job => !before.Contains(job.Job.Id));
        }
        finally
        {
            windows.VideoCompress?.Close();
            radial.Close(animate: false);
            profile.PresetIds = previous;
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task The_window_returns_the_chosen_percentage_or_nothing()
    {
        TestServices.EnsureInitialized();
        var windows = AppHost.Get<WindowService>();
        var folder = Directory.CreateTempSubdirectory("filee-video-compress-").FullName;
        var video = Path.Combine(folder, "clip.mp4");
        File.WriteAllBytes(video, new byte[2_000_000]);
        try
        {
            // Opened the way RadialController does: the call waits for the window, so the test waits for it beside.
            var chosenTask = windows.ChooseVideoCompressPercentAsync([video]);
            for (var i = 0; i < 200 && windows.VideoCompress is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            var dialog = Assert.IsType<VideoCompressWindow>(windows.VideoCompress);
            var vm = Assert.IsType<VideoCompressViewModel>(dialog.DataContext);
            Assert.Equal(2_000_000, vm.SourceBytes);

            vm.Percent = 30;
            Click(dialog, "Compress");
            Assert.Equal(30, await chosenTask);
            Assert.Null(windows.VideoCompress);

            chosenTask = windows.ChooseVideoCompressPercentAsync([video]);
            for (var i = 0; i < 200 && windows.VideoCompress is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Click(Assert.IsType<VideoCompressWindow>(windows.VideoCompress), "Cancel");
            Assert.Null(await chosenTask);
            Assert.Null(windows.VideoCompress);
        }
        finally
        {
            windows.VideoCompress?.Close();
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void The_hint_reads_the_most_the_videos_may_take_together()
    {
        TestServices.EnsureInitialized();
        var loc = AppHost.Get<ILocalizer>();
        var folder = Directory.CreateTempSubdirectory("filee-video-compress-").FullName;
        var big = Path.Combine(folder, "big.mp4");
        var small = Path.Combine(folder, "small.mp4");
        File.WriteAllBytes(big, new byte[30_000_000]);
        File.WriteAllBytes(small, new byte[10_000_000]);
        try
        {
            var vm = new VideoCompressViewModel([big, small], loc);
            Assert.Equal((40_000_000, 20_000_000), (vm.SourceBytes, vm.TargetBytes));
            Assert.Equal("50 %", vm.PercentText);
            Assert.Equal(loc.Format("video_compress.size_hint_many", 2, "20 MB", "50", "40 MB"), vm.SizeHint);

            vm.Percent = 25;
            Assert.Equal(10_000_000, vm.TargetBytes);
            Assert.Equal("25 %", vm.PercentText);
            Assert.Equal(loc.Format("video_compress.size_hint_many", 2, "10 MB", "25", "40 MB"), vm.SizeHint);

            var one = new VideoCompressViewModel([big], loc);
            Assert.Equal(loc.Format("video_compress.size_hint_one", "15 MB", "50", "30 MB"), one.SizeHint);

            // A file that is gone counts as nothing instead of breaking the window.
            var missing = new VideoCompressViewModel([Path.Combine(folder, "gone.mp4")], loc);
            Assert.Equal((0, 0), (missing.SourceBytes, missing.TargetBytes));
            Assert.Equal("0 MB", missing.Sources[0].Size);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void The_floor_disables_compress_until_the_percentage_reaches_it()
    {
        TestServices.EnsureInitialized();
        var loc = AppHost.Get<ILocalizer>();
        var vm = new VideoCompressViewModel(["a.mp4"], loc);
        Assert.True(vm.CanCompress); // before ffprobe answers, Compress works as it always has
        Assert.False(vm.HasFloor);
        Assert.Equal("", vm.FloorText);

        vm.MinimumBytes = 6_100_000;
        vm.MinimumPercent = 21;
        Assert.True(vm.HasFloor);
        Assert.Equal(loc.Format("video_compress.floor", "6.1 MB", "21"), vm.FloorText);

        vm.Percent = 10;
        Assert.True(vm.BelowFloor);
        Assert.False(vm.CanCompress);
        vm.Percent = 21;
        Assert.False(vm.BelowFloor);
        Assert.True(vm.CanCompress);
    }

    [AvaloniaFact]
    public async Task A_probed_video_gets_a_floor_and_an_unreadable_one_does_not()
    {
        TestServices.EnsureInitialized();
        var tools = FfmpegConverter.Locate();
        Assert.SkipUnless(tools is not null, "ffprobe comes with FFmpeg (pwsh build/fetch-engines.ps1 -Only ffmpeg).");
        var folder = Directory.CreateTempSubdirectory("filee-video-compress-").FullName;
        var real = Path.Combine(folder, "real.mp4");
        var broken = Path.Combine(folder, "broken.mp4");
        File.WriteAllBytes(broken, new byte[2048]);
        try
        {
            // A real 5-second video with sound, as FFmpeg makes it for itself.
            var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tools!.Value.Ffmpeg,
                "-f lavfi -i testsrc=size=320x240:rate=15:duration=5 -f lavfi -i sine=frequency=440:duration=5 " +
                $"-shortest -y \"{real}\"")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            await ffmpeg!.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, ffmpeg.ExitCode);

            var vm = new VideoCompressViewModel([real, broken], AppHost.Get<ILocalizer>());
            await vm.ProbeMinimumsAsync();
            Assert.True(vm.HasFloor); // the broken file adds nothing instead of keeping the real one's floor back
            Assert.True(vm.MinimumBytes > 0);

            var only = new VideoCompressViewModel([real], AppHost.Get<ILocalizer>());
            await only.ProbeMinimumsAsync();
            Assert.Equal(only.MinimumPercent, vm.MinimumPercent);
            Assert.Equal(only.MinimumBytes, vm.MinimumBytes);

            var none = new VideoCompressViewModel([broken], AppHost.Get<ILocalizer>());
            await none.ProbeMinimumsAsync();
            Assert.False(none.HasFloor);
            Assert.True(none.CanCompress);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}