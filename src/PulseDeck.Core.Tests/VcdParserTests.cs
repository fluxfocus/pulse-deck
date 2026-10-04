using System.Diagnostics;
using PulseDeck.Core.Vcd;
using Xunit;
using Xunit.Abstractions;

namespace PulseDeck.Core.Tests;

public class VcdParserTests
{
    private readonly ITestOutputHelper _output;

    public VcdParserTests(ITestOutputHelper output) => _output = output;

    private const string SampleDir = @"C:\Users\user\Documents\My Projects\TinyTapeout\wokwi pll";

    [Fact]
    public void Parses_Logic1_With_Seven_Signals_Missing_D5()
    {
        var doc = ParseTimed("wokwi-logic.vcd");
        var names = doc.AllVariables.Select(v => v.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "D0", "D1", "D2", "D3", "D4", "D6", "D7" }, names);
        Assert.DoesNotContain("D5", names);
        Assert.Equal(VcdTimeUnit.NS, doc.Timescale.Unit);
        Assert.Equal(1, doc.Timescale.Multiplier);

        var d0 = doc.AllVariables.Single(v => v.Name == "D0");
        Assert.True(d0.Changes.Count > 0);
        Assert.All(d0.Changes, c => Assert.True(c.Value is "0" or "1"));
    }

    [Fact]
    public void Parses_Logic2_With_Eight_Signals_Including_D5()
    {
        var doc = ParseTimed("wokwi-logic2.vcd");
        var names = doc.AllVariables.Select(v => v.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7" }, names);
    }

    [Fact]
    public void Parses_Logic3_With_Eight_Signals_Including_D5()
    {
        var doc = ParseTimed("wokwi-logic3.vcd");
        var names = doc.AllVariables.Select(v => v.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7" }, names);
    }

    [Fact]
    public void Scope_Hierarchy_Is_Single_Module_Logic()
    {
        var doc = ParseTimed("wokwi-logic.vcd");
        var d0 = doc.AllVariables.Single(v => v.Name == "D0");
        Assert.Equal(new[] { "logic" }, d0.ScopePath);
        Assert.Equal("logic.D0", d0.HierarchicalPath);
    }

    private VcdDocument ParseTimed(string fileName)
    {
        string path = Path.Combine(SampleDir, fileName);
        var sw = Stopwatch.StartNew();
        var doc = VcdParser.ParseFile(path);
        sw.Stop();
        _output.WriteLine($"{fileName}: parsed {doc.AllVariables.Count} vars, " +
                           $"{doc.AllVariables.Sum(v => v.Changes.Count)} changes in {sw.ElapsedMilliseconds} ms, " +
                           $"end={doc.EndTimeSeconds}s");
        return doc;
    }
}
