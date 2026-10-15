using AVAMappy.Engine;
using Engine = AVAMappy.Engine;

namespace AVAMappy.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamappy-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play in an empty mansion: no doors, loot or cats unless a test puts them there.</summary>
    private static GameEngine Game(int round = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(round);
        g.Bare();
        // A last thing to take, far away, so the round doesn't end by itself.
        g.PutItem(0, 60, Loot.Safe);
        return g;
    }

    private static readonly Engine.Controls Left = new(-1), Right = new(1), Press = new(0, true);

    // ───────────────────────────── walking and bouncing ─────────────────────────────

    [Fact]
    public void The_mouse_walks_its_floor_and_drops_into_a_shaft_at_the_end_of_the_room()
    {
        var g = Game();
        g.PutMouse(4, 800);
        g.Run(30, Right);
        Assert.Equal(800 + GameEngine.MouseSpeed / 2, g.Mouse.X, 0);
        for (int t = 0; t < 120 && !g.Mouse.InShaft; t++) g.Run(1, Right);
        Assert.True(g.Mouse.InShaft);
        Assert.Equal(2, g.Mouse.Shaft);
    }

    [Fact]
    public void Bouncing_he_steps_off_at_a_floor_as_it_goes_by_but_only_after_a_bounce()
    {
        var g = Game();
        g.PutMouse(2, 980);
        // Walking in with the way still held doesn't carry him straight across.
        g.Run(20, Right);
        Assert.True(g.Mouse.InShaft);
        g.Run(10, Right);
        Assert.True(g.Mouse.InShaft);
        // After the first bounce, holding left as the top floor goes by steps him off there.
        for (int t = 0; t < 400 && g.Mouse.InShaft; t++)
            g.Tick(g.Mouse.Bounces >= 1 && g.Mouse.Vy < 0 && GameEngine.ExitFloor(g.Mouse) == 0 ? Left : default);
        Assert.False(g.Mouse.InShaft);
        Assert.Equal(0, g.Mouse.Floor);
        Assert.True(g.Mouse.X < Field.ShaftX[2]);
    }

    [Fact]
    public void Four_bounces_in_a_row_break_the_trampoline()
    {
        var g = Game();
        g.PutMouse(4, 980);
        for (int t = 0; t < 60 && !g.Mouse.InShaft; t++) g.Run(1, Right);
        Assert.True(g.Mouse.InShaft);
        for (int t = 0; t < 60 * 10 && g.Phase == Phase.Play; t++) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("THE TRAMPOLINE BROKE", g.Cause);
        Assert.Equal(GameEngine.BreakAt, g.Mouse.Bounces);
    }

    // ───────────────────────────── doors ─────────────────────────────

    [Fact]
    public void A_shut_door_stops_him_and_the_button_opens_it()
    {
        var g = Game();
        g.PutMouse(4, 800);
        var d = g.PutDoor(4, 860);
        g.Run(60, Right);
        Assert.Equal(860 - 14, g.Mouse.X, 0);
        g.Run(1, Press);
        Assert.True(d.Open);
        Assert.Equal(1, d.Side);
        g.Idle(1);
        g.Run(30, Right);
        Assert.True(g.Mouse.X > 870);
    }

    [Fact]
    public void Opened_in_a_cats_face_a_door_flattens_it_for_a_while()
    {
        var g = Game();
        g.PutMouse(4, 800);
        g.PutDoor(4, 830);
        var cat = g.PutCat(4, 880);
        g.Run(10, Right);
        g.Run(1, Press);
        Assert.True(cat.Stunned > 0);
        Assert.Equal(GameEngine.KnockPoints, g.Score);
        // Flattened, it can't catch him.
        cat.X = g.Mouse.X + 10;
        g.Idle(20);
        Assert.Equal(Phase.Play, g.Phase);
        g.Idle((int)(60 * GameEngine.StunSeconds));
        Assert.Equal(0, cat.Stunned, 1);
    }

    [Fact]
    public void A_power_door_sends_a_wave_that_sweeps_every_cat_ahead_for_rising_points()
    {
        var g = Game();
        g.PutMouse(4, 700);
        g.PutDoor(4, 730, power: true);
        var a = g.PutCat(4, 820);
        var b = g.PutCat(4, 900);
        var c = g.PutCat(4, 1300);
        foreach (var cat in new[] { a, b, c }) cat.Stunned = 99;
        g.Run(5, Right);
        g.Run(1, Press);
        Assert.Single(g.Waves);
        g.Idle(60);
        Assert.True(a.Away > 0 && b.Away > 0);
        Assert.Equal(0, c.Away);
        Assert.Equal(GameEngine.WavePoints + 2 * GameEngine.WavePoints, g.Score);
        Assert.False(g.Doors[0].Power);
    }

    // ───────────────────────────── cats and loot ─────────────────────────────

    [Fact]
    public void A_cat_on_his_floor_in_his_room_catches_him()
    {
        var g = Game();
        g.PutMouse(4, 800);
        g.PutCat(4, 960);
        for (int t = 0; t < 60 * 5 && g.Phase == Phase.Play; t++) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CAUGHT BY A CAT", g.Cause);
    }

    [Fact]
    public void A_cat_on_another_floor_bounces_up_a_shaft_to_get_at_him()
    {
        var g = Game();
        g.PutMouse(1, 800);
        var cat = g.PutCat(4, 900);
        bool bounced = false;
        for (int t = 0; t < 60 * 15 && g.Phase == Phase.Play; t++)
        {
            g.Idle(1);
            bounced |= cat.InShaft;
        }
        Assert.True(bounced);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void Loot_scores_and_two_of_a_kind_in_a_row_pay_double()
    {
        var g = Game();
        g.PutMouse(4, 700);
        g.PutItem(4, 760, Loot.Telly);
        g.PutItem(4, 820, Loot.Telly);
        g.PutItem(4, 880, Loot.Radio);
        g.Run(60, Right);
        Assert.Equal(200 + 400 + 100, g.Score);
        Assert.Equal(Loot.Radio, g.LastTaken);
    }

    [Fact]
    public void Taking_back_the_last_of_the_loot_clears_the_round_with_a_bonus()
    {
        var g = Game(2);
        g.Items.Clear();
        g.PutMouse(4, 700);
        g.PutItem(4, 760, Loot.Painting);
        g.Run(30, Right);
        Assert.Equal(Phase.RoundClear, g.Phase);
        Assert.Equal(300 + 2000, g.Score);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 2);
        Assert.Equal(3, g.Round);
        Assert.Equal(10, g.ItemsLeft);
    }

    [Fact]
    public void After_a_while_the_cats_hurry()
    {
        var g = Game(1);
        double speed = g.CatSpeed;
        g.SetRoundTime(GameEngine.HurryAfter + 1);
        g.Idle(1);
        Assert.True(g.Hurry);
        Assert.True(g.CatSpeed > speed);
    }

    // ───────────────────────────── rounds and mice ─────────────────────────────

    [Fact]
    public void Every_round_lays_out_ten_things_two_of_each_and_doors_some_of_them_power_doors()
    {
        foreach (int round in new[] { 1, 2, 3, 6 })
        {
            var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(round);
            Assert.Equal(10, g.Items.Count);
            Assert.All(Enum.GetValues<Loot>(), k => Assert.Equal(2, g.Items.Count(i => i.Kind == k)));
            Assert.All(g.Items, i => Assert.Equal(-1, Field.ShaftAt(i.X)));
            Assert.True(g.Doors.Count >= 3);
            Assert.Contains(g.Doors, d => d.Power);
            Assert.Equal(2 + Math.Min(round, 3), g.Cats.Count);
            Assert.Equal(round >= 2, g.Cats.Any(c => c.Boss));
        }
    }

    [Fact]
    public void A_lost_mouse_starts_again_on_the_ground_floor_and_the_last_ends_the_game()
    {
        var g = Game();
        g.PutMouse(4, 1200);
        g.PutCat(4, 1220);
        g.Idle(2);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 1.5)));
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(800, g.Mouse.X);
        Assert.Equal(Field.Floors - 1, g.Mouse.Floor);
        g.SetLives(1);
        g.PutCat(4, 810);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 1)));
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_mouse_clears_the_first_round()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 150 && g.Round == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Round);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Mouse.X:0.000} {g.Mouse.Y:0.000} {g.Round} {g.Lives} {g.ItemsLeft}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.PutCat(4, 810);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "SQK") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("SQK", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
