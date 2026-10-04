namespace PulseDeck.Core.Vcd;

public enum VcdVarType
{
    Wire, Reg, Integer, Parameter, Real, Time, Event,
    Supply0, Supply1, Tri, TriAnd, TriOr, TriReg, Tri0, Tri1, WAnd, WOr
}

public enum VcdScopeType { Module, Task, Function, Fork, Begin, Root }

public enum VcdTimeUnit { S, MS, US, NS, PS, FS }

public readonly record struct VcdTimescale(double Multiplier, VcdTimeUnit Unit)
{
    public static readonly VcdTimescale Default = new(1, VcdTimeUnit.NS);

    public double UnitSeconds => Unit switch
    {
        VcdTimeUnit.S => 1,
        VcdTimeUnit.MS => 1e-3,
        VcdTimeUnit.US => 1e-6,
        VcdTimeUnit.NS => 1e-9,
        VcdTimeUnit.PS => 1e-12,
        VcdTimeUnit.FS => 1e-15,
        _ => 1e-9
    };

    public double ToSeconds(long ticks) => ticks * Multiplier * UnitSeconds;

    public static VcdTimescale Parse(string text)
    {
        text = text.Trim();
        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
        string numPart = i > 0 ? text[..i] : "1";
        string unitPart = text[i..].Trim().ToLowerInvariant();
        double mult = double.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 1;
        VcdTimeUnit unit = unitPart switch
        {
            "s" => VcdTimeUnit.S,
            "ms" => VcdTimeUnit.MS,
            "us" => VcdTimeUnit.US,
            "ns" => VcdTimeUnit.NS,
            "ps" => VcdTimeUnit.PS,
            "fs" => VcdTimeUnit.FS,
            _ => VcdTimeUnit.NS
        };
        return new VcdTimescale(mult, unit);
    }
}

/// <summary>One value change for a signal, with time already normalized to seconds.</summary>
public readonly record struct VcdValueChange(double TimeSeconds, string Value);

public sealed class VcdScope
{
    public required string Name { get; init; }
    public required VcdScopeType Type { get; init; }
    public List<VcdScope> Children { get; } = new();
    public List<VcdVariable> Variables { get; } = new();
}

public sealed class VcdVariable
{
    /// <summary>Raw VCD identifier code, e.g. "!" — not stable across reloads, do not persist against it.</summary>
    public required string Identifier { get; init; }
    public required VcdVarType Type { get; init; }
    public required int Width { get; init; }
    /// <summary>Declared reference name, e.g. "D0" or "bus [7:0]".</summary>
    public required string Name { get; init; }
    /// <summary>Scope names from root to immediate parent, e.g. ["logic"].</summary>
    public required IReadOnlyList<string> ScopePath { get; init; }

    /// <summary>Value changes in ascending time order, seconds already applied via the document's timescale.</summary>
    public List<VcdValueChange> Changes { get; set; } = new();

    /// <summary>Stable identity used to match this signal across separate VCD loads/reloads of the same source.</summary>
    public string HierarchicalPath => ScopePath.Count == 0 ? Name : string.Join(".", ScopePath) + "." + Name;
}

public sealed class VcdDocument
{
    public string? DateText { get; set; }
    public string? VersionText { get; set; }
    public VcdTimescale Timescale { get; set; } = VcdTimescale.Default;
    public required VcdScope RootScope { get; init; }
    public List<VcdVariable> AllVariables { get; } = new();

    public double EndTimeSeconds { get; set; }
}
