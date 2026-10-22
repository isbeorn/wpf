using System.Windows;

namespace FrameworkIdentity.Companion;

public sealed class ValueOwner : DependencyObject
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(int), typeof(ValueOwner), new PropertyMetadata(7, OnChanged, static (_, value) => Math.Max(0, (int)value)), Validate);

    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public int CallbackCount { get; private set; }
    public int LastOldValue { get; private set; }
    public int LastNewValue { get; private set; }

    private static bool Validate(object value) => (int)value <= 100;
    private static void OnChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var valueOwner = (ValueOwner)owner;
        valueOwner.CallbackCount++;
        valueOwner.LastOldValue = (int)args.OldValue;
        valueOwner.LastNewValue = (int)args.NewValue;
    }
}

public sealed class FrozenValue : Freezable
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(int), typeof(FrozenValue), new PropertyMetadata(11));

    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new FrozenValue();
}
