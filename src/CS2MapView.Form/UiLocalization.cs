using CS2MapView.Config;
using System.Globalization;

namespace CS2MapView.Form;

internal static class UiLocalization
{
    internal static readonly UiChoice[] Languages =
    [
        new("en", "Language_English"),
        new("ja-JP", "Language_JapanesePartial"),
        new("zh-Hans", "Language_ChineseSimplified"),
        new("zh-Hant", "Language_ChineseTraditional")
    ];

    // Capture the operating-system preference before applying an explicit UI choice.
    internal static CultureInfo SystemUICulture { get; } = CultureInfo.CurrentUICulture;

    internal static CultureInfo ResolveCulture(string? preference, CultureInfo systemCulture)
    {
        var requested = string.IsNullOrWhiteSpace(preference) ? systemCulture.Name : preference;
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(requested); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
        // Only registered interface languages are enabled. Walk regional parents
        // (for example en-US -> en), then use the neutral English language.
        for (; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            if (Languages.Any(x => string.Equals(x.Value, culture.Name, StringComparison.OrdinalIgnoreCase))) return culture;
            var regionalChoice = Languages.FirstOrDefault(x => string.Equals(
                CultureInfo.GetCultureInfo(x.Value).Parent.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
            if (regionalChoice != null) return CultureInfo.GetCultureInfo(regionalChoice.Value);
        }
        return CultureInfo.GetCultureInfo("en");
    }

    internal static void Initialize(UserSettings settings, IEnumerable<string> args)
    {
        var locale = args.FirstOrDefault(a => a.StartsWith("-locale=", StringComparison.Ordinal));
        var requested = locale == null ? settings.UILanguage : locale[(locale.IndexOf('=') + 1)..];
        var uiCulture = ResolveCulture(requested, SystemUICulture);
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        // Preserve the established -locale switch's number-formatting behavior.
        // The Language tab changes UI language only, leaving regional formats alone.
        if (locale != null)
        {
            var culture = CultureInfo.GetCultureInfo(requested!);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
        }
    }
}
