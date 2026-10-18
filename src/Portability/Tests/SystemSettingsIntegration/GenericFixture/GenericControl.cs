using System.Windows;
using System.Windows.Controls;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace Portability.SystemSettings.GenericFixture;

public sealed class GenericControl : Control
{
    static GenericControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(GenericControl), new FrameworkPropertyMetadata(typeof(GenericControl)));
}
