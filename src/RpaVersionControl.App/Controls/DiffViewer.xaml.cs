using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RpaVersionControl.App.Controls;

public sealed partial class DiffViewer : UserControl
{
    public DiffViewer()
    {
        InitializeComponent();
    }

    public void LoadPatch(string? patch)
    {
        var text = patch ?? string.Empty;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var items = new List<DiffLineViewModel>();

        var added = 0;
        var removed = 0;

        foreach (var line in lines)
        {
            var kind = DiffLineKind.Neutral;
            Brush background = new SolidColorBrush(Colors.Transparent);

            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                kind = DiffLineKind.Added;
                added++;
                background = new SolidColorBrush(ColorHelper.FromArgb(35, 22, 131, 90));
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                kind = DiffLineKind.Removed;
                removed++;
                background = new SolidColorBrush(ColorHelper.FromArgb(35, 196, 43, 28));
            }
            else if (line.StartsWith("@@"))
            {
                kind = DiffLineKind.Header;
                background = new SolidColorBrush(ColorHelper.FromArgb(28, 0, 95, 184));
            }

            items.Add(new DiffLineViewModel(line, background, kind));
        }

        LinesList.ItemsSource = items;
        SummaryText.Text = $"+{added}  −{removed}";
    }

    private sealed record DiffLineViewModel(string Text, Brush Background, DiffLineKind Kind);
    private enum DiffLineKind { Neutral, Added, Removed, Header }
}
