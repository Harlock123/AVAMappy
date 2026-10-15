namespace AVAMappy.Engine;

/// <summary>
/// The computer at the controls, for the demonstration (and the tests). It goes after the nearest loot,
/// bouncing up and down the shafts to reach other floors and rooms; opens a door in a cat's face or a
/// power door at a room full of them; runs for a shaft when a cat closes in with no door between; and
/// never lets a trampoline break under it.
/// </summary>
public sealed class MousePilot
{
    public string Doing { get; private set; } = "";
    /// <summary>The floor it means to step off at, and which way.</summary>
    public int WantFloor { get; private set; } = -1;
    public int WantSide { get; private set; } = 1;

    private bool _pressed;

    public Controls Decide(GameEngine g)
    {
        var m = g.Mouse;
        if (g.Phase != Phase.Play || !m.Alive) return default;
        if (m.InShaft) return InShaft(g);

        int room = Field.RoomAt(m.X);
        var cats = g.Cats.Where(c => !c.InShaft && c.Floor == m.Floor && c.Stunned <= 0 && c.Away <= 0 && Field.RoomAt(c.X) == room).ToList();

        // A power door at hand with cats beyond it: open it on them.
        foreach (var d in g.Doors.Where(d => d.Floor == m.Floor && !d.Open && d.Power && Math.Abs(d.X - m.X) < 40))
        {
            int side = Math.Sign(d.X - m.X) == 0 ? m.Facing : Math.Sign(d.X - m.X);
            if (cats.Any(c => (c.X - d.X) * side > 0)) return Press(side, "power door");
        }
        // Cats closing in.
        foreach (var cat in cats.OrderBy(c => Math.Abs(c.X - m.X)))
        {
            double gap = Math.Abs(cat.X - m.X);
            int toward = Math.Sign(cat.X - m.X);
            var between = g.Doors.FirstOrDefault(d => d.Floor == m.Floor && !d.Open && (d.X - m.X) * toward > 0 && (cat.X - d.X) * toward > 0);
            if (between != null)
            {
                // A shut door between: open it in the cat's face when it is right behind it.
                if (Math.Abs(between.X - m.X) < 40 && Math.Abs(cat.X - between.X) < 75) return Press(toward, "door in its face");
                continue;
            }
            if (gap < 230)
            {
                // Nothing between: run for the shaft on the far side.
                Doing = "run";
                return new Controls(-toward);
            }
        }

        // The nearest loot: straight to it in this room, or by way of a shaft.
        var target = g.Items.Where(i => !i.Taken).OrderBy(i => Cost(m, i)).FirstOrDefault();
        if (target == null) return default;
        int targetRoom = Field.RoomAt(target.X);
        double goal;
        if (target.Floor == m.Floor && targetRoom == room) goal = target.X;
        else
        {
            int s = GameEngine.ShaftToward(room, targetRoom, m.X);
            goal = Field.ShaftMid(s);
            WantFloor = target.Floor;
            WantSide = targetRoom > s ? 1 : -1;
        }
        int dir = Math.Sign(goal - m.X);
        // A shut door in the way: open it.
        var door = g.Doors.FirstOrDefault(d => d.Floor == m.Floor && !d.Open && (d.X - m.X) * dir > 0 && Math.Abs(d.X - m.X) < 40);
        if (door != null) return Press(dir, "open the way");
        Doing = "loot";
        return new Controls(dir);
    }

    /// <summary>
    /// In a shaft: step off at the floor it wants if no cat waits there (once it has bounced); failing that, at
    /// any floor with no cat in the room on the way up; and on the last safe bounce, anywhere at all.
    /// </summary>
    private Controls InShaft(GameEngine g)
    {
        var m = g.Mouse;
        if (m.Bounces < 1 || GameEngine.ExitFloor(m) is not int f) return default;
        bool last = m.Bounces >= GameEngine.BreakAt - 1;
        foreach (int side in new[] { WantSide, -WantSide })
        {
            double x = side < 0 ? Field.ShaftX[m.Shaft] - 14 : Field.ShaftX[m.Shaft] + Field.ShaftW + 14;
            if (!Safe(g, f, x)) continue;
            if (f == WantFloor && side == WantSide) { Doing = "step off"; return new Controls(side); }
            if (m.Bounces >= 2 && m.Vy < 0 || last) { Doing = "step off elsewhere"; return new Controls(side); }
        }
        // About to break it: off anywhere.
        if (last) { Doing = "jump clear"; return new Controls(WantSide); }
        return default;
    }

    /// <summary>No cat on that floor in that room close enough to make it run, with no shut door between.</summary>
    private static bool Safe(GameEngine g, int floor, double x)
    {
        int room = Field.RoomAt(x);
        return !g.Cats.Any(c => !c.InShaft && c.Floor == floor && c.Away <= 0 && c.Stunned <= 0 && Field.RoomAt(c.X) == room &&
                                Math.Abs(c.X - x) < 260 &&
                                !g.Doors.Any(d => d.Floor == floor && !d.Open && (d.X - x) * (c.X - d.X) > 0));
    }

    private static double Cost(Mouse m, Item i)
    {
        int room = Field.RoomAt(m.X), r = Field.RoomAt(i.X);
        if (i.Floor == m.Floor && r == room) return Math.Abs(i.X - m.X);
        return 400 + Math.Abs(i.X - m.X) + Math.Abs(i.Floor - m.Floor) * 60;
    }

    /// <summary>Face a way and press the door button; let go on the next step so the press after counts again.</summary>
    private Controls Press(int side, string doing)
    {
        Doing = doing;
        _pressed = !_pressed;
        return new Controls(_pressed ? 0 : side, _pressed);
    }
}
