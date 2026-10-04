using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PulseDeck.App.ViewModels;
using PulseDeck.Core.Model;

namespace PulseDeck.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    /// <summary>Loads files passed on the command line (e.g. a file-association launch), then fits the view.</summary>
    public async Task LoadFilesFromArgsAsync(string[] paths)
    {
        foreach (var path in paths)
            await _vm.AddFileAsync(path);
        _vm.ZoomFit();
    }

    private void ExpandToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WaveRow row })
            _vm.ToggleExpand(row.Node);
    }

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WaveRow row }) return;
        var current = row.Node.Color ?? RgbColor.DefaultPalette[0];
        int idx = Array.IndexOf(RgbColor.DefaultPalette, current);
        row.Node.Color = RgbColor.FromPalette(idx + 1);
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var node = tb.DataContext switch { WaveRow row => row.Node, SignalTreeNode n => n, _ => null };
        if (node == null) return;

        if (node.Kind == SignalNodeKind.File && node.File != null)
            _vm.SetFileAlias(node.File, tb.Text);
        else
            node.EditableName = tb.Text;
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void FileAliasBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LoadedFile file } tb)
            _vm.SetFileAlias(file, tb.Text);
    }

    private async void ReloadFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LoadedFile file })
            await _vm.ReloadFileAsync(file);
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LoadedFile file })
            _vm.RemoveFile(file);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SignalTreeNode node })
            _vm.MoveDisplayedSignal(node, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SignalTreeNode node })
            _vm.MoveDisplayedSignal(node, 1);
    }

    private void RemoveFromDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SignalTreeNode node })
            _vm.RemoveFromDisplay(node);
    }

    private void ColorSwatch2_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SignalTreeNode node }) return;
        var current = node.Color ?? RgbColor.DefaultPalette[0];
        int idx = Array.IndexOf(RgbColor.DefaultPalette, current);
        node.Color = RgbColor.FromPalette(idx + 1);
    }

    private void PanFarLeft_Click(object sender, RoutedEventArgs e) => _vm.PanBySpanFraction(-0.75);
    private void PanLeft_Click(object sender, RoutedEventArgs e) => _vm.PanBySpanFraction(-0.25);
    private void PanRight_Click(object sender, RoutedEventArgs e) => _vm.PanBySpanFraction(0.25);
    private void PanFarRight_Click(object sender, RoutedEventArgs e) => _vm.PanBySpanFraction(0.75);

    private void HScrollBar_Scroll(object sender, ScrollEventArgs e) => _vm.PanTo(e.NewValue);

    private void RulerFontUp_Click(object sender, RoutedEventArgs e) => _vm.IncreaseRulerFont();
    private void RulerFontDown_Click(object sender, RoutedEventArgs e) => _vm.DecreaseRulerFont();

    private void ToolSelect_Click(object sender, RoutedEventArgs e) => _vm.ActiveTool = MarkupTool.Select;
    private void ToolVLine_Click(object sender, RoutedEventArgs e) => _vm.ActiveTool = MarkupTool.VerticalLine;
    private void ToolText_Click(object sender, RoutedEventArgs e) => _vm.ActiveTool = MarkupTool.Text;
    private void ToolArrow_Click(object sender, RoutedEventArgs e) => _vm.ActiveTool = MarkupTool.Arrow;
    private void ClearMarkups_Click(object sender, RoutedEventArgs e) => _vm.Annotations.Clear();

    private SignalTreeNode? _contextMenuNode;

    /// <summary>Resolves which signal was right-clicked before the shared NameDisplayContextMenu opens —
    /// its DataContext differs between the tree (WaveRow) and the Displayed panel (SignalTreeNode directly).</summary>
    private void NameArea_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        _contextMenuNode = fe.DataContext switch
        {
            WaveRow row => row.Node,
            SignalTreeNode node => node,
            _ => null
        };
    }

    private void NamesGlobal_OriginalWithFile_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.OriginalWithFile;
    private void NamesGlobal_OriginalWithFileAndAlias_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.OriginalWithFileAndAlias;
    private void NamesGlobal_AliasOnly_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.AliasOnly;

    private void GlobalMode_OriginalWithFile_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.OriginalWithFile;
    private void GlobalMode_OriginalWithFileAndAlias_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.OriginalWithFileAndAlias;
    private void GlobalMode_AliasOnly_Click(object sender, RoutedEventArgs e) => _vm.GlobalNameDisplayMode = SignalNameDisplayMode.AliasOnly;

    private void NodeMode_UseGlobal_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuNode != null) _vm.SetNodeNameDisplayMode(_contextMenuNode, null);
    }

    private void NodeMode_OriginalWithFile_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuNode != null) _vm.SetNodeNameDisplayMode(_contextMenuNode, SignalNameDisplayMode.OriginalWithFile);
    }

    private void NodeMode_OriginalWithFileAndAlias_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuNode != null) _vm.SetNodeNameDisplayMode(_contextMenuNode, SignalNameDisplayMode.OriginalWithFileAndAlias);
    }

    private void NodeMode_AliasOnly_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuNode != null) _vm.SetNodeNameDisplayMode(_contextMenuNode, SignalNameDisplayMode.AliasOnly);
    }

    private void RenameSignal_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuNode != null) RenameSignalDialog.Show(this, _contextMenuNode);
    }

    private AliasEditorWindow? _aliasEditorWindow;

    private void OpenAliasEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_aliasEditorWindow != null)
        {
            _aliasEditorWindow.Activate();
            return;
        }
        _aliasEditorWindow = new AliasEditorWindow(_vm.Session) { Owner = this };
        _aliasEditorWindow.Closed += (_, _) => _aliasEditorWindow = null;
        _aliasEditorWindow.Show();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
        MessageBox.Show(this,
            $"Pulse Deck V{version}\n" +
            "A multi-file VCD waveform viewer.\n\n" +
            "Opens VCD files from any tool. Load captures from different simulation runs side by side " +
            "to see what difference a change made to the logic states.\n\n" +
            "Also useful for working with multiple Wokwi VCD files.\n\n" +
            "Created by John Dowdell\n" +
            "https://github.com/fluxfocus\n\n" +
            "Copyright © 2026 John Dowdell. Released under the MIT License: free to use, modify and " +
            "share, provided the copyright and licence notice is kept. Supplied without warranty.",
            "About Pulse Deck", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
