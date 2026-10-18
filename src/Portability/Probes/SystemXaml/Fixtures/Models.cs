using System.ComponentModel;
using System.Globalization;
using System.Windows.Markup;

namespace Portability.XamlFixtures;

[ContentProperty(nameof(Items))]
public sealed class Catalog
{
    public string Title { get; set; } = "";
    public List<Entry> Items { get; } = [];
    public Entry? Selected { get; set; }
}

[RuntimeNameProperty(nameof(Name))]
public sealed class Entry
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public Coordinate Location { get; set; } = new(0, 0);
}

[TypeConverter(typeof(CoordinateConverter))]
public sealed record Coordinate(int X, int Y);

public sealed class CoordinateConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        var parts = ((string)value).Split(',');
        return new Coordinate(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        var coordinate = (Coordinate)value!;
        return FormattableString.Invariant($"{coordinate.X},{coordinate.Y}");
    }
}

public sealed class EchoExtension : MarkupExtension
{
    public string Text { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => $"echo:{Text}";
}
