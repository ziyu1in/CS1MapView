using System.Globalization;

namespace CS2MapView.CS1.Config;

/// <summary>Input bounds shared by native Forms controls and backend acceptance checks.</summary>
public static class CS1SettingsValidation
{
    public static bool TryStationDistance(string text, out double value)
    {
        var parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value) && value >= 0 && value <= 512;
    }
}
