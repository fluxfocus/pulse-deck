using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using PulseDeck.Core.Model;

namespace PulseDeck.App;

public partial class AliasEditorWindow : Window
{
    private readonly WaveformSession _session;
    public ObservableCollection<SignalTreeNode> Rows { get; } = new();

    public AliasEditorWindow(WaveformSession session)
    {
        InitializeComponent();
        _session = session;
        Grid.ItemsSource = Rows;
        LoadRows();
    }

    private void LoadRows()
    {
        Rows.Clear();
        foreach (var node in _session.EnumerateAllSignalNodes())
            Rows.Add(node);
        StatusText.Text = $"{Rows.Count} signal(s).";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadRows();

    private void SaveAliases_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Alias File",
            Filter = "Pulse Deck alias file (*.vcdaliases)|*.vcdaliases|All files (*.*)|*.*",
            DefaultExt = ".vcdaliases"
        };
        if (dialog.ShowDialog(this) != true) return;

        var doc = _session.ToAliasFileDocument();
        AliasFileSerializer.Save(doc, dialog.FileName);
        StatusText.Text = $"Saved {doc.Entries.Count} alias(es) to {System.IO.Path.GetFileName(dialog.FileName)}.";
    }

    private void LoadAliases_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Alias File",
            Filter = "Pulse Deck alias file (*.vcdaliases)|*.vcdaliases|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var doc = AliasFileSerializer.Load(dialog.FileName);
        int applied = _session.ApplyAliasFileDocument(doc);
        StatusText.Text = $"Applied {applied} of {doc.Entries.Count} saved alias(es) to currently loaded signals.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
