using System.ComponentModel;
using System.Globalization;
using CS2MapView.CS1.Config;

namespace CS2MapView.Form;

internal static class ContourDetailChoices
{
    internal static UiChoice[] Items => [
        new("Fine", "Options_ContourFine"), new("Balanced", "Options_ContourBalanced"), new("Sparse", "Options_ContourSparse")
    ];
    internal static string Caption(ContourDetail detail) => Items.Single(x => x.Value == detail.ToString()).ToString();
}

internal sealed class ContourDetailConverter() : EnumConverter(typeof(ContourDetail))
{
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is ContourDetail detail
            ? ContourDetailChoices.Caption(detail) : base.ConvertTo(context, culture, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            var choice = ContourDetailChoices.Items.FirstOrDefault(x => x.ToString() == text);
            if (choice is not null) return Enum.Parse<ContourDetail>(choice.Value);
        }
        return base.ConvertFrom(context, culture, value);
    }
}
