using System.Globalization;

namespace PulseDeck.Core.Vcd;

public sealed class VcdParseException : Exception
{
    public VcdParseException(string message) : base(message) { }
}

public static class VcdParser
{
    public static VcdDocument ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        string text = reader.ReadToEnd();
        return ParseText(text);
    }

    public static VcdDocument ParseText(string text)
    {
        var root = new VcdScope { Name = "$root", Type = VcdScopeType.Root };
        var doc = new VcdDocument { RootScope = root };

        var scopeStack = new List<VcdScope> { root };
        var idToChanges = new Dictionary<string, List<(long Time, string Value)>>();

        using var it = VcdTokenizer.Tokenize(text).GetEnumerator();
        long currentTime = 0;

        bool Next(out string tok)
        {
            if (it.MoveNext()) { tok = it.Current; return true; }
            tok = "";
            return false;
        }

        string ReadUntilEnd()
        {
            var parts = new List<string>();
            while (Next(out var t))
            {
                if (t == "$end") break;
                parts.Add(t);
            }
            return string.Join(" ", parts);
        }

        while (Next(out var tok))
        {
            if (tok.Length == 0) continue;

            switch (tok)
            {
                case "$date":
                    doc.DateText = ReadUntilEnd();
                    break;

                case "$version":
                    doc.VersionText = ReadUntilEnd();
                    break;

                case "$comment":
                    ReadUntilEnd();
                    break;

                case "$timescale":
                    doc.Timescale = VcdTimescale.Parse(ReadUntilEnd());
                    break;

                case "$scope":
                {
                    if (!Next(out var typeTok)) throw new VcdParseException("Unexpected end of file in $scope");
                    if (!Next(out var nameTok)) throw new VcdParseException("Unexpected end of file in $scope");
                    ReadUntilEnd();
                    var type = ParseScopeType(typeTok);
                    var scope = new VcdScope { Name = nameTok, Type = type };
                    scopeStack[^1].Children.Add(scope);
                    scopeStack.Add(scope);
                    break;
                }

                case "$upscope":
                    ReadUntilEnd();
                    if (scopeStack.Count > 1) scopeStack.RemoveAt(scopeStack.Count - 1);
                    break;

                case "$var":
                {
                    if (!Next(out var typeTok)) throw new VcdParseException("Unexpected end of file in $var");
                    if (!Next(out var widthTok)) throw new VcdParseException("Unexpected end of file in $var");
                    if (!Next(out var idTok)) throw new VcdParseException("Unexpected end of file in $var");
                    if (!Next(out var nameTok)) throw new VcdParseException("Unexpected end of file in $var");

                    var extra = new List<string>();
                    while (Next(out var t))
                    {
                        if (t == "$end") break;
                        extra.Add(t);
                    }
                    string fullName = extra.Count > 0 ? nameTok + " " + string.Join(" ", extra) : nameTok;

                    int width = int.TryParse(widthTok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 1;

                    var variable = new VcdVariable
                    {
                        Identifier = idTok,
                        Type = ParseVarType(typeTok),
                        Width = width,
                        Name = fullName,
                        ScopePath = scopeStack.Skip(1).Select(s => s.Name).ToList()
                    };
                    scopeStack[^1].Variables.Add(variable);
                    doc.AllVariables.Add(variable);
                    if (!idToChanges.ContainsKey(idTok)) idToChanges[idTok] = new();
                    break;
                }

                case "$enddefinitions":
                    ReadUntilEnd();
                    break;

                case "$dumpvars":
                case "$dumpon":
                case "$dumpoff":
                case "$dumpall":
                case "$end":
                    break;

                default:
                    if (tok[0] == '#')
                    {
                        if (long.TryParse(tok.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                            currentTime = t;
                    }
                    else if (tok[0] is 'b' or 'B')
                    {
                        string value = tok[1..];
                        if (Next(out var id)) RecordChange(idToChanges, id, currentTime, value);
                    }
                    else if (tok[0] is 'r' or 'R')
                    {
                        string value = tok[1..];
                        if (Next(out var id)) RecordChange(idToChanges, id, currentTime, value);
                    }
                    else
                    {
                        // Scalar change: value char immediately followed by identifier, e.g. "0!".
                        string value = tok[..1];
                        string id = tok[1..];
                        if (id.Length == 0)
                        {
                            // Tolerate a space between value and identifier, seen in some non-conformant dumps.
                            if (Next(out var sep)) id = sep;
                        }
                        if (id.Length > 0) RecordChange(idToChanges, id, currentTime, value);
                    }
                    break;
            }
        }

        double endTime = 0;
        foreach (var variable in doc.AllVariables)
        {
            if (idToChanges.TryGetValue(variable.Identifier, out var raw))
            {
                var changes = new List<VcdValueChange>(raw.Count);
                foreach (var (time, value) in raw)
                {
                    double seconds = doc.Timescale.ToSeconds(time);
                    changes.Add(new VcdValueChange(seconds, value));
                    if (seconds > endTime) endTime = seconds;
                }
                variable.Changes = changes;
            }
        }
        doc.EndTimeSeconds = endTime;

        return doc;
    }

    private static void RecordChange(Dictionary<string, List<(long Time, string Value)>> map, string id, long time, string value)
    {
        if (!map.TryGetValue(id, out var list))
        {
            list = new List<(long Time, string Value)>();
            map[id] = list;
        }
        list.Add((time, value));
    }

    private static VcdVarType ParseVarType(string token) => token.ToLowerInvariant() switch
    {
        "wire" => VcdVarType.Wire,
        "reg" => VcdVarType.Reg,
        "integer" => VcdVarType.Integer,
        "parameter" => VcdVarType.Parameter,
        "real" => VcdVarType.Real,
        "time" => VcdVarType.Time,
        "event" => VcdVarType.Event,
        "supply0" => VcdVarType.Supply0,
        "supply1" => VcdVarType.Supply1,
        "tri" => VcdVarType.Tri,
        "triand" => VcdVarType.TriAnd,
        "trior" => VcdVarType.TriOr,
        "trireg" => VcdVarType.TriReg,
        "tri0" => VcdVarType.Tri0,
        "tri1" => VcdVarType.Tri1,
        "wand" => VcdVarType.WAnd,
        "wor" => VcdVarType.WOr,
        _ => VcdVarType.Wire
    };

    private static VcdScopeType ParseScopeType(string token) => token.ToLowerInvariant() switch
    {
        "module" => VcdScopeType.Module,
        "task" => VcdScopeType.Task,
        "function" => VcdScopeType.Function,
        "fork" => VcdScopeType.Fork,
        "begin" => VcdScopeType.Begin,
        _ => VcdScopeType.Module
    };
}
