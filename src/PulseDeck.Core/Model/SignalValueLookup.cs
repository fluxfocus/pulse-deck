using PulseDeck.Core.Vcd;

namespace PulseDeck.Core.Model;

public static class SignalValueLookup
{
    /// <summary>Raw value held by the signal at the given time (binary search over ascending-time changes).</summary>
    public static string? GetValueAt(VcdVariable? variable, double timeSeconds)
    {
        if (variable == null || variable.Changes.Count == 0) return null;
        var changes = variable.Changes;

        if (timeSeconds < changes[0].TimeSeconds) return null;

        int lo = 0, hi = changes.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (changes[mid].TimeSeconds <= timeSeconds) lo = mid; else hi = mid - 1;
        }
        return changes[lo].Value;
    }

    /// <summary>Index of the first change at or after <paramref name="timeSeconds"/>; used to start windowed rendering.</summary>
    public static int FindFirstIndexAtOrAfter(List<VcdValueChange> changes, double timeSeconds)
    {
        int lo = 0, hi = changes.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (changes[mid].TimeSeconds < timeSeconds) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Formats a raw VCD value for display: hex for multi-bit vectors, as-is for scalars/reals.</summary>
    public static string FormatValue(string raw, int width, VcdVarType type)
    {
        if (type == VcdVarType.Real) return raw;
        if (width <= 1) return raw;

        if (raw.Any(c => c is 'x' or 'X')) return "x";
        if (raw.Any(c => c is 'z' or 'Z')) return "z";

        string bits = raw.PadLeft(width, '0');
        try
        {
            var value = Convert.ToUInt64(bits.Length > 64 ? bits[^64..] : bits, 2);
            int hexDigits = (width + 3) / 4;
            return "0x" + value.ToString("X").PadLeft(hexDigits, '0');
        }
        catch
        {
            return raw;
        }
    }
}
