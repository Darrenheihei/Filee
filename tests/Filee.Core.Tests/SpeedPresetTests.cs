// "Change video speed": one preset that asks for the speed in the window, like Compress video asks for a size;
// nothing else in the factory list asks either question or starts with a speed of its own.

using Filee.Core.Presets;

namespace Filee.Core.Tests;

public class SpeedPresetTests
{
    [Fact]
    public void The_speed_preset_keeps_the_video_s_format_and_asks_for_the_factor()
    {
        var preset = Assert.Single(BuiltInData.SpeedPresets());
        Assert.Equal("change-speed", preset.Id);
        Assert.Equal(BuiltInData.SameAsSource, preset.TargetFormat);
        Assert.True(preset.Media.AskSpeed);
        Assert.Equal(1, preset.Media.Speed);
        Assert.Equal("{name}_speed", preset.Output.FileNamePattern);
        Assert.Contains(preset.Id, BuiltInData.CreatePresets().Select(p => p.Id));
    }

    [Fact]
    public void The_asking_presets_are_the_only_ones_and_nothing_else_starts_sped_up()
    {
        var presets = BuiltInData.CreatePresets();
        Assert.Equal(["change-speed"], presets.Where(p => p.Media.AskSpeed).Select(p => p.Id));
        Assert.Equal(["compress-video"], presets.Where(p => p.Media.AskSizePercent).Select(p => p.Id));
        Assert.DoesNotContain(presets, p => p.Media.Speed != 1);
    }
}