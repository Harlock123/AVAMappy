using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMappy.Engine;
using AVAMappy.Views;

namespace AVAMappy.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MAPPY_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamappy-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MAPPY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMappy-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer mouse play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 51250, 8);
        g.HighScores.Insert("AVA", 42800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Round_the_mansion()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "round-start");
        Assert.True(PlayUntil(g, c, g => g.Mouse.InShaft && g.Mouse.Bounces >= 1 && g.Mouse.Vy < -300 && g.Mouse.Y < 600 && g.Cats.Any(k => Math.Abs(k.X - g.Mouse.X) < 400)));
        Shot(w, c, "bouncing");
        Assert.True(PlayUntil(g, c, g => g.Cats.Any(k => k.Stunned > GameEngine.StunSeconds - 0.4)));
        Shot(w, c, "door");
        Assert.True(PlayUntil(g, c, g => g.Mouse.InShaft && g.Mouse.Bounces >= 2 && g.Mouse.Y > 640));
        Shot(w, c, "trampoline");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.ItemsLeft <= 4 && g.Cats.Count(k => k.Away <= 0 && Math.Abs(k.X - g.Mouse.X) < 500) >= 2 && !g.Mouse.InShaft));
        Shot(w, c, "chase");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.RoundClear && g.PhaseTime > 0.4));
        Shot(w, c, "round-clear");
        Assert.True(PlayUntil(g, c, g => g.Round >= 2 && g.Phase == Phase.Play && g.Cats.Any(k => k.Boss && Math.Abs(k.X - g.Mouse.X) < 400 && !k.InShaft), 300));
        Shot(w, c, "big-tabby");
    }

    [AvaloniaFact]
    public void The_power_door()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.Bare();
        g.PutItem(0, 60, Loot.Safe);
        g.PutMouse(4, 690);
        g.PutDoor(4, 720, power: true);
        foreach (double x in new[] { 790.0, 850, 910 }) g.PutCat(4, x).Stunned = 99;
        g.Run(3, new Engine.Controls(1));
        g.Run(1, new Engine.Controls(0, true));
        for (int t = 0; t < 14; t++) { g.Idle(1); c.Advance(1 / 60.0); }
        Shot(w, c, "power-door");
    }
}
