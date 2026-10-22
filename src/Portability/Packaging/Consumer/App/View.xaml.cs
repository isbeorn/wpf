using System.Windows;
using System.Windows.Controls;
namespace PackageConsumer;
public partial class View : UserControl
{
    public int Clicks { get; private set; }
    public View() { InitializeComponent(); }
    private void OnClick(object sender, RoutedEventArgs args) { Clicks++; }
}
