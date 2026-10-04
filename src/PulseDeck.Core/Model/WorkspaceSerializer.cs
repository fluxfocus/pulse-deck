using System.Text.Json;

namespace PulseDeck.Core.Model;

public static class WorkspaceSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(WorkspaceDocument doc, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(doc, Options));

    public static WorkspaceDocument Load(string path) =>
        JsonSerializer.Deserialize<WorkspaceDocument>(File.ReadAllText(path), Options) ?? new WorkspaceDocument();
}
