using PulseDeck.Core.Model;
using Xunit;

namespace PulseDeck.Core.Tests;

public class WaveformSessionTests
{
    private const string SampleDir = @"C:\Users\user\Documents\My Projects\TinyTapeout\wokwi pll";

    [Fact]
    public async Task Reload_Detects_Added_Signal_When_D5_Appears()
    {
        // Simulate consecutive captures landing on the same path: first a run missing D5,
        // then a later run that includes it — the real-world case these two sample files represent.
        string tempPath = Path.Combine(Path.GetTempPath(), $"pulsedeck-test-{Guid.NewGuid():N}.vcd");
        File.Copy(Path.Combine(SampleDir, "wokwi-logic.vcd"), tempPath, overwrite: true);

        try
        {
            var session = new WaveformSession();
            var addOutcome = await session.AddFileAsync(tempPath);
            Assert.True(addOutcome.Success);

            var fileNode = session.RootNodes.Single();
            var namesAfterFirstLoad = fileNode.EnumerateSignalDescendants().Select(s => s.OriginalName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "D0", "D1", "D2", "D3", "D4", "D6", "D7" }, namesAfterFirstLoad);
            Assert.All(fileNode.EnumerateSignalDescendants(), s => Assert.False(s.IsNew));

            // Give D5 a distinct alias before reload, so we can confirm alias assignment survives reload for untouched signals.
            var d0 = fileNode.EnumerateSignalDescendants().Single(s => s.OriginalName == "D0");
            d0.Alias = "clk";

            File.Copy(Path.Combine(SampleDir, "wokwi-logic2.vcd"), tempPath, overwrite: true);
            var file = session.Files.Single();
            var reloadOutcome = await session.ReloadFileAsync(file);

            Assert.True(reloadOutcome.Success);
            Assert.Equal(new[] { "logic.D5" }, reloadOutcome.Summary.AddedPaths);
            Assert.Empty(reloadOutcome.Summary.RemovedPaths);

            var d5 = fileNode.EnumerateSignalDescendants().Single(s => s.OriginalName == "D5");
            Assert.True(d5.IsNew);

            var d0After = fileNode.EnumerateSignalDescendants().Single(s => s.OriginalName == "D0");
            Assert.Same(d0, d0After); // same node instance preserved across reload
            Assert.Equal("clk", d0After.Alias); // alias survived the reload
            Assert.False(d0After.IsMissing);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task Reload_Flags_Signal_Missing_When_It_Disappears_But_Keeps_Node()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"pulsedeck-test-{Guid.NewGuid():N}.vcd");
        File.Copy(Path.Combine(SampleDir, "wokwi-logic2.vcd"), tempPath, overwrite: true);

        try
        {
            var session = new WaveformSession();
            await session.AddFileAsync(tempPath);
            var fileNode = session.RootNodes.Single();
            Assert.Equal(8, fileNode.EnumerateSignalDescendants().Count());

            File.Copy(Path.Combine(SampleDir, "wokwi-logic.vcd"), tempPath, overwrite: true);
            var file = session.Files.Single();
            var reloadOutcome = await session.ReloadFileAsync(file);

            Assert.Equal(new[] { "logic.D5" }, reloadOutcome.Summary.RemovedPaths);
            // Node count unchanged — the missing signal stays in the tree, just flagged.
            Assert.Equal(8, fileNode.EnumerateSignalDescendants().Count());
            var d5 = fileNode.EnumerateSignalDescendants().Single(s => s.OriginalName == "D5");
            Assert.True(d5.IsMissing);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task Workspace_Round_Trip_Preserves_Aliases_And_Colors()
    {
        var session = new WaveformSession();
        await session.AddFileAsync(Path.Combine(SampleDir, "wokwi-logic.vcd"));
        var d0 = session.EnumerateAllSignalNodes().Single(s => s.OriginalName == "D0");
        d0.Alias = "clk";
        d0.Color = RgbColor.FromHex("#FF0000");
        d0.IsVisible = false;

        var view = new WorkspaceViewState { WindowStartSeconds = 0, WindowEndSeconds = 0.01, CursorSeconds = 0.005 };
        var workspaceDoc = session.ToWorkspaceDocument(view);

        string tempPath = Path.Combine(Path.GetTempPath(), $"pulsedeck-ws-{Guid.NewGuid():N}.json");
        try
        {
            WorkspaceSerializer.Save(workspaceDoc, tempPath);
            var reloaded = WorkspaceSerializer.Load(tempPath);

            var session2 = new WaveformSession();
            await session2.LoadFromWorkspaceAsync(reloaded);

            var d0Restored = session2.EnumerateAllSignalNodes().Single(s => s.OriginalName == "D0");
            Assert.Equal("clk", d0Restored.Alias);
            Assert.Equal(RgbColor.FromHex("#FF0000"), d0Restored.Color);
            Assert.False(d0Restored.IsVisible);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
