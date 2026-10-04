namespace PulseDeck.Core.Vcd;

/// <summary>
/// Whitespace tokenizer for VCD files. Per the VCD grammar, every token (keywords, identifiers,
/// value-change tokens, scalar "0!" pairs) is whitespace-delimited, so a plain split is spec-correct
/// and much simpler than hand-rolling per-construct parsing.
/// </summary>
internal static class VcdTokenizer
{
    public static IEnumerable<string> Tokenize(string text)
    {
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n) break;
            int start = i;
            while (i < n && !char.IsWhiteSpace(text[i])) i++;
            yield return text[start..i];
        }
    }
}
