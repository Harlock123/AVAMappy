namespace AVAMappy.Engine;

/// <summary>Left or right, and the button that opens and shuts doors.</summary>
public readonly record struct Controls(int Dx = 0, bool Door = false);

/// <summary>
/// The mansion in cross-section: five floors, split into rooms by four trampoline shafts. Positions are
/// pixels across the house (wider than the screen; the camera follows the mouse) and down the screen.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const double House = 1600;
    public const int Floors = 5;
    /// <summary>Where each floor's boards are (feet stand here), top floor first; the roof; the trampolines.</summary>
    public static readonly double[] FloorY = [236, 356, 476, 596, 716];
    public const double RoofY = 150, TrampY = 742;
    /// <summary>The left edge of each shaft, and how wide a shaft is.</summary>
    public static readonly double[] ShaftX = [200, 600, 1000, 1400];
    public const double ShaftW = 74;

    public static double ShaftMid(int s) => ShaftX[s] + ShaftW / 2;

    /// <summary>The shaft a point across the house is over, if any.</summary>
    public static int ShaftAt(double x)
    {
        for (int i = 0; i < ShaftX.Length; i++)
            if (x >= ShaftX[i] && x <= ShaftX[i] + ShaftW) return i;
        return -1;
    }

    /// <summary>Which room (between the shafts) a point is in: 0 to 4.</summary>
    public static int RoomAt(double x)
    {
        int r = 0;
        foreach (double s in ShaftX) if (x > s + ShaftW / 2) r++;
        return r;
    }

    /// <summary>A room's walls: from the house's edge or a shaft to the next.</summary>
    public static (double Lo, double Hi) Room(int r) =>
        (r == 0 ? 20 : ShaftX[r - 1] + ShaftW, r == ShaftX.Length ? House - 20 : ShaftX[r]);
}

/// <summary>A mouse or a cat: on a floor, or bouncing in a shaft.</summary>
public class Walker
{
    public double X, Y, Vy;
    /// <summary>The floor it stands on, or −1 in a shaft.</summary>
    public int Floor;
    public int Shaft = -1;
    public int Facing = 1;
    /// <summary>Bounces on the trampoline since it last stood on a floor.</summary>
    public int Bounces;
    public double Walk;
    public bool InShaft => Floor < 0;
}

public sealed class Mouse : Walker
{
    public bool Alive = true;
}

public sealed class Cat : Walker
{
    /// <summary>The big boss cat: slower, and worth more.</summary>
    public bool Boss;
    /// <summary>Flattened by a door; swept off by a wave and away for a while.</summary>
    public double Stunned, Away;
    /// <summary>The floor it means to step off at, coming up a shaft.</summary>
    public int Want = -1;
    public double Wait;
}

public sealed class Door
{
    public int Floor;
    public double X;
    /// <summary>Open, and which way it swung (−1 left, +1 right).</summary>
    public bool Open;
    public int Side = 1;
    /// <summary>A flashing door: opened, it sends a wave down the floor.</summary>
    public bool Power;
    public double Swing;
}

public enum Loot { Radio, Telly, Painting, Computer, Safe }

public sealed class Item
{
    public int Floor;
    public double X;
    public Loot Kind;
    public bool Taken;

    public int Points => Kind switch { Loot.Radio => 100, Loot.Telly => 200, Loot.Painting => 300, Loot.Computer => 400, _ => 500 };
}

/// <summary>A power door's wave, rolling down its floor.</summary>
public sealed class Wave
{
    public int Floor;
    public double X, Lo, Hi;
    public int Dir;
    public int Swept;
    public bool Dead;
}

public enum EffectKind { Score, Stars, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.9;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
