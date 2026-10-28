// "Change video speed": the plan re-times the picture (setpts) and chains atempo steps for the sound's pitch, whether
// a graphics chip encodes or not, and never copies the streams at a speed they would not play at.

using Filee.Core.Presets;
using Filee.Engines.Media;
using Xunit;

namespace Filee.Engines.Tests;

public class VideoSpeedTests
{
    private static readonly MediaInfo Info = new(TimeSpan.FromSeconds(60),
        new VideoStreamInfo(1920, 1080, 1, 0, 30, Codec: "h264", PixelFormat: "yuv420p"),
        new AudioStreamInfo(2, 48_000, "aac"), CoverStreamIndex: null);

    private static string ArgsOf(MediaOptions options) =>
        string.Join(" ", MediaEncoding.Plan("in.mp4", "out.mp4", "mp4", options, Info, workDirectory: "work")
            .SelectMany(pass => pass.Arguments));

    [Fact]
    public void A_speed_change_retimes_the_picture_and_keeps_the_sound_s_pitch_instead_of_copying()
    {
        var args = ArgsOf(new MediaOptions { Speed = 2 });
        Assert.Contains("setpts=PTS/2", args);
        Assert.Contains("-af atempo=2", args);
        // The streams cannot be copied: h264 and aac would arrive at the old speed.
        Assert.DoesNotContain("copy", args);
    }

    [Fact]
    public void Slower_and_faster_extremes_take_chained_atempo_steps()
    {
        var slow = ArgsOf(new MediaOptions { Speed = 0.1 });
        Assert.Contains("setpts=PTS/0.1", slow);
        Assert.Contains("-af atempo=0.5,atempo=0.5,atempo=0.5,atempo=0.8", slow);

        var fast = ArgsOf(new MediaOptions { Speed = 10 });
        Assert.Contains("setpts=PTS/10", fast);
        Assert.Contains("-af atempo=2,atempo=2,atempo=2,atempo=1.25", fast);
    }

    [Fact]
    public void At_the_video_s_own_speed_nothing_is_retimed_and_the_streams_are_copied()
    {
        var args = ArgsOf(new MediaOptions());
        Assert.DoesNotContain("setpts", args);
        Assert.DoesNotContain("-af", args);
        Assert.Contains("-c:v copy", args);
        Assert.Contains("-c:a copy", args);
    }
}