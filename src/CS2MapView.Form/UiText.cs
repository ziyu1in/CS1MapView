using System.Globalization;
using System.Resources;

namespace CS2MapView.Form;

/// <summary>Stable UI text IDs backed by .resx resources and English fallback.</summary>
internal static class UiText
{
    internal static ResourceManager ResourceManager { get; } = new("CS2MapView.Form.Localization.UiText", typeof(UiText).Assembly);

    internal static string? TryGet(string id) => ResourceManager.GetString(id, CultureInfo.CurrentUICulture);

    internal static string Get(string id) => TryGet(id)
        ?? throw new MissingManifestResourceException("Missing UI text ID: " + id);

    internal static string Format(string id, params object?[] values)
        => string.Format(CultureInfo.CurrentCulture, Get(id), values);
}

/// <summary>A localized caption with an independent, stable settings value.</summary>
internal sealed record UiChoice(string Value, string TextId)
{
    public override string ToString() => UiText.Get(TextId);
}
