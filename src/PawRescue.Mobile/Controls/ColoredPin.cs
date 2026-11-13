using Microsoft.Maui.Controls.Maps;

namespace PawRescue.Mobile.Controls;

/// <summary>
/// A map pin with a color (Google Maps "hue", 0–360).
/// The color is applied on Android by the mapping added in MauiProgram.cs,
/// because the standard MAUI Pin has no color property.
/// </summary>
public class ColoredPin : Pin
{
    public static readonly BindableProperty HueProperty =
        BindableProperty.Create(nameof(Hue), typeof(float), typeof(ColoredPin), 355f);

    public float Hue
    {
        get => (float)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }
}
