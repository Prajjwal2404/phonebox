using System.Globalization;
using PhoneBox.Agent;
using Xunit;

public class WidgetSoundEditTests
{
    private static (ConfigStore Store, string Path) Make()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"phonebox-sound-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"pages":[{"id":"p","title":"P","widgets":[{"id":"s","type":"sessions","options":{"sound":false}},{"id":"c","type":"clock"}]}]}""");
        return (new ConfigStore(path), path);
    }

    private static void Cleanup(string path)
    {
        File.Delete(path);
        File.Delete(path + ".bak");
    }

    [Fact]
    public void SetsTheSoundOfSessionsWidget()
    {
        var (cfg, path) = Make();
        try
        {
            var r = WidgetSoundEdit.Apply(cfg, "p", "s", true);
            Assert.Equal(200, r.Status);
            var w = cfg.Get().Pages!.First(p => p.Id == "p").Widgets!.First(w => w.Id == "s");
            Assert.True(w.Bool("sound"));
            Assert.Equal(cfg.Version.ToString(CultureInfo.InvariantCulture), r.Version);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NonSessionsWidgetIsRefused()
    {
        var (cfg, path) = Make();
        try
        {
            var r = WidgetSoundEdit.Apply(cfg, "p", "c", false);
            Assert.Equal(400, r.Status);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void MissingWidgetIsNotFound()
    {
        var (cfg, path) = Make();
        try
        {
            var r = WidgetSoundEdit.Apply(cfg, "p", "missing", false);
            Assert.Equal(404, r.Status);
        }
        finally { Cleanup(path); }
    }
}

