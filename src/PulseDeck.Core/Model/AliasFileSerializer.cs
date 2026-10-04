using System.Text.Json;

namespace PulseDeck.Core.Model;

public static class AliasFileSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(AliasFileDocument doc, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options));

    public static AliasFileDocument Load(string path) =>
        JsonSerializer.Deserialize<AliasFileDocument>(File.ReadAllText(path), Options) ?? new AliasFileDocument();
}
