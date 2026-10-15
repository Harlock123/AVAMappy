using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMappy.Engine;
using Effect = AVAMappy.Engine.Effect;

namespace AVAMappy.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMappy/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Black = 0xFF101010;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private double _cam;
    private double SX(double x) => x - _cam;

    /// <summary>The wallpaper of each floor, top first.</summary>
    private static readonly (uint Paper, uint Stripe)[] Papers =
    [
        (0xFFF0D8A8, 0xFFE0C090), (0xFFC8E0B8, 0xFFB0CCA0), (0xFFB8D0F0, 0xFFA0B8E0), (0xFFF0C0D0, 0xFFE0A8BC), (0xFFE8E0B0, 0xFFD8CC98),
    ];

    /// <summary>The trampoline's colour for so many bounces in a row: green, blue, yellow, red.</summary>
    public static uint TrampColour(int bounces) => bounces switch { 0 => 0xFF3AC04A, 1 => 0xFF3A7AE0, 2 => 0xFFF0D030, _ => 0xFFE03A3A };

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        _cam = g.Camera;
        DrawHouse(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            foreach (var it in g.Items.Where(i => !i.Taken)) DrawItem(dc, SX(it.X), Field.FloorY[it.Floor], it.Kind, 1);
            foreach (var d in g.Doors) DrawDoor(dc, d);
            foreach (var w in g.Waves) DrawWave(dc, w);
            foreach (var c in g.Cats.Where(c => c.Away <= 0)) DrawCat(dc, c);
            if (g.Mouse.Alive || g.Phase == Phase.Dying) DrawMouse(dc, SX(g.Mouse.X), g.Mouse.Y, g.Mouse.Facing, g.Mouse.Walk, g.Mouse.InShaft, !g.Mouse.Alive, 1);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 12), 24, Yellow, center: true);
    }

    // ───────────────────────────── the mansion ─────────────────────────────

    private void DrawHouse(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0E1426), new Rect(0, 0, Field.Width, Field.Height));
        // The roof, tiled.
        double l = SX(0), r = SX(Field.House);
        Poly(dc, 0xFF8A2A2A, 0xFF4A1010, 2, new Point(l - 20, Field.RoofY), new Point(r + 20, Field.RoofY), new Point(r - 60, Field.Top + 26), new Point(l + 60, Field.Top + 26));
        for (double y = Field.Top + 40; y < Field.RoofY; y += 18) dc.DrawLine(Pen(0xFF6A1A1A, 2), new Point(l, y), new Point(r, y));
        // The floors, room by room.
        for (int f = 0; f < Field.Floors; f++)
        {
            var (paper, stripe) = Papers[f];
            double top = f == 0 ? Field.RoofY : Field.FloorY[f - 1] + 10, bottom = Field.FloorY[f];
            for (int room = 0; room < 5; room++)
            {
                var (lo, hi) = Field.Room(room);
                lo = room == 0 ? 0 : lo;
                hi = room == 4 ? Field.House : hi;
                var rect = new Rect(SX(lo), top, hi - lo, bottom - top);
                dc.FillRectangle(Brush(paper), rect);
                for (double x = lo + 16; x < hi; x += 32) dc.FillRectangle(Brush(stripe), new Rect(SX(x), top, 10, bottom - top));
                // A window, and a wainscot.
                double wx = (lo + hi) / 2 - 30;
                if (hi - lo > 200)
                {
                    dc.DrawRectangle(Brush(0xFF2A3A6A), Pen(0xFFFFFFFF, 3), new Rect(SX(wx), top + 18, 60, 46));
                    dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(SX(wx + 30), top + 18), new Point(SX(wx + 30), top + 64));
                }
                dc.FillRectangle(Brush(0x30000000), new Rect(SX(lo), bottom - 26, hi - lo, 26));
                // The floor boards.
                dc.FillRectangle(Brush(0xFF8A5A2A), new Rect(SX(lo), bottom, hi - lo, 10));
                dc.FillRectangle(Brush(0xFFB07A3A), new Rect(SX(lo), bottom, hi - lo, 3));
            }
        }
        // The house's outer walls.
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(SX(0), Field.RoofY, 16, Field.TrampY - Field.RoofY + 20));
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(SX(Field.House - 16), Field.RoofY, 16, Field.TrampY - Field.RoofY + 20));
        // The shafts and their trampolines.
        for (int s = 0; s < Field.ShaftX.Length; s++)
        {
            double x = SX(Field.ShaftX[s]);
            dc.FillRectangle(Brush(0xFF1A1420), new Rect(x, Field.RoofY, Field.ShaftW, Field.TrampY - Field.RoofY + 20));
            int bounces = g.Mouse.Shaft == s && g.Mouse.InShaft ? g.Mouse.Bounces : 0;
            bool broken = !g.Mouse.Alive && g.Cause == "THE TRAMPOLINE BROKE" && g.Mouse.Shaft == s;
            double sag = g.Cats.Concat<Walker>([g.Mouse]).Any(w => w.InShaft && w.Shaft == s && w.Y > Field.TrampY - 8) ? 8 : 0;
            dc.FillRectangle(Brush(0xFF6A6A7A), new Rect(x + 2, Field.TrampY - 4, 6, 24));
            dc.FillRectangle(Brush(0xFF6A6A7A), new Rect(x + Field.ShaftW - 8, Field.TrampY - 4, 6, 24));
            if (broken)
            {
                dc.DrawLine(Pen(TrampColour(3), 5), new Point(x + 6, Field.TrampY), new Point(x + 26, Field.TrampY + 14));
                dc.DrawLine(Pen(TrampColour(3), 5), new Point(x + Field.ShaftW - 6, Field.TrampY), new Point(x + Field.ShaftW - 26, Field.TrampY + 14));
            }
            else
            {
                var mid = new Point(x + Field.ShaftW / 2, Field.TrampY + sag);
                dc.DrawLine(Pen(TrampColour(bounces), 6), new Point(x + 6, Field.TrampY), mid);
                dc.DrawLine(Pen(TrampColour(bounces), 6), mid, new Point(x + Field.ShaftW - 6, Field.TrampY));
            }
        }
        // The cellar under the ground floor.
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(SX(0), Field.TrampY + 20, Field.House, Field.Height - Field.TrampY));
    }

    private void DrawDoor(DrawingContext dc, Door d)
    {
        double x = SX(d.X), y = Field.FloorY[d.Floor];
        if (x < -60 || x > Field.Width + 60) return;
        bool flash = d.Power && (int)(_clock * 6) % 2 == 0;
        uint wood = d.Power ? (flash ? 0xFFFF6AD8 : 0xFFC04AB0) : 0xFF9A5A2A;
        if (d.Power) dc.DrawRectangle(Brush(0x50FF6AD8), null, new Rect(x - 18, y - 104, 36, 104), 6, 6);
        if (!d.Open)
        {
            dc.DrawRectangle(Brush(wood), Pen(0xFF4A2A10, 2), new Rect(x - 7, y - 96, 14, 96));
            dc.DrawEllipse(Brush(Yellow), null, new Point(x + 3, y - 46), 2.5, 2.5);
            return;
        }
        // Swung open, seen edge-on as a panel at an angle.
        double w = 42 * d.Side;
        Poly(dc, Darker(wood), 0xFF4A2A10, 2, new Point(x, y - 96), new Point(x + w, y - 104), new Point(x + w, y + 6), new Point(x, y));
    }

    private static uint Darker(uint c)
    {
        uint r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
        return (c & 0xFF000000) | (r * 3 / 4) << 16 | (g * 3 / 4) << 8 | (b * 3 / 4);
    }

    private void DrawWave(DrawingContext dc, Wave w)
    {
        double x = SX(w.X), y = Field.FloorY[w.Floor];
        for (int i = 0; i < 4; i++)
        {
            double xx = x - w.Dir * i * 14;
            uint a = (uint)(200 - i * 45) << 24;
            var pts = Enumerable.Range(0, 9).Select(k => new Point(xx + Math.Sin(_clock * 30 + k + i) * 6, y - 100 + k * 12.5)).ToArray();
            for (int k = 0; k + 1 < pts.Length; k++) dc.DrawLine(Pen(a | 0x00FF8AE8, 4), pts[k], pts[k + 1]);
        }
    }

    // ───────────────────────────── loot ─────────────────────────────

    private static void DrawItem(DrawingContext dc, double x, double floorY, Loot kind, double s)
    {
        var b = new Point(x, floorY);
        switch (kind)
        {
            case Loot.Radio:
                dc.DrawRectangle(Brush(0xFFC0503A), Pen(0xFF5A1A10, 1.5), new Rect(b.X - 18 * s, b.Y - 26 * s, 36 * s, 24 * s), 4, 4);
                dc.DrawEllipse(Brush(0xFF3A2A2A), null, new Point(b.X - 7 * s, b.Y - 14 * s), 7 * s, 7 * s);
                dc.FillRectangle(Brush(0xFFF0E0A0), new Rect(b.X + 3 * s, b.Y - 20 * s, 11 * s, 5 * s));
                dc.DrawLine(Pen(0xFF8A8A9A, 2 * s), new Point(b.X + 12 * s, b.Y - 26 * s), new Point(b.X + 20 * s, b.Y - 42 * s));
                break;
            case Loot.Telly:
                dc.DrawRectangle(Brush(0xFF6A4A2A), Pen(0xFF2A1A0A, 1.5), new Rect(b.X - 22 * s, b.Y - 38 * s, 44 * s, 34 * s), 5, 5);
                dc.DrawRectangle(Brush(0xFF7AB0D8), null, new Rect(b.X - 17 * s, b.Y - 33 * s, 30 * s, 24 * s), 4, 4);
                dc.DrawLine(Pen(0xFF8A8A9A, 2 * s), new Point(b.X - 4 * s, b.Y - 38 * s), new Point(b.X - 14 * s, b.Y - 52 * s));
                dc.DrawLine(Pen(0xFF8A8A9A, 2 * s), new Point(b.X + 4 * s, b.Y - 38 * s), new Point(b.X + 14 * s, b.Y - 52 * s));
                dc.FillRectangle(Brush(0xFF2A1A0A), new Rect(b.X - 18 * s, b.Y - 4 * s, 6 * s, 4 * s));
                dc.FillRectangle(Brush(0xFF2A1A0A), new Rect(b.X + 12 * s, b.Y - 4 * s, 6 * s, 4 * s));
                break;
            case Loot.Painting:
                dc.DrawRectangle(Brush(0xFFE0B040), Pen(0xFF8A6A10, 1.5), new Rect(b.X - 22 * s, b.Y - 46 * s, 44 * s, 36 * s));
                dc.FillRectangle(Brush(0xFF7AC0E8), new Rect(b.X - 17 * s, b.Y - 41 * s, 34 * s, 26 * s));
                Poly(dc, 0xFF3A9A4A, null, 0, new Point(b.X - 17 * s, b.Y - 15 * s), new Point(b.X - 4 * s, b.Y - 32 * s), new Point(b.X + 6 * s, b.Y - 22 * s), new Point(b.X + 17 * s, b.Y - 30 * s), new Point(b.X + 17 * s, b.Y - 15 * s));
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(b.X + 9 * s, b.Y - 35 * s), 4 * s, 4 * s);
                dc.DrawLine(Pen(0xFF8A6A10, 2 * s), new Point(b.X - 8 * s, b.Y - 10 * s), new Point(b.X - 14 * s, b.Y));
                dc.DrawLine(Pen(0xFF8A6A10, 2 * s), new Point(b.X + 8 * s, b.Y - 10 * s), new Point(b.X + 14 * s, b.Y));
                break;
            case Loot.Computer:
                dc.DrawRectangle(Brush(0xFFD8D0C0), Pen(0xFF6A6050, 1.5), new Rect(b.X - 18 * s, b.Y - 44 * s, 36 * s, 28 * s), 3, 3);
                dc.FillRectangle(Brush(0xFF1A4A2A), new Rect(b.X - 14 * s, b.Y - 40 * s, 28 * s, 20 * s));
                for (int k = 0; k < 3; k++) dc.FillRectangle(Brush(0xFF6AF08A), new Rect(b.X - 11 * s, b.Y - 37 * s + k * 5 * s, (8 + k * 5) * s, 2 * s));
                dc.DrawRectangle(Brush(0xFFC8C0B0), Pen(0xFF6A6050, 1.5), new Rect(b.X - 22 * s, b.Y - 12 * s, 44 * s, 10 * s), 2, 2);
                break;
            default:
                dc.DrawRectangle(Brush(0xFF6A7080), Pen(0xFF2A3040, 2), new Rect(b.X - 20 * s, b.Y - 40 * s, 40 * s, 40 * s), 4, 4);
                dc.DrawEllipse(Brush(0xFFC0C8D8), Pen(0xFF2A3040, 1.5), new Point(b.X, b.Y - 21 * s), 9 * s, 9 * s);
                dc.DrawLine(Pen(0xFF2A3040, 2 * s), new Point(b.X, b.Y - 21 * s), new Point(b.X + 5 * s, b.Y - 26 * s));
                dc.FillRectangle(Brush(0xFF2A3040), new Rect(b.X + 12 * s, b.Y - 30 * s, 4 * s, 16 * s));
                break;
        }
    }

    // ───────────────────────────── the mouse and the cats ─────────────────────────────

    private void DrawMouse(DrawingContext dc, double x, double feet, int facing, double walk, bool bouncing, bool down, double s)
    {
        double f = facing;
        double step = bouncing ? 0 : Math.Sin(walk * 0.12) * 4;
        // The tail.
        dc.DrawLine(Pen(0xFFD8A0A8, 2.5 * s), new Point(x - f * 10 * s, feet - 14 * s), new Point(x - f * 24 * s, feet - 22 * s - Math.Sin(_clock * 6) * 4));
        // Legs and boots.
        dc.DrawLine(Pen(0xFF2A3A8A, 6 * s), new Point(x - 4 * s, feet - 16 * s), new Point(x - 5 * s + step, feet - 3 * s));
        dc.DrawLine(Pen(0xFF2A3A8A, 6 * s), new Point(x + 4 * s, feet - 16 * s), new Point(x + 5 * s - step, feet - 3 * s));
        dc.DrawEllipse(Brush(Black), null, new Point(x - 5 * s + step + f * 2, feet - 2 * s), 5 * s, 3 * s);
        dc.DrawEllipse(Brush(Black), null, new Point(x + 5 * s - step + f * 2, feet - 2 * s), 5 * s, 3 * s);
        // A blue police coat with a badge.
        dc.DrawRectangle(Brush(0xFF3A5AD0), Pen(0xFF1A2A70, 1.5), new Rect(x - 10 * s, feet - 36 * s, 20 * s, 22 * s), 5, 5);
        dc.DrawEllipse(Brush(Yellow), null, new Point(x + f * 4 * s, feet - 30 * s), 2.5 * s, 2.5 * s);
        // Arms: up when bouncing.
        double armY = bouncing ? -18 : 6;
        dc.DrawLine(Pen(0xFF3A5AD0, 4.5 * s), new Point(x + f * 8 * s, feet - 32 * s), new Point(x + f * 15 * s, feet - (30 - armY) * s));
        // The head: grey with big round ears and a pink nose, under a police cap.
        var head = new Point(x + f * 2 * s, feet - 46 * s);
        dc.DrawEllipse(Brush(0xFFB0B0BC), Pen(0xFF5A5A6A, 1), new Point(head.X - 9 * s, head.Y - 9 * s), 7 * s, 7 * s);
        dc.DrawEllipse(Brush(0xFFB0B0BC), Pen(0xFF5A5A6A, 1), new Point(head.X + 9 * s, head.Y - 9 * s), 7 * s, 7 * s);
        dc.DrawEllipse(Brush(0xFFF0B0C0), null, new Point(head.X - 9 * s, head.Y - 9 * s), 3.5 * s, 3.5 * s);
        dc.DrawEllipse(Brush(0xFFF0B0C0), null, new Point(head.X + 9 * s, head.Y - 9 * s), 3.5 * s, 3.5 * s);
        dc.DrawEllipse(Brush(0xFFC8C8D4), Pen(0xFF5A5A6A, 1), head, 10 * s, 9 * s);
        dc.DrawEllipse(Brush(0xFFFF8AA0), null, new Point(head.X + f * 10 * s, head.Y + 2 * s), 3 * s, 2.5 * s);
        if (down)
        {
            dc.DrawLine(Pen(Black, 1.5), new Point(head.X + f * 2 * s, head.Y - 3 * s), new Point(head.X + f * 6 * s, head.Y + 1 * s));
            dc.DrawLine(Pen(Black, 1.5), new Point(head.X + f * 6 * s, head.Y - 3 * s), new Point(head.X + f * 2 * s, head.Y + 1 * s));
        }
        else dc.DrawEllipse(Brush(Black), null, new Point(head.X + f * 4 * s, head.Y - 1 * s), 1.8 * s, 2.2 * s);
        dc.DrawRectangle(Brush(0xFF1A2A70), null, new Rect(head.X - 9 * s, head.Y - 13 * s, 18 * s, 6 * s), 2, 2);
        dc.FillRectangle(Brush(0xFF1A2A70), new Rect(head.X - 4 * s + f * 4 * s, head.Y - 8 * s, 10 * s, 2.5 * s));
        dc.DrawEllipse(Brush(Yellow), null, new Point(head.X, head.Y - 10 * s), 1.8 * s, 1.8 * s);
    }

    private void DrawCat(DrawingContext dc, Cat c)
    {
        double x = SX(c.X), feet = c.Y;
        if (x < -60 || x > Field.Width + 60) return;
        double s = c.Boss ? 1.3 : 1;
        double f = c.Facing;
        uint fur = c.Boss ? 0xFFE07A2A : 0xFFF0A0C0, dark = c.Boss ? 0xFF8A3A10 : 0xFFB0607A;
        // Flattened by a door.
        double squash = c.Stunned > 0 ? 0.45 : 1;
        double step = c.InShaft ? 0 : Math.Sin(c.Walk * 0.1) * 4;
        double H(double v) => v * squash;
        dc.DrawLine(Pen(dark, 4 * s), new Point(x - f * 10 * s, feet - H(20) * s), new Point(x - f * 22 * s, feet - H(40) * s + Math.Sin(_clock * 5 + c.X) * 4));
        dc.DrawLine(Pen(dark, 6 * s), new Point(x - 4 * s, feet - H(18) * s), new Point(x - 5 * s + step, feet - 2 * s));
        dc.DrawLine(Pen(dark, 6 * s), new Point(x + 4 * s, feet - H(18) * s), new Point(x + 5 * s - step, feet - 2 * s));
        dc.DrawEllipse(Brush(fur), Pen(dark, 1.5), new Point(x, feet - H(28) * s), 12 * s, H(13) * s);
        if (c.Boss)
            for (int k = -1; k <= 1; k++) dc.DrawLine(Pen(dark, 2.5), new Point(x + k * 6, feet - H(38) * s), new Point(x + k * 6, feet - H(22) * s));
        var head = new Point(x + f * 4 * s, feet - H(48) * s);
        Poly(dc, fur, dark, 1.2, new Point(head.X - 11 * s, head.Y - 4 * s), new Point(head.X - 9 * s, head.Y - 18 * s), new Point(head.X - 2 * s, head.Y - 8 * s));
        Poly(dc, fur, dark, 1.2, new Point(head.X + 11 * s, head.Y - 4 * s), new Point(head.X + 9 * s, head.Y - 18 * s), new Point(head.X + 2 * s, head.Y - 8 * s));
        dc.DrawEllipse(Brush(fur), Pen(dark, 1.5), head, 12 * s, H(11) * s);
        dc.DrawEllipse(Brush(White), null, new Point(head.X + f * 3 * s - 3 * s, head.Y - 2 * s), 3.2 * s, 3.6 * s);
        dc.DrawEllipse(Brush(White), null, new Point(head.X + f * 3 * s + 4 * s, head.Y - 2 * s), 3.2 * s, 3.6 * s);
        dc.DrawEllipse(Brush(Black), null, new Point(head.X + f * 4 * s - 3 * s, head.Y - 2 * s), 1.6 * s, 2 * s);
        dc.DrawEllipse(Brush(Black), null, new Point(head.X + f * 4 * s + 4 * s, head.Y - 2 * s), 1.6 * s, 2 * s);
        dc.DrawEllipse(Brush(0xFFE04A6A), null, new Point(head.X + f * 9 * s, head.Y + 3 * s), 2.4 * s, 2 * s);
        for (int k = -1; k <= 1; k += 2)
        {
            dc.DrawLine(Pen(dark, 1), new Point(head.X + f * 8 * s, head.Y + 4 * s), new Point(head.X + f * 20 * s, head.Y + (4 + k * 4) * s));
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var p = new Point(SX(e.X), e.Y);
        switch (e.Kind)
        {
            case EffectKind.Stars:
                // Stars round a flattened cat's head.
                for (int i = 0; i < 4; i++)
                {
                    double a = _clock * 5 + i * Math.PI / 2;
                    var c = new Point(p.X + Math.Cos(a) * 18, p.Y + Math.Sin(a) * 6);
                    Poly(dc, Yellow, null, 0, new Point(c.X, c.Y - 5), new Point(c.X + 2, c.Y - 1), new Point(c.X + 6, c.Y), new Point(c.X + 2, c.Y + 1), new Point(c.X, c.Y + 5), new Point(c.X - 2, c.Y + 1), new Point(c.X - 6, c.Y), new Point(c.X - 2, c.Y - 1));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 30), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(p.X, p.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0E1A), new Rect(0, 0, Field.Width, Field.Top));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        Text(dc, $"ROUND {g.Round}", new Point(320, 16), 22, Dim);
        if (g.Hurry && (int)(_clock * 3) % 2 == 0) Text(dc, "HURRY!", new Point(320, 44), 28, Red);
        // What is still to take back.
        Text(dc, "LOOT", new Point(450, 16), 22, Dim);
        int i = 0;
        foreach (var it in g.Items.Where(i => !i.Taken).OrderBy(i => i.Kind))
            DrawItem(dc, 462 + i++ * 34, 60, it.Kind, 0.55);
        Text(dc, "MICE", new Point(860, 16), 22, Dim);
        for (int k = 0; k < Math.Min(5, g.Lives - (g.Mouse.Alive ? 1 : 0)); k++)
        {
            var c = new Point(872 + k * 28, 46);
            dc.DrawEllipse(Brush(0xFFB0B0BC), null, new Point(c.X - 7, c.Y - 7), 5, 5);
            dc.DrawEllipse(Brush(0xFFB0B0BC), null, new Point(c.X + 7, c.Y - 7), 5, 5);
            dc.DrawEllipse(Brush(0xFFC8C8D4), null, c, 8, 7);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 340, 250, 680, g.SubMessage != null ? 110 : 70));
        Text(dc, msg, new Point(cx, 282), 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 332), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC80E1426), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA MAPPY", new Point(cx, 140), 120, 0xFF7AB0FF, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 206), 28, Dim, center: true);
        // The mouse bouncing beside the title.
        double bounce = Math.Abs(Math.Sin(_clock * 3)) * 70;
        DrawMouse(dc, cx - 330, 210 - bounce, 1, 0, true, false, 1.6);
        dc.DrawLine(Pen(TrampColour((int)(_clock / 1.05) % 4), 6), new Point(cx - 370, 218), new Point(cx - 290, 218));
        string[] lines =
        [
            "TAKE BACK THE STOLEN LOOT FROM THE CATS' MANSION",
            "BOUNCE THE SHAFTS TO CHANGE FLOOR - FOUR BOUNCES AND IT BREAKS",
            "OPEN A DOOR IN A CAT'S FACE - A FLASHING DOOR SWEEPS THE ROOM",
            "TWO OF A KIND IN A ROW PAY DOUBLE",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 272 + i * 34), 26, White, center: true);
        (Loot Kind, string Name, string Points)[] table =
        [
            (Loot.Radio, "RADIO", "100"), (Loot.Telly, "TELLY", "200"), (Loot.Painting, "PAINTING", "300"), (Loot.Computer, "COMPUTER", "400"), (Loot.Safe, "SAFE", "500"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = 160 + i * 176;
            DrawItem(dc, x, 500, table[i].Kind, 1.2);
            Text(dc, table[i].Name, new Point(x, 522), 22, Dim, center: true);
            Text(dc, table[i].Points, new Point(x, 546), 26, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 624), 46, Yellow, center: true);
        Text(dc, "LEFT / RIGHT: WALK, AND STEP OFF A TRAMPOLINE    SPACE: OPEN AND SHUT DOORS", new Point(cx, 690), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD80E1426), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP MICE", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD80E1426), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
