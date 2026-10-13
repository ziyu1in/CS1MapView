namespace CS2MapView.Beta;

/// <summary>Product identity and compatibility with settings from the earlier native viewer.</summary>
public static class ProjectInfo
{
    public const string Name = "CS1MapView";
    public const string DevelopmentDeclaration =
        "The author developed CS1MapView entirely through prompts to GPT-6 Astra and GPT-6.1 Sol.";
    public const string UpstreamAcknowledgment =
        "With respect and thanks to gansaku: inspired by the original CSLMapView and built on the CS2MapView codebase.";

    internal static string SettingsPathForRead(string directory, string suffix)
    {
        var current = Path.Combine(directory, Name + "." + suffix);
        var previous = Path.Combine(directory, "CSLMapView.Beta." + suffix);
        return File.Exists(current) || !File.Exists(previous) ? current : previous;
    }
}
