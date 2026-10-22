using System.Windows;
using System.Windows.Controls;

namespace Portability.MarkupIntegration.Fixture;

public partial class ProbeView : UserControl
{
    public ProbeView() => InitializeComponent();
    public int ActionCount { get; private set; }
    public Button ConnectedButton => ActionButton;
    public ProbeCard ConnectedCard => Card;
    public ContentControl ConnectedTemplateTarget => TemplateTarget;
    private void Action_Click(object sender, RoutedEventArgs args) => ActionCount++;
}

public sealed class ProbeCard : ContentControl;

public sealed class ProbeModel
{
    public string Label { get; set; } = "";
}
