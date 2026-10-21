using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Form;
using CS2MapView.Form.Command;
using System.Reflection;
using System.Xml.Serialization;

internal static class ProjectIdentityChecks
{
    public static void Run()
    {
        Require(typeof(ProjectInfo).Assembly.GetName().Name == "CS1MapView.Core", "Core assembly has the old product name");
        Require(typeof(MainForm).Assembly.GetName().Name == "CS1MapView", "Application assembly has the old product name");
        Require(typeof(MainForm).Assembly.GetName().Version == new Version(0, 1, 3, 0)
            && typeof(ProjectInfo).Assembly.GetName().Version == new Version(0, 1, 3, 0), "Viewer and Core versions must both be 0.1.3");
        Require(typeof(MainForm).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == ProjectInfo.Name,
            "Windows product metadata has the old name");
        Require(HelpCommands.VersionInfoText.Contains(ProjectInfo.DevelopmentDeclaration) &&
                HelpCommands.VersionInfoText.Contains("gansaku") &&
                HelpCommands.VersionInfoText.Contains("CSLMapView") &&
                HelpCommands.VersionInfoText.Contains("CS2MapView"), "About information omits the declaration or upstream credit");
        Require(BetaRenderCommand.Usage.StartsWith("CS1MapView --render"), "CLI help has the old product name");

        var directory = AppContext.BaseDirectory;
        string FilePath(string name) => Path.Combine(directory, name);
        var names = new[] { "CS1MapView.config.xml", "CS1MapView.routes.xml", "CS1MapView.settings",
            "CSLMapView.Beta.config.xml", "CSLMapView.Beta.routes.xml", "CSLMapView.Beta.settings",
            "CS1MapView.identity-check-explicit.xml" };
        var backups = names.ToDictionary(FilePath, name => File.Exists(FilePath(name)) ? File.ReadAllBytes(FilePath(name)) : null);
        try
        {
            foreach (var path in backups.Keys) if (File.Exists(path)) File.Delete(path);
            File.WriteAllText(FilePath("CSLMapView.Beta.config.xml"),
                "<ApplicationConfig><RouteMapConfig><StationClusterRadius>37</StationClusterRadius><StationLinkDistance>68</StationLinkDistance></RouteMapConfig></ApplicationConfig>");
            SaveXml(FilePath("CSLMapView.Beta.routes.xml"), new List<BetaRouteOverride> {
                new() { City = "migration fixture", Id = 7, Name = "Test service", Visible = false, Shorthand = "A" }
            });
            var preferences = UserSettings.Default;
            preferences.OutputImageSize = 1234;
            preferences.SaveAsync(FilePath("CSLMapView.Beta.settings")).GetAwaiter().GetResult();
            var legacy = names.Where(name => name.StartsWith("CSLMapView.Beta."))
                .ToDictionary(FilePath, name => File.ReadAllBytes(FilePath(name)));

            BetaSettings.Load();
            Require(BetaSettings.Config.RouteMapConfig.StationClusterRadius == 37 &&
                    BetaSettings.Config.RouteMapConfig.StationLinkDistance == 68, "Previous cluster/link settings were lost during rename");
            Require(!BetaSettings.RouteOverrides.Single().Visible && BetaSettings.RouteOverrides.Single().Shorthand == "A",
                "Previous route visibility or shorthand was lost during rename");
            preferences = UserSettings.LoadOrDefault(false);
            Require(preferences.OutputImageSize == 1234, "Previous viewer preferences were lost during rename");

            BetaSettings.Config.RouteMapConfig.StationClusterRadius = 91;
            BetaSettings.Config.RouteMapConfig.StationLinkDistance = 152;
            BetaSettings.RouteOverrides.Single().Visible = true;
            BetaSettings.Save();
            preferences.OutputImageSize = 2048;
            preferences.SaveAsync().GetAwaiter().GetResult();
            Require(names.Take(3).All(name => File.Exists(FilePath(name))), "Renamed settings were not saved with the new product filenames");

            BetaSettings.Load();
            Require(BetaSettings.Config.RouteMapConfig.StationClusterRadius == 91 &&
                    BetaSettings.Config.RouteMapConfig.StationLinkDistance == 152 &&
                    BetaSettings.RouteOverrides.Single().Visible, "New settings did not take precedence over previous settings");
            Require(UserSettings.LoadOrDefault(false).OutputImageSize == 2048, "New viewer preferences did not take precedence");

            var explicitConfig = FilePath("CS1MapView.identity-check-explicit.xml");
            File.WriteAllText(explicitConfig,
                "<ApplicationConfig><RouteMapConfig><StationClusterRadius>55</StationClusterRadius></RouteMapConfig></ApplicationConfig>");
            BetaSettings.Load(explicitConfig);
            Require(BetaSettings.Config.RouteMapConfig.StationClusterRadius == 55, "Explicit render settings lost precedence after rename");
            foreach (var (path, bytes) in legacy)
                Require(File.ReadAllBytes(path).SequenceEqual(bytes), "Rename migration modified a previous settings file");
        }
        finally
        {
            foreach (var (path, bytes) in backups)
            {
                if (bytes != null) File.WriteAllBytes(path, bytes);
                else if (File.Exists(path)) File.Delete(path);
            }
        }
        Console.WriteLine("PASS CS1MapView branding, credits, settings migration and preference precedence");
    }

    private static void SaveXml<T>(string path, T value)
    {
        using var stream = File.Create(path);
        new XmlSerializer(typeof(T)).Serialize(stream, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
