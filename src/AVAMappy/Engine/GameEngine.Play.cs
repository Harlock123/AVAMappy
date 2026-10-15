namespace AVAMappy.Engine;

public sealed partial class GameEngine
{
    public const double MouseSpeed = 190, Gravity = 1500, BounceSpeed = 1260, ExitWindow = 15;
    public const int BreakAt = 4;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, ClearSeconds = 3.2;
    public const double StunSeconds = 3, AwaySeconds = 6, WaveSpeed = 560, HurryAfter = 75;
    public const int KnockPoints = 200, WavePoints = 200;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 40000;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Mouse Mouse { get; } = new();
    public List<Cat> Cats { get; } = [];
    public List<Door> Doors { get; } = [];
    public List<Item> Items { get; } = [];
    public List<Wave> Waves { get; } = [];
    public MousePilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoundTime { get; private set; }
    public bool Hurry => RoundTime > HurryAfter;
    /// <summary>The kind of the last thing taken, for the pair bonus.</summary>
    public Loot? LastTaken { get; private set; }
    /// <summary>For tests: no cats unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    public double CatSpeed => Math.Min(210, 125 + 12 * (Round - 1)) * (Hurry ? 1.35 : 1);
    public int ItemsLeft => Items.Count(i => !i.Taken);
    public double Camera => Math.Clamp(Mouse.X - Field.Width / 2, 0, Field.House - Field.Width);

    private int _nextExtra;
    private bool _doorPrev, _hurried;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.RoundStart);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.RoundStart);
        Say($"ROUND {round}", round == 1 ? "TAKE BACK ALL THE LOOT" : Cats.Any(c => c.Boss) ? "THE BIG TABBY IS ABOUT" : null, StartSeconds);
        Play(Sfx.RoundStart);
    }

    /// <summary>A fresh mansion: doors in the rooms (some of them power doors), ten things to take back, and the cats.</summary>
    private void LayOut()
    {
        Doors.Clear();
        Items.Clear();
        Waves.Clear();
        Effects.Clear();
        var rng = new Random(Round * 53 + 11);
        for (int f = 0; f < Field.Floors; f++)
            for (int r = 1; r <= 3; r++)
            {
                if (rng.NextDouble() > 0.5) continue;
                var (lo, hi) = Field.Room(r);
                Doors.Add(new Door { Floor = f, X = (lo + hi) / 2 + (rng.Next(2) == 0 ? -1 : 1) * 60 });
            }
        // A few of them are power doors.
        foreach (var d in Doors.OrderBy(_ => rng.Next()).Take(Math.Min(Doors.Count, 3))) d.Power = true;
        // Two of each kind of loot, spread over the house.
        foreach (Loot kind in Enum.GetValues<Loot>())
            for (int n = 0; n < 2; n++)
                for (int tries = 0; tries < 200; tries++)
                {
                    int f = rng.Next(Field.Floors), r = rng.Next(5);
                    var (lo, hi) = Field.Room(r);
                    double x = lo + 30 + rng.NextDouble() * (hi - lo - 60);
                    if (Doors.Any(d => d.Floor == f && Math.Abs(d.X - x) < 50) || Items.Any(i => i.Floor == f && Math.Abs(i.X - x) < 70)) continue;
                    if (f == Field.Floors - 1 && Math.Abs(x - 800) < 80) continue;
                    Items.Add(new Item { Floor = f, X = x, Kind = kind });
                    break;
                }
        LastTaken = null;
        RoundTime = 0;
        _hurried = false;
        Reset();
    }

    /// <summary>The mouse on the ground floor in the middle; the cats in from the top.</summary>
    private void Reset()
    {
        var m = Mouse;
        m.X = 800;
        m.Floor = Field.Floors - 1;
        m.Y = Field.FloorY[m.Floor];
        m.Vy = 0;
        m.Shaft = -1;
        m.Bounces = 0;
        m.Facing = 1;
        m.Alive = true;
        Cats.Clear();
        Waves.Clear();
        if (Calm) return;
        int n = 2 + Math.Min(Round, 3);
        for (int i = 0; i < n; i++) Cats.Add(NewCat(i % 2 == 0 ? 60 : Field.House - 60, i == n - 1 && Round >= 2));
    }

    private static Cat NewCat(double x, bool boss) => new()
    {
        X = x, Floor = 0, Y = Field.FloorY[0], Boss = boss, Facing = x < Field.House / 2 ? 1 : -1, Wait = 0.5,
    };

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.RoundStart:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                if (Hurry && !_hurried) { _hurried = true; Say("HURRY!", "THE CATS ARE GETTING FASTER", 2); Play(Sfx.Hurry); }
                MoveMouse(c, dt);
                if (Phase != Phase.Play) break;
                MoveCats(dt);
                MoveWaves(dt);
                Catch();
                if (Phase == Phase.Play && ItemsLeft == 0 && Items.Count > 0) RoundDone();
                break;
            case Phase.Dying:
                if (Mouse.InShaft) Fall(Mouse, dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Reset();
                    SetPhase(Phase.RoundStart);
                    PhaseTime = StartSeconds - 1;
                }
                break;
            case Phase.RoundClear:
                if (PhaseTime > ClearSeconds) BeginRound(Round + 1);
                break;
        }
        Waves.RemoveAll(w => w.Dead);
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    private void MoveMouse(Controls c, double dt)
    {
        var m = Mouse;
        bool press = c.Door && !_doorPrev;
        _doorPrev = c.Door;
        if (m.InShaft)
        {
            if (Bounce(m, dt)) { Play(Sfx.Bounce); if (m.Bounces >= BreakAt) { Snapped(); return; } }
            // Hold a way as a floor goes by to step off onto it (once he has bounced: no hopping straight across).
            if (c.Dx != 0 && m.Bounces >= 1 && ExitFloor(m) is int f)
            {
                StepOff(m, f, c.Dx);
                Play(Sfx.Step);
            }
            return;
        }
        if (c.Dx != 0)
        {
            m.Facing = c.Dx;
            double step = c.Dx * MouseSpeed * dt;
            m.X = Blocked(m.Floor, m.X, m.X + step);
            m.Walk += Math.Abs(step);
            Enter(m);
        }
        if (press) UseDoor();
        Take();
    }

    /// <summary>Where a walker going from x to x2 on a floor ends up: closed doors stop it, and so do the house's ends.</summary>
    private double Blocked(int floor, double x, double x2)
    {
        foreach (var d in Doors)
        {
            if (d.Floor != floor || d.Open) continue;
            if (x <= d.X - 14 && x2 > d.X - 14) x2 = d.X - 14;
            if (x >= d.X + 14 && x2 < d.X + 14) x2 = d.X + 14;
        }
        return Math.Clamp(x2, 20, Field.House - 20);
    }

    /// <summary>Walking off the edge of a floor into a shaft: down it drops, onto the trampoline.</summary>
    private static void Enter(Walker w)
    {
        int s = Field.ShaftAt(w.X);
        if (s < 0) return;
        w.Floor = -1;
        w.Shaft = s;
        w.X = Field.ShaftMid(s);
        w.Vy = 0;
        w.Bounces = 0;
    }

    /// <summary>A step of bouncing; true if it landed on the trampoline this step.</summary>
    private static bool Bounce(Walker w, double dt)
    {
        w.Vy += Gravity * dt;
        w.Y += w.Vy * dt;
        if (w.Y < Field.FloorY[0] - 40) { w.Y = Field.FloorY[0] - 40; w.Vy = Math.Max(w.Vy, 0); }
        if (w.Y < Field.TrampY || w.Vy <= 0) return false;
        w.Y = Field.TrampY;
        w.Vy = -BounceSpeed;
        w.Bounces++;
        return true;
    }

    private static void Fall(Walker w, double dt)
    {
        w.Vy += Gravity * dt;
        w.Y = Math.Min(Field.Height + 40, w.Y + w.Vy * dt);
    }

    /// <summary>The floor a bouncing walker is level with just now, if any.</summary>
    public static int? ExitFloor(Walker w)
    {
        for (int f = 0; f < Field.Floors; f++)
            if (Math.Abs(w.Y - Field.FloorY[f]) < ExitWindow) return f;
        return null;
    }

    private static void StepOff(Walker w, int floor, int side)
    {
        w.Floor = floor;
        w.Y = Field.FloorY[floor];
        w.Vy = 0;
        w.Bounces = 0;
        w.Facing = side;
        w.X = side < 0 ? Field.ShaftX[w.Shaft] - 14 : Field.ShaftX[w.Shaft] + Field.ShaftW + 14;
        w.Shaft = -1;
    }

    /// <summary>The door the mouse is at, facing it: shut, it opens away from him (flattening cats behind it); open, it shuts.</summary>
    private void UseDoor()
    {
        var m = Mouse;
        var d = Doors.Where(d => d.Floor == m.Floor && Math.Abs(d.X - m.X) < 42 && (d.X - m.X) * m.Facing >= -8).OrderBy(d => Math.Abs(d.X - m.X)).FirstOrDefault();
        if (d == null) return;
        if (d.Open) { d.Open = false; d.Swing = 0; Play(Sfx.Door); return; }
        d.Open = true;
        d.Side = m.Facing;
        d.Swing = 0;
        Play(Sfx.Door);
        foreach (var cat in Cats)
        {
            if (cat.InShaft || cat.Floor != d.Floor || cat.Away > 0 || cat.Stunned > 0) continue;
            double beyond = (cat.X - d.X) * d.Side;
            if (beyond < -6 || beyond > 80) continue;
            cat.Stunned = StunSeconds;
            cat.X = Blocked(cat.Floor, cat.X, cat.X + d.Side * 40);
            AddScore(cat.Boss ? KnockPoints * 2 : KnockPoints, cat.X, cat.Y - 60);
            Effects.Add(new Effect { Kind = EffectKind.Stars, X = cat.X, Y = cat.Y - 50, Life = StunSeconds });
            Play(Sfx.Knock);
        }
        if (d.Power)
        {
            // The power door sends a wave down the room that sweeps away every cat in front of it.
            d.Power = false;
            var (lo, hi) = Field.Room(Field.RoomAt(d.X));
            Waves.Add(new Wave { Floor = d.Floor, X = d.X, Dir = d.Side, Lo = lo - 40, Hi = hi + 40 });
            Play(Sfx.Wave);
        }
    }

    private void MoveWaves(double dt)
    {
        foreach (var w in Waves)
        {
            double x0 = w.X;
            w.X += w.Dir * WaveSpeed * dt;
            foreach (var cat in Cats)
            {
                if (cat.InShaft || cat.Floor != w.Floor || cat.Away > 0) continue;
                double lo = Math.Min(x0, w.X) - 20, hi = Math.Max(x0, w.X) + 20;
                if (cat.X < lo || cat.X > hi) continue;
                int points = WavePoints << Math.Min(w.Swept, 5);
                if (cat.Boss) points *= 2;
                w.Swept++;
                cat.Away = AwaySeconds;
                cat.Stunned = 0;
                AddScore(points, cat.X, cat.Y - 60, 0xFFFFE14A);
                Play(Sfx.Sweep);
            }
            if (w.X < w.Lo || w.X > w.Hi) w.Dead = true;
        }
    }

    private void Take()
    {
        var m = Mouse;
        foreach (var it in Items)
        {
            if (it.Taken || it.Floor != m.Floor || Math.Abs(it.X - m.X) > 22) continue;
            it.Taken = true;
            bool pair = LastTaken == it.Kind;
            AddScore(it.Points * (pair ? 2 : 1), it.X, m.Y - 70, pair ? 0xFFFFE14A : 0xFFF2F2F2);
            if (pair) Effects.Add(new Effect { Kind = EffectKind.Word, X = it.X, Y = m.Y - 110, Text = "PAIR x2", Colour = 0xFFFFE14A, Life = 1.2 });
            LastTaken = it.Kind;
            Play(pair ? Sfx.Pair : Sfx.Loot);
        }
    }

    // ───────────────────────────── the cats ─────────────────────────────

    /// <summary>The floor the mouse is on, or nearest to while bouncing.</summary>
    public int MouseFloor => Mouse.InShaft ? Enumerable.Range(0, Field.Floors).OrderBy(f => Math.Abs(Field.FloorY[f] - Mouse.Y)).First() : Mouse.Floor;

    /// <summary>The shaft to take from a room towards another room, or (for the same room) the nearer of its two.</summary>
    public static int ShaftToward(int fromRoom, int toRoom, double x)
    {
        if (toRoom > fromRoom) return fromRoom;
        if (toRoom < fromRoom) return fromRoom - 1;
        int left = fromRoom - 1, right = fromRoom < Field.ShaftX.Length ? fromRoom : -1;
        if (left < 0) return right;
        if (right < 0) return left;
        return Math.Abs(Field.ShaftMid(left) - x) < Math.Abs(Field.ShaftMid(right) - x) ? left : right;
    }

    private void MoveCats(double dt)
    {
        var m = Mouse;
        int mf = MouseFloor, mr = Field.RoomAt(m.X);
        foreach (var cat in Cats)
        {
            if (cat.Away > 0)
            {
                cat.Away -= dt;
                // Back in from the top corner.
                if (cat.Away <= 0)
                {
                    var fresh = NewCat(_rng.Next(2) == 0 ? 60 : Field.House - 60, cat.Boss);
                    cat.X = fresh.X; cat.Y = fresh.Y; cat.Floor = 0; cat.Shaft = -1; cat.Vy = 0; cat.Facing = fresh.Facing; cat.Wait = 0;
                }
                continue;
            }
            if (cat.Stunned > 0) { cat.Stunned -= dt; continue; }
            double speed = CatSpeed * (cat.Boss ? 0.8 : 1);
            if (cat.InShaft)
            {
                Bounce(cat, dt);
                // Off at the mouse's floor, towards the mouse; or anywhere after a few bounces.
                if (cat.Bounces >= 1 && ExitFloor(cat) is int f && (f == cat.Want || cat.Bounces >= 3))
                {
                    int side = Math.Sign(m.X - Field.ShaftMid(cat.Shaft));
                    if (side == 0) side = _rng.Next(2) == 0 ? -1 : 1;
                    StepOff(cat, f, side);
                }
                continue;
            }
            int room = Field.RoomAt(cat.X);
            double targetX;
            if (cat.Floor == mf && room == mr && !m.InShaft) targetX = m.X;
            else
            {
                // To the mouse's own shaft if it is bouncing in one at the edge of this room; otherwise the way towards it.
                bool besideHisShaft = m.InShaft && (m.Shaft == room || m.Shaft == room - 1);
                int s = besideHisShaft ? m.Shaft : ShaftToward(room, mr, cat.X);
                targetX = Field.ShaftMid(s);
                cat.Want = mf;
            }
            // Now and then, while waiting at a shut door, it gives up and goes the other way.
            if (cat.Wait < 0) { cat.Wait += dt; targetX = cat.X - cat.Facing * 100; }
            int dir = Math.Sign(targetX - cat.X);
            if (dir == 0) continue;
            cat.Facing = dir;
            double before = cat.X;
            cat.X = Blocked(cat.Floor, cat.X, cat.X + dir * speed * dt);
            cat.Walk += Math.Abs(cat.X - before);
            if (Math.Abs(cat.X - before) < 1e-6 && cat.Wait >= 0)
            {
                cat.Wait += dt;
                if (cat.Wait > 1.5) cat.Wait = -1.6;
            }
            else if (cat.Wait > 0) cat.Wait = 0;
            Enter(cat);
            if (cat.InShaft) cat.Want = mf;
        }
    }

    private void Catch()
    {
        var m = Mouse;
        if (m.InShaft) return;
        foreach (var cat in Cats)
        {
            if (cat.InShaft || cat.Floor != m.Floor || cat.Stunned > 0 || cat.Away > 0) continue;
            if (Math.Abs(cat.X - m.X) > 28) continue;
            Lose(cat.Boss ? "CAUGHT BY THE BIG TABBY" : "CAUGHT BY A CAT");
            return;
        }
    }

    private void Snapped()
    {
        Play(Sfx.Snap);
        Lose("THE TRAMPOLINE BROKE");
        Mouse.Vy = 200;
    }

    private void Lose(string cause)
    {
        Cause = cause;
        Lives--;
        Mouse.Alive = false;
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "MOUSE" : "MICE")} LEFT" : null, DyingSeconds);
    }

    private void RoundDone()
    {
        int bonus = 1000 * Round;
        AddScore(bonus, Mouse.X, Mouse.Y - 100, 0xFFFFE14A);
        Say("ALL THE LOOT IS BACK!", $"ROUND {Round} BONUS {bonus}", ClearSeconds);
        Play(Sfx.RoundClear);
        Music(false);
        SetPhase(Phase.RoundClear);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round.</summary>
    internal void Go(int round = 1)
    {
        StartGame();
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>An empty house: no doors, loot or cats.</summary>
    internal void Bare()
    {
        Doors.Clear();
        Items.Clear();
        Cats.Clear();
        Waves.Clear();
    }

    internal void PutMouse(int floor, double x)
    {
        Mouse.Floor = floor;
        Mouse.Y = Field.FloorY[floor];
        Mouse.X = x;
        Mouse.Vy = 0;
        Mouse.Shaft = -1;
    }

    internal Cat PutCat(int floor, double x, bool boss = false)
    {
        var c = new Cat { Floor = floor, Y = Field.FloorY[floor], X = x, Boss = boss, Facing = x < Mouse.X ? 1 : -1 };
        Cats.Add(c);
        return c;
    }

    internal Door PutDoor(int floor, double x, bool power = false)
    {
        var d = new Door { Floor = floor, X = x, Power = power };
        Doors.Add(d);
        return d;
    }

    internal Item PutItem(int floor, double x, Loot kind)
    {
        var i = new Item { Floor = floor, X = x, Kind = kind };
        Items.Add(i);
        return i;
    }

    internal void SetRoundTime(double t) => RoundTime = t;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
}
