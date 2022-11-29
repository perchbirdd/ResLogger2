namespace ResLogger2.Common.ServerDatabase.Model;

// stolen from XL
public class GameVersion : IComparable
{
    public long Id { get; set; }
    public uint Year { get; private init; }
    public uint Month { get; private init; }
    public uint Day { get; private init; }
    public uint Part { get; private init; }
    public uint Revision { get; private init; }
    public bool IsSpecial { get; private init; }
    public string? Comment { get; private init; }

    public static GameVersion Parse(string input)
    {
        try
        {
            var parts = input.Split('.');
            return new GameVersion
            {
                Year = uint.Parse(parts[0]),
                Month = uint.Parse(parts[1]),
                Day = uint.Parse(parts[2]),
                Part = uint.Parse(parts[3]),
                Revision = uint.Parse(parts[4]),
                IsSpecial = false,
                Comment = null
            };
        }
        catch (FormatException e)
        {
            throw new Exception($"Failed to parse game version {input}", e);
        }
    }
    
    public static GameVersion Parse(string input, string? comment)
    {
        try
        {
            var parts = input.Split('.');
            return new GameVersion
            {
                Year = uint.Parse(parts[0]),
                Month = uint.Parse(parts[1]),
                Day = uint.Parse(parts[2]),
                Part = uint.Parse(parts[3]),
                Revision = uint.Parse(parts[4]),
                IsSpecial = comment != null,
                Comment = comment,
            };
        }
        catch (FormatException e)
        {
            throw new Exception($"Failed to parse game version {input}", e);
        }
    }
    
    public override string ToString() => $"{Year:0000}.{Month:00}.{Day:00}.{Part:0000}.{Revision:0000}";

    public int CompareTo(object obj)
    {
        var other = obj as GameVersion;
        if (other == null)
            return 1;

        if (Year > other.Year)
            return 1;

        if (Year < other.Year)
            return -1;

        if (Month > other.Month)
            return 1;

        if (Month < other.Month)
            return -1;

        if (Day > other.Day)
            return 1;

        if (Day < other.Day)
            return -1;

        if (Revision > other.Revision)
            return 1;

        if (Revision < other.Revision)
            return -1;

        if (Part > other.Part)
            return 1;

        if (Part < other.Part)
            return -1;

        return 0;
    }

    public static bool operator <(GameVersion x, GameVersion y) => x.CompareTo(y) < 0;
    public static bool operator >(GameVersion x, GameVersion y) => x.CompareTo(y) > 0;
    public static bool operator <=(GameVersion x, GameVersion y) => x.CompareTo(y) <= 0;
    public static bool operator >=(GameVersion x, GameVersion y) => x.CompareTo(y) >= 0;

    public static bool operator ==(GameVersion x, GameVersion y)
    {
        if (x is null)
            return y is null;

        return x.CompareTo(y) == 0;
    }

    public static bool operator !=(GameVersion x, GameVersion y)
    {
        if (x is null)
            return y != null;

        return x.CompareTo(y) != 0;
    }
}