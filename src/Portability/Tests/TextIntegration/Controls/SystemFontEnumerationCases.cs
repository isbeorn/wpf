using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

internal static class SystemFontEnumerationCases
{
    internal static object Verify(bool portable)
    {
        ICollection<FontFamily> families = Fonts.SystemFontFamilies;
        Require(families.Count > 0 && families.IsReadOnly, "System font families must be nonempty and read-only on the test host.");
        Require(ReferenceEquals(families, Fonts.SystemFontFamilies), "System family enumeration changed identity.");
        RejectMutation(() => families.Clear());
        Require(families.Select(family => family.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == families.Count,
            "System family enumeration contains duplicate names.");

        var control = (ItemsControl)XamlReader.Parse("""
            <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:ComponentModel="clr-namespace:System.ComponentModel;assembly=WindowsBase">
                <ItemsControl.Resources>
                    <CollectionViewSource x:Key="OrderedSystemFonts" Source="{Binding Source={x:Static Fonts.SystemFontFamilies}}">
                        <CollectionViewSource.SortDescriptions>
                            <ComponentModel:SortDescription PropertyName="Source" />
                        </CollectionViewSource.SortDescriptions>
                    </CollectionViewSource>
                </ItemsControl.Resources>
                <ItemsControl.ItemsSource>
                    <Binding Source="{StaticResource OrderedSystemFonts}" />
                </ItemsControl.ItemsSource>
            </ItemsControl>
            """);
        var ordered = (CollectionViewSource)control.Resources["OrderedSystemFonts"];
        Require(ReferenceEquals(ordered.Source, families) && control.Items.Count == families.Count && control.Items[0] is FontFamily,
            "The real x:Static collection binding did not populate the actual ItemsControl.");
        Require(control.Items.Cast<FontFamily>().Select(family => family.Source).SequenceEqual(
            families.Select(family => family.Source).OrderBy(name => name, StringComparer.Create(ordered.View.Culture, false))),
            "CollectionViewSource did not order the font families by Source.");
        var selected = (FontFamily)control.Items[0];
        Require(!string.IsNullOrEmpty(selected.FamilyNames.Values.First()), "The selected family has no actual display name.");

        string representativeName = OperatingSystem.IsWindows() ? "Arial" : "FreeSans";
        FontStyle representativeSlant = OperatingSystem.IsWindows() ? FontStyles.Italic : FontStyles.Oblique;
        FontWeight representativeBold = OperatingSystem.IsWindows() ? FontWeights.Bold : FontWeights.SemiBold;
        FontFamily representative = families.Single(family => family.Source == representativeName);
        ICollection<Typeface> representativeFaces = representative.GetTypefaces();
        foreach (FontWeight weight in new[] { FontWeights.Normal, representativeBold })
        {
            Require(representativeFaces.Any(face => face.Weight == weight && face.Stretch == FontStretches.Normal && face.Style == FontStyles.Normal),
                $"The declared host font {representativeName} is missing its upright {weight} variant.");
            Require(representativeFaces.Any(face => face.Weight == weight && face.Stretch == FontStretches.Normal &&
                face.Style == representativeSlant),
                $"The declared host font {representativeName} is missing its {representativeSlant} {weight} variant.");
        }

        ICollection<Typeface> typefaces = Fonts.SystemTypefaces;
        Require(typefaces.IsReadOnly && typefaces.Count >= families.Count, "System typefaces did not enumerate the family variants.");
        RejectMutation(() => typefaces.Clear());
        int physical = 0, variants = 0;
        foreach (FontFamily family in families)
        {
            var faces = family.GetTypefaces();
            Require(faces.Count > 0, "A system family has no typeface.");
            if (portable)
            {
                Require(faces.IsReadOnly, "Physical family typefaces are mutable.");
                RejectMutation(() => faces.Clear());
                Require(faces.Select(face => (face.Style, face.Weight, face.Stretch)).Distinct().Count() == faces.Count,
                    "Physical family contains duplicate variants.");
            }
            variants += faces.Count;
            foreach (Typeface face in faces)
            {
                if (!face.TryGetGlyphTypeface(out GlyphTypeface glyph))
                {
                    Require(!portable, "An enumerated portable physical face cannot resolve.");
                    continue;
                }
                physical++;
                if (portable)
                    Require(glyph.Style == face.Style && glyph.Weight == face.Weight && glyph.Stretch == face.Stretch,
                        "An enumerated variant resolves to different physical font properties.");
            }
        }
        Require(physical > 0 && typefaces.Count == variants, "SystemTypefaces differs from the family enumeration.");
        var local = new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar), "./#DejaVu Sans");
        var localFaces = local.GetTypefaces();
        Require(localFaces.Count > 0, "The pinned local family has no typefaces.");
        foreach (Typeface face in localFaces)
        {
            Require(face.FontFamily.Source == local.Source && face.FontFamily.BaseUri == local.BaseUri,
                "Local family enumeration discarded its URI identity.");
            Require(face.TryGetGlyphTypeface(out GlyphTypeface glyph) && glyph.FontUri.IsFile &&
                Path.GetFullPath(glyph.FontUri.LocalPath).StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fonts")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Local family enumeration resolved a system font instead of the pinned fixture.");
        }
        return new { name = "system-fonts-public-and-xaml-enumeration", status = "pass", families = families.Count,
            variants, physical, representative = representativeName, selected = selected.Source, profile = portable ? "existing physical TTF/OTF catalog" : "stock desktop catalog" };
    }

    private static void RejectMutation(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A read-only font collection accepted mutation.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
