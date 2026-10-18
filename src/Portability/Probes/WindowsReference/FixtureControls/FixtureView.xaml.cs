using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FixtureControls;

public partial class FixtureView : UserControl
{
    public FixtureView() => InitializeComponent();
}

public sealed class MetricControl : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(MetricControl), new PropertyMetadata(0));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

public sealed class NonNegativeRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
        int.TryParse(value?.ToString(), NumberStyles.Integer, cultureInfo, out int result) && result >= 0
            ? ValidationResult.ValidResult
            : new ValidationResult(false, "Enter a nonnegative integer.");
}
