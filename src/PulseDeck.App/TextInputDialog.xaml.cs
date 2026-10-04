using System.Windows;
using System.Windows.Input;

namespace PulseDeck.App;

public partial class TextInputDialog : Window
{
    public string ResultText { get; private set; } = "";

    public TextInputDialog(string prompt, string initial = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = initial;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ResultText = InputBox.Text; DialogResult = true; }
    }

    /// <summary>Shows the dialog modally; returns null if cancelled.</summary>
    public static string? Show(Window? owner, string prompt, string initial = "")
    {
        var dlg = new TextInputDialog(prompt, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.ResultText : null;
    }
}
