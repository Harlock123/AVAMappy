using AVAMappy.Engine;

namespace AVAMappy.Tests;

/// <summary>How the computer mouse gets on, round by round (set MAPPY_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("MAPPY_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "mappy-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var rounds = new List<string>();
            var last = g.Phase;
            int round = 1, t = 0;
            double start = 0;
            for (; t < 60 * 600 && g.Round <= 4; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"R{g.Round} {g.Cause}"] = deaths.GetValueOrDefault($"R{g.Round} {g.Cause}") + 1;
                if (g.Round != round) { rounds.Add($"R{round}:{(t - start) / 60:0}s"); round = g.Round; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60:0}s score {g.Score} loot left {g.ItemsLeft} | {string.Join(" ", rounds)} | doing {g.Pilot.Doing} at floor {g.Mouse.Floor} x {g.Mouse.X:0}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
