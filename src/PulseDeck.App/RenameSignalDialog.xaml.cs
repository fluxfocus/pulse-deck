using System.Windows;
using System.Windows.Input;
using PulseDeck.Core.Model;

namespace PulseDeck.App;

public partial class RenameSignalDialog : Window
{
    private readonly SignalTreeNode _node;

    public RenameSignalDialog(SignalTreeNode node)
    {
        InitializeComponent();
        _node = node;

        FileText.Text = node.File?.FileAlias ?? "";
        SignalText.Text = node.Key?.HierarchicalPath ?? node.OriginalName;
        AliasBox.Text = node.Alias ?? "";

        Loaded += (_, _) => { AliasBox.Focus(); AliasBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _node.Alias = null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void AliasBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Commit();
    }

    private void Commit()
    {
        _node.Alias = string.IsNullOrWhiteSpace(AliasBox.Text) ? null : AliasBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Shows the dialog modally; returns true if the alias was changed (including cleared).</summary>
    public static bool Show(Window? owner, SignalTreeNode node)
    {
        var dlg = new RenameSignalDialog(node) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
