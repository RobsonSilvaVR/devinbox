using DevInbox.Core.Settings;
using Xunit;

namespace DevInbox.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GitHubCheckerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingFile_LoadsDefaults()
    {
        var store = new SettingsStore(_directory);

        Assert.Equal(60, store.Current.PollIntervalSeconds);
        Assert.True(store.Current.Events.NewComment);
        Assert.False(store.Current.QuietHours.Enabled);
    }

    [Fact]
    public void SaveAndReload_RoundTrips()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings
        {
            PollIntervalSeconds = 120,
            StartWithWindows = true,
            QuietHours = new QuietHoursSettings { Enabled = true, Start = "21:30", End = "07:00" },
        };
        settings.Events.MergeConflict = false;

        store.Save(settings);
        var reloaded = new SettingsStore(_directory);

        Assert.Equal(120, reloaded.Current.PollIntervalSeconds);
        Assert.True(reloaded.Current.StartWithWindows);
        Assert.False(reloaded.Current.Events.MergeConflict);
        Assert.True(reloaded.Current.QuietHours.Enabled);
        Assert.Equal("21:30", reloaded.Current.QuietHours.Start);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ isso não é json válido");

        var store = new SettingsStore(_directory);

        Assert.Equal(60, store.Current.PollIntervalSeconds);
    }

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("03:30", true)]
    [InlineData("07:59", true)]
    [InlineData("08:00", false)]
    [InlineData("12:00", false)]
    [InlineData("21:59", false)]
    public void QuietHours_OvernightWindow(string time, bool expected)
    {
        var quietHours = new QuietHoursSettings { Enabled = true, Start = "22:00", End = "08:00" };

        Assert.Equal(expected, quietHours.IsQuietAt(TimeSpan.Parse(time)));
    }

    [Theory]
    [InlineData("12:30", true)]
    [InlineData("11:59", false)]
    [InlineData("14:00", false)]
    public void QuietHours_SameDayWindow(string time, bool expected)
    {
        var quietHours = new QuietHoursSettings { Enabled = true, Start = "12:00", End = "14:00" };

        Assert.Equal(expected, quietHours.IsQuietAt(TimeSpan.Parse(time)));
    }

    [Fact]
    public void QuietHours_Disabled_IsNeverQuiet()
    {
        var quietHours = new QuietHoursSettings { Enabled = false, Start = "00:00", End = "23:59" };

        Assert.False(quietHours.IsQuietAt(TimeSpan.Parse("12:00")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
