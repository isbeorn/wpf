using System.Windows;

namespace AssemblyIdentity.Companion;

public sealed class PropertyOwner : DependencyObject
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        "Number", typeof(int), typeof(PropertyOwner), new PropertyMetadata(7, OnNumberChanged));

    public int CallbackCount { get; private set; }

    private static void OnNumberChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
        => ((PropertyOwner)owner).CallbackCount++;
}

public static class PropertyOperations
{
    public static object Create() => new PropertyOwner();

    public static string Exercise(object value)
    {
        var owner = (PropertyOwner)value;
        var initial = (int)owner.GetValue(PropertyOwner.NumberProperty);
        owner.SetValue(PropertyOwner.NumberProperty, 23);
        var changed = (int)owner.GetValue(PropertyOwner.NumberProperty);
        owner.ClearValue(PropertyOwner.NumberProperty);
        var cleared = (int)owner.GetValue(PropertyOwner.NumberProperty);
        return $"default={initial};set={changed};clear={cleared};callbacks={owner.CallbackCount}";
    }
}
