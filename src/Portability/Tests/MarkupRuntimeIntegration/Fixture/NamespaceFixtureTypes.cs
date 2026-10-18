using System.Windows.Markup;

[assembly: XmlnsDefinition("urn:portability:namespace-resolution", "Portability.MarkupIntegration.NamespaceFixture")]

namespace Portability.MarkupIntegration.NamespaceFixture;

public sealed class ExistingValue
{
    public string Label { get; set; } = string.Empty;
}
