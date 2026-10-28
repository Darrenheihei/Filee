// The "Change video speed" window: the factor chosen with the slider (or the quick chips), the hint that reads how
// long the results will be, and the donut flow around it: the window asks before the videos are sped up, and the
// answer goes into the conversion. The donut test needs FFmpeg (elsewhere the slice is greyed out); the window and
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
public class VideoSpeedTests
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
    private static VideoSpeedWindow OpenOn(RadialController radial, WindowService windows, string video)
    {
        radial.ShowForFiles(450, 400, [video]);
        var donut = radial.Window!.DonutControl;
        Assert.True(donut.HandleKey(Key.Right));
        Assert.True(donut.HandleKey(Key.Enter));
        RenderTests.Pump();
        return Assert.IsType<VideoSpeedWindow>(windows.VideoSpeed);
    }

    [AvaloniaFact]
    public async Task The_donut_asks_how_fast_the_videos_play_and_converts_with_the_answer()
    {
        TestServices.EnsureInitialized();
        Assert.SkipUnless(FfmpegConverter.Locate() is not null,
            "The slice needs FFmpeg to be offered (pwsh build/fetch-engines.ps1 -Only ffmpeg).");
        var folder = Directory.CreateTempSubdirectory("filee-video-speed-").FullName;
        var video = Path.Combine(folder, "clip.mp4");
        File.WriteAllBytes(video, new byte[40_000]);
        var store = AppHost.Get<UserDataStore>();
        var profile = store.Profiles.Single(p => p.Id == "video");
        var previous = profile.PresetIds;
        var radial = AppHost.Get<RadialController>();
        var windows = AppHost.Get<WindowService>();
        var conversions = AppHost.Get<ConversionService>();
        var before = conversions.Jobs.Select(job => job.Job.Id).ToHashSet();
        try
        {
            profile.PresetIds = ["change-speed"];
            var dialog = OpenOn(radial, windows, video);
            var vm = Assert.IsType<VideoSpeedViewModel>(dialog.DataContext);
            Assert.False(radial.IsVisible); // the donut has closed for the window, like for a compression
            Assert.Equal(2.0, vm.Speed);    // the window opens at twice as fast
            Assert.DoesNotContain(conversions.Jobs, job => !before.Contains(job.Job.Id));

            vm.Speed = 1.5;
            Click(dialog, "Apply");

            JobViewModel? started = null;
            for (var i = 0; i < 200 && started is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                started = conversions.Jobs.FirstOrDefault(job => !before.Contains(job.Job.Id));
                if (started is null)
                    await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Assert.NotNull(started);
            Assert.Equal(1.5, started.Job.Preset.Media.Speed);
        }
        finally
        {
            windows.VideoSpeed?.Close();
            radial.Close(animate: false);
            profile.PresetIds = previous;
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task The_window_returns_the_chosen_factor_or_nothing()
    {
        TestServices.EnsureInitialized();
        var windows = AppHost.Get<WindowService>();
        var folder = Directory.CreateTempSubdirectory("filee-video-speed-").FullName;
        var video = Path.Combine(folder, "clip.mp4");
        File.WriteAllBytes(video, new byte[4096]);
        try
        {
            var chosenTask = windows.ChooseVideoSpeedAsync([video]);
            for (var i = 0; i < 200 && windows.VideoSpeed is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            var dialog = Assert.IsType<VideoSpeedWindow>(windows.VideoSpeed);

            Click(dialog, "Quadruple");
            Click(dialog, "Apply");
            Assert.Equal(4, await chosenTask);
            Assert.Null(windows.VideoSpeed);

            chosenTask = windows.ChooseVideoSpeedAsync([video]);
            for (var i = 0; i < 200 && windows.VideoSpeed is null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Click(Assert.IsType<VideoSpeedWindow>(windows.VideoSpeed), "Cancel");
            Assert.Null(await chosenTask);
        }
        finally
        {
            windows.VideoSpeed?.Close();
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task A_probed_video_gets_its_length_and_an_unreadable_one_does_not()
    {
        TestServices.EnsureInitialized();
        var tools = FfmpegConverter.Locate();
        Assert.SkipUnless(tools is not null, "ffprobe comes with FFmpeg (pwsh build/fetch-engines.ps1 -Only ffmpeg).");
        var folder = Directory.CreateTempSubdirectory("filee-video-speed-").FullName;
        var real = Path.Combine(folder, "real.mp4");
        var broken = Path.Combine(folder, "broken.mp4");
        File.WriteAllBytes(broken, new byte[2048]);
        try
        {
            var ffmpeg = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tools!.Value.Ffmpeg,
                "-f lavfi -i testsrc=size=320x240:rate=15:duration=5 -f lavfi -i sine=frequency=440:duration=5 " +
                $"-shortest -y \"{real}\"")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            await ffmpeg!.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, ffmpeg.ExitCode);

            var vm = new VideoSpeedViewModel([real, broken], AppHost.Get<ILocalizer>());
            await vm.ProbeDurationsAsync();
            Assert.True(vm.HasDuration); // the broken file adds nothing instead of keeping the real length back
            Assert.InRange(vm.InputSeconds, 4, 6);
            Assert.InRange(vm.Sources[0].Seconds, 4, 6);
            Assert.Equal(0, vm.Sources[1].Seconds);

            var none = new VideoSpeedViewModel([broken], AppHost.Get<ILocalizer>());
            await none.ProbeDurationsAsync();
            Assert.False(none.HasDuration);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void The_factor_is_greyed_out_at_the_video_s_own_speed_and_the_hint_reads_the_lengths()
    {
        TestServices.EnsureInitialized();
        var loc = AppHost.Get<ILocalizer>();
        var vm = new VideoSpeedViewModel(["clip.mp4"], loc);
        Assert.Equal((2.0, "2.00 ×", true), (vm.Speed, vm.SpeedText, vm.CanApply));

        vm.Speed = 1;
        Assert.False(vm.CanApply); // a video at its own speed comes out the same: nothing to apply
        vm.Speed = 0.5;
        Assert.True(vm.CanApply);
        Assert.Equal("0.50 ×", vm.SpeedText);

        vm.InputSeconds = 120;
        Assert.True(vm.HasDuration);
        Assert.Equal(loc.Format("video_speed.duration_one", "4:00.0", "0.50", "2:00.0"), vm.DurationHint);

        var many = new VideoSpeedViewModel(["a.mp4", "b.mp4"], loc) { Speed = 0.5, InputSeconds = 90 };
        many.Sources[0].Seconds = 60;
        many.Sources[1].Seconds = 30;
        Assert.Equal(("1:00.0", "0:30.0"), (many.Sources[0].Duration, many.Sources[1].Duration));
        Assert.Equal(loc.Format("video_speed.duration_many", 2, "3:00.0", "0.50", "1:30.0"), many.DurationHint);

        // A speed a hand-edited preset cannot hold is clamped (the window's own slider never goes there).
        vm.Speed = 50;
        Assert.Equal(10, vm.SpeedFactor);
    }
}