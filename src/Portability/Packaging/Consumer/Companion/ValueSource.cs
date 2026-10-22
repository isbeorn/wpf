using System.Windows;

namespace PackageCompanion;

public sealed class ValueSource : DependencyObject
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(ValueSource), new PropertyMetadata(7));
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
}
