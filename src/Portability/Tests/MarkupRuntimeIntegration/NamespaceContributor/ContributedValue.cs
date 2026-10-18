using System.Windows.Markup;

[assembly: XmlnsDefinition("urn:portability:namespace-resolution", "Portability.MarkupIntegration.NamespaceContributor")]

namespace Portability.MarkupIntegration.NamespaceContributor;

public sealed class ContributedValue
{
    public string Label { get; set; } = string.Empty;
}
