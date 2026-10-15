using CS2MapView.Beta;
using CS2MapView.Config;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using ApplicationConfig = CS2MapView.CS1.Config.ApplicationConfig;

namespace CS2MapView.Form;

/// <summary>Checks real resource lookup, persisted choices and hidden Forms controls.</summary>
internal static class LocalizationChecks
{
    private static readonly HashSet<string> InheritedMainMenuNames =
    [
        "FileMenuItem", "OpenFileMenuItem", "SaveImageToolStripMenuItem", "CloseMenuItem", "ViewMenuItem",
        "OptionMenuItem", "HelpMenuItem", "GitHubMenuItem", "VersionInfoMenuItem"
    ];

    public static void Run(string root, string reports)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;
        var priorDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var priorDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var priorContext = SynchronizationContext.Current;
        var priorAutoInstall = WindowsFormsSynchronizationContext.AutoInstall;
        _ = UiLocalization.SystemUICulture;
        var priorConfig = BetaSettings.Clone(BetaSettings.Config);
        var priorOverrides = BetaOutput.Clone(BetaSettings.RouteOverrides);
        var settingsPath = Path.Combine(AppContext.BaseDirectory, ProjectInfo.Name + ".settings");
        var routesPath = Path.Combine(AppContext.BaseDirectory, ProjectInfo.Name + ".routes.xml");
        var snapshots = new FileSnapshots([settingsPath, BetaSettings.ConfigPath, routesPath,
            BetaSettings.ConfigPath + ".tmp", routesPath + ".tmp"]);
        WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Directory.CreateDirectory(reports);
            var referencedIds = CheckResources(root);
            CheckStartupCultures();
            CheckSettingsXml(reports);

            BetaSettings.Save(new ApplicationConfig());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            using var main = new MainForm();
            main.Context.UserSettings = UserSettings.Default;
            BetaOutput.ApplyFeatureSettings(main.Context.UserSettings, BetaSettings.Config);
            main.Context.UserSettings.CustomBuildingNameFont = null;
            main.Context.UserSettings.CustomDistrictNameFont = null;
            main.Context.UserSettings.CustomStreetNameFont = null;
            main.Context.UserSettings.UILanguage = "en";
            var englishMenuCaptions = CheckDynamicCaptions(main);
            CheckLanguageOnlyApply(main, settingsPath);
            CheckCancel(main, settingsPath);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            using (var otherCultureMain = new MainForm())
            {
                Require(englishMenuCaptions.SequenceEqual(CheckDynamicCaptions(otherCultureMain)),
                    "Japanese changed new English menu captions or stable menu IDs");
                CheckInheritedJapaneseControls(root, otherCultureMain);
            }
            CheckStableSettingValues(main, settingsPath);
            foreach (var culture in new[] { "zh-Hans", "zh-Hant" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                using var chineseMain = new MainForm();
                Require(MenuIds(englishMenuCaptions).SequenceEqual(MenuIds(CheckDynamicCaptions(chineseMain))),
                    "A Chinese resource changed stable menu IDs");
                CheckChineseControls(root, chineseMain, culture);
                CheckStableSettingValues(main, settingsPath);
            }
            RenderOptionsTab(main, "en", "LanguageConfigTabPage", "localization-language-en.png", reports);
            RenderOptionsTab(main, "ja-JP", "LanguageConfigTabPage", "localization-language-ja.png", reports);
            RenderOptionsTab(main, "ja-JP", "GeneralConfigTabPage", "localization-options-general-ja.png", reports);
            RenderOptionsTab(main, "ja-JP", "LayerConfigTabPage", "localization-options-layers-ja.png", reports);
            foreach (var culture in new[] { "zh-Hans", "zh-Hant" })
            {
                RenderOptionsTab(main, culture, "LanguageConfigTabPage", "localization-language-" + culture + ".png", reports);
                RenderOptionsTab(main, culture, "GeneralConfigTabPage", "localization-options-general-" + culture + ".png", reports);
                RenderOptionsTab(main, culture, "LayerConfigTabPage", "localization-options-layers-" + culture + ".png", reports);
            }
            File.WriteAllText(Path.Combine(reports, "localization-acceptance.txt"),
                $"PASS {referencedIds} direct text IDs, inherited Japanese source/resource matching, Chinese source/embedded values and format placeholders, English fallback, startup/regional independence, saved language Apply/Cancel, stable values, attribution and real Options previews. Chinese wording was not reviewed.");
            Console.WriteLine($"PASS localization: {referencedIds} direct text IDs, startup/persistence and real Language-tab controls");
        }
        finally
        {
            try
            {
                BetaSettings.RouteOverrides.Clear();
                BetaSettings.RouteOverrides.AddRange(priorOverrides);
                BetaSettings.Save(priorConfig);
            }
            finally
            {
                snapshots.Restore();
                CultureInfo.CurrentCulture = priorCulture;
                CultureInfo.CurrentUICulture = priorUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = priorDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = priorDefaultUiCulture;
                WindowsFormsSynchronizationContext.AutoInstall = priorAutoInstall;
                SynchronizationContext.SetSynchronizationContext(priorContext);
            }
        }
    }

    private static int CheckResources(string root)
    {
        var formDirectory = Path.Combine(root, "src", "CS2MapView.Form");
        var english = ReadCatalog(Path.Combine(formDirectory, "Localization", "UiText.resx"));
        Require(english.Count > 100, "The UI catalog was not embedded with the migrated captions");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        Require(UiText.Get("Language_Tab") == "Language", "English Language-tab resource is wrong");
        var calls = new Regex("""UiText\.(?:Get|Format|TryGet)\(\s*"(?<id>[^"]+)"\s*[,)]""");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(formDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(formDirectory, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin") ||
                Path.GetFileName(file) == nameof(LocalizationChecks) + ".cs") continue;
            foreach (Match call in calls.Matches(File.ReadAllText(file)))
            {
                var id = call.Groups["id"].Value;
                Require(english.ContainsKey(id), "Missing English text ID " + id + " referenced by " + relative);
                Require(NormalizeLineEndings(UiText.Get(id)) == NormalizeLineEndings(english[id]), "Embedded text differs from the source catalog: " + id);
                ids.Add(id);
            }
        }
        foreach (var culture in new[] { "en", "ja-JP", "fr-FR" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            foreach (var (id, text) in english)
                Require(NormalizeLineEndings(UiText.Get(id)) == NormalizeLineEndings(text), "A translation was added to new UI text " + id + " under " + culture);
        }
        foreach (var culture in new[] { "zh-Hans", "zh-Hant" })
        {
            var uiPath = Path.Combine(formDirectory, "Localization", "UiText." + culture + ".resx");
            CheckChineseCatalog(Path.Combine(formDirectory, "Localization", "UiText.resx"), uiPath, UiText.ResourceManager, culture);
            foreach (var formType in new[] { typeof(MainForm), typeof(ConfigForm) })
                CheckChineseCatalog(Path.Combine(formDirectory, formType.Name + ".resx"),
                    Path.Combine(formDirectory, formType.Name + "." + culture + ".resx"), new ComponentResourceManager(formType), culture);
            var coreDirectory = Path.Combine(root, "src", "CS2MapView.Core");
            CheckChineseCatalog(Path.Combine(coreDirectory, "Resources.resx"), Path.Combine(coreDirectory, "Resources." + culture + ".resx"),
                new ResourceManager("CS2MapView.Resources", typeof(UserSettings).Assembly), culture);
        }
        var unknown = "__localization_check_missing_text__";
        Require(UiText.TryGet(unknown) == null, "Optional unknown text ID did not return null");
        var missingRejected = false;
        try { UiText.Get(unknown); }
        catch (MissingManifestResourceException) { missingRejected = true; }
        Require(missingRejected, "Required unknown text IDs should fail instead of displaying a key");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        Require(UiText.Format("Format_MapArea", 5.1).Contains("5,1"),
            "Localized text formatting ignored the independent regional number format");
        return ids.Count;
    }

    private static void CheckChineseCatalog(string neutralPath, string localizedPath, ResourceManager manager, string culture)
    {
        var neutral = ReadStringCatalog(neutralPath);
        var localized = ReadStringCatalog(localizedPath);
        var requested = CultureInfo.GetCultureInfo(culture);
        Require(localized.Count > 0, "Chinese resource file has no string values: " + localizedPath);
        foreach (var (id, text) in localized)
        {
            Require(NormalizeLineEndings(manager.GetString(id, requested) ?? "") == NormalizeLineEndings(text),
                "Embedded Chinese value differs from its source: " + Path.GetFileName(localizedPath) + "/" + id);
            if (neutral.TryGetValue(id, out var english)) CheckFormatStructure(english, text, Path.GetFileName(localizedPath) + "/" + id);
        }
        foreach (var (id, english) in neutral)
        {
            var expected = localized.TryGetValue(id, out var text) ? text : english;
            Require(NormalizeLineEndings(manager.GetString(id, requested) ?? "") == NormalizeLineEndings(expected),
                "Chinese resource lookup or English fallback differs from its source: " + Path.GetFileName(localizedPath) + "/" + id);
        }
    }

    private static Dictionary<string, string> ReadStringCatalog(string path) => XDocument.Load(path).Root!.Elements("data")
        .Where(x => x.Attribute("mimetype") == null && (x.Attribute("type") == null || ((string)x.Attribute("type")!).StartsWith("System.String", StringComparison.Ordinal)) &&
                    !((string)x.Attribute("name")!).StartsWith(">>", StringComparison.Ordinal))
        .ToDictionary(x => (string)x.Attribute("name")!, x => (string)x.Element("value")!, StringComparer.Ordinal);

    private static void CheckFormatStructure(string english, string localized, string context)
    {
        var tokens = new Regex(@"\{\{|\}\}|\{(?<index>\d+)(?:,[+-]?\d+)?(?::(?<format>[^{}]*))?\}");
        string[] Signature(string value) => tokens.Matches(value).Where(x => x.Groups["index"].Success)
            .Select(x => x.Groups["index"].Value + ":" + x.Groups["format"].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        var expected = Signature(english);
        var actual = Signature(localized);
        Require(expected.SequenceEqual(actual), "Resource format indices/specifiers differ: " + context);
        if (expected.Length == 0) return;
        _ = System.Text.CompositeFormat.Parse(english);
        _ = System.Text.CompositeFormat.Parse(localized);
    }

    private static Dictionary<string, string> ReadCatalog(string path) => XDocument.Load(path).Root!
        .Elements("data").ToDictionary(x => (string)x.Attribute("name")!, x => (string)x.Element("value")!, StringComparer.Ordinal);

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static void CheckStartupCultures()
    {
        var regional = CultureInfo.GetCultureInfo("fr-FR");
        var simulatedSystem = CultureInfo.GetCultureInfo("de-DE");
        foreach (var preference in new[] { "", "en", "en-US", "fr-FR", "invalid-culture!" })
            Require(UiLocalization.ResolveCulture(preference, simulatedSystem).Name == "en", "Unsupported saved preferences did not resolve to English");
        Require(UiLocalization.ResolveCulture("ja-JP", simulatedSystem).Name == "ja-JP" &&
                UiLocalization.ResolveCulture("ja", simulatedSystem).Name == "ja-JP" &&
                UiLocalization.ResolveCulture(null, CultureInfo.GetCultureInfo("ja-JP")).Name == "ja-JP",
            "The registered Japanese language did not resolve from a saved choice or Japanese system culture");
        foreach (var (alias, canonical) in new[]
        {
            ("zh", "zh-Hans"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-Hans", "zh-Hans"),
            ("zh-TW", "zh-Hant"), ("zh-HK", "zh-Hant"), ("zh-MO", "zh-Hant"), ("zh-Hant", "zh-Hant")
        })
            Require(UiLocalization.ResolveCulture(alias, simulatedSystem).Name == canonical &&
                    UiLocalization.ResolveCulture(null, CultureInfo.GetCultureInfo(alias)).Name == canonical,
                "Chinese culture alias did not resolve to its registered script: " + alias);
        var settings = UserSettings.Default;
        settings.UILanguage = "ja-JP";
        CultureInfo.CurrentCulture = regional;
        CultureInfo.DefaultThreadCurrentCulture = regional;
        UiLocalization.Initialize(settings, []);
        Require(CultureInfo.CurrentUICulture.Name == "ja-JP" && CultureInfo.DefaultThreadCurrentUICulture?.Name == "ja-JP",
            "The saved Japanese language was not applied to the UI cultures");
        Require(CultureInfo.CurrentCulture.Equals(regional) && CultureInfo.DefaultThreadCurrentCulture.Equals(regional),
            "Saved interface language changed regional number/date formatting");
        foreach (var culture in new[] { "zh-Hans", "zh-Hant" })
        {
            settings.UILanguage = culture;
            UiLocalization.Initialize(settings, []);
            Require(CultureInfo.CurrentUICulture.Name == culture && CultureInfo.DefaultThreadCurrentUICulture?.Name == culture &&
                    CultureInfo.CurrentCulture.Equals(regional) && CultureInfo.DefaultThreadCurrentCulture.Equals(regional),
                "Saved Chinese interface language changed regional formats or failed to load: " + culture);
        }
        settings.UILanguage = "ja-JP";
        UiLocalization.Initialize(settings, ["-locale=en-US"]);
        Require(CultureInfo.CurrentUICulture.Name == "en" && CultureInfo.CurrentCulture.Name == "en-US" &&
                CultureInfo.DefaultThreadCurrentUICulture?.Name == "en" && CultureInfo.DefaultThreadCurrentCulture?.Name == "en-US",
            "Explicit English startup locale did not override the saved Japanese language and regional formats");
        settings.UILanguage = "en";
        UiLocalization.Initialize(settings, ["-locale=ja-JP"]);
        Require(CultureInfo.CurrentUICulture.Name == "ja-JP" && CultureInfo.CurrentCulture.Name == "ja-JP" &&
                CultureInfo.DefaultThreadCurrentUICulture?.Name == "ja-JP" && CultureInfo.DefaultThreadCurrentCulture?.Name == "ja-JP",
            "Explicit Japanese startup locale did not override the saved English language and regional formats");
        foreach (var (locale, script) in new[] { ("zh-CN", "zh-Hans"), ("zh-TW", "zh-Hant") })
        {
            UiLocalization.Initialize(settings, ["-locale=" + locale]);
            Require(CultureInfo.CurrentUICulture.Name == script && CultureInfo.DefaultThreadCurrentUICulture?.Name == script &&
                    CultureInfo.CurrentCulture.Name == locale && CultureInfo.DefaultThreadCurrentCulture?.Name == locale,
                "Explicit Chinese startup locale did not keep the regional culture separate from its UI script: " + locale);
        }
        UiLocalization.Initialize(settings, ["-locale=fr-FR"]);
        Require(CultureInfo.CurrentUICulture.Name == "en" && CultureInfo.CurrentCulture.Equals(regional),
            "An unsupported interface locale did not keep English UI with its requested regional formats");
        settings.UILanguage = "";
        UiLocalization.Initialize(settings, []);
        Require(CultureInfo.CurrentUICulture.Equals(UiLocalization.ResolveCulture(null, UiLocalization.SystemUICulture)),
            "Old settings without a saved language did not follow a supported system UI culture");
        settings.UILanguage = "invalid-culture!";
        UiLocalization.Initialize(settings, []);
        Require(CultureInfo.CurrentUICulture.Name == "en", "Invalid persisted startup language did not recover to English");
    }

    private static void CheckSettingsXml(string reports)
    {
        var path = Path.Combine(reports, "localization-settings-roundtrip.xml");
        var settings = UserSettings.Default;
        foreach (var culture in new[] { "en", "ja-JP", "zh-Hans", "zh-Hant" })
        {
            settings.UILanguage = culture;
            settings.SaveAsync(path).GetAwaiter().GetResult();
            var loaded = UserSettings.LoadOrDefault(false, path);
            Require(loaded.UILanguage == culture && loaded.DefaultMapSymbolType == settings.DefaultMapSymbolType &&
                    loaded.LayerDrawingOrder!.Select(x => x.Name).SequenceEqual(settings.LayerDrawingOrder!.Select(x => x.Name)),
                "Language settings XML round trip changed the language or existing settings values: " + culture);
        }
        var old = XDocument.Load(path);
        old.Root!.Element(nameof(UserSettings.UILanguage))!.Remove();
        var oldPath = Path.Combine(reports, "localization-settings-legacy.xml");
        old.Save(oldPath);
        var legacy = UserSettings.LoadOrDefault(false, oldPath);
        Require(string.IsNullOrEmpty(legacy.UILanguage), "Old settings without UILanguage did not retain backward compatibility");
        UiLocalization.Initialize(legacy, []);
        Require(CultureInfo.CurrentUICulture.Equals(UiLocalization.ResolveCulture(null, UiLocalization.SystemUICulture)),
            "Legacy settings did not initialize the appropriate supported system interface language");
    }

    private static string[] CheckDynamicCaptions(MainForm main)
    {
        var menus = Menus(main.MainMenuStrip!.Items).ToArray();
        Require(menus.First(x => x.Name == "CS1StyleMenu").Text == UiText.Get("Menu_MapStyle"), "Dynamic map-style menu bypassed resource lookup");
        foreach (var name in new[] { "AppleRailAppearanceMenu", "GoogleRailAppearanceMenu", "StationAreaColor_Red", "StationAreaColor_Blue", "Zoom85Menu", "Rotate90Menu", "Diagnostic_TransportOverview" })
            Require(menus.Any(x => x.Name == name && !string.IsNullOrWhiteSpace(x.Text)), "Missing localized dynamic menu caption: " + name);
        foreach (var kind in Enum.GetValues<BetaDiagnosticKind>())
            Require(!string.IsNullOrWhiteSpace(UiText.Get("Diagnostic_" + kind)), "Missing dynamic diagnostic title: " + kind);
        using var features = new BetaFeatureForm(main);
        var grid = Descendants(features).OfType<PropertyGrid>().Single();
        CheckProperties(grid.SelectedObject!);
        return menus.Where(x => !InheritedMainMenuNames.Contains(x.Name ?? ""))
            .Select(x => (x.Name ?? "") + "\t" + x.Text).Order(StringComparer.Ordinal).ToArray();
    }

    private static void CheckInheritedJapaneseControls(string root, MainForm main)
    {
        var japanese = CultureInfo.GetCultureInfo("ja-JP");
        var directory = Path.Combine(root, "src", "CS2MapView.Form");
        var mainTexts = CheckInheritedJapaneseResources(typeof(MainForm), Path.Combine(directory, "MainForm.ja-JP.resx"));
        var menus = Menus(main.MainMenuStrip!.Items).ToArray();
        foreach (var (id, text) in mainTexts)
        {
            if (id == "$this.Text") continue; // The product title is deliberately rebranded at runtime.
            var name = id[..^".Text".Length];
            var liveText = menus.SingleOrDefault(x => x.Name == name)?.Text
                ?? Descendants(main).SingleOrDefault(x => x.Name == name)?.Text;
            Require(liveText == text, "Inherited Japanese MainForm caption did not match its upstream resource: " + id);
        }
        Require(menus.Single(x => x.Name == "FileMenuItem").Text !=
                new ComponentResourceManager(typeof(MainForm)).GetString("FileMenuItem.Text", CultureInfo.GetCultureInfo("en")),
            "Inherited Japanese main menus still displayed their English captions");

        var configTexts = CheckInheritedJapaneseResources(typeof(ConfigForm), Path.Combine(directory, "ConfigForm.ja-JP.resx"));
        var changedCaptions = new Dictionary<string, string>
        {
            ["RenderAirplaneLinesCheckbox"] = "Options_BlimpRoutes", ["RenderAirplaneStopsCheckbox"] = "Options_BlimpStops",
            ["RenderShipLinesCheckbox"] = "Options_FerryRoutes", ["RenderShipStopsCheckbox"] = "Options_FerryHarbours"
        };
        using var options = NewOptions(main);
        foreach (var (id, text) in configTexts)
        {
            if (id == "$this.Text") continue;
            var name = id[..^".Text".Length];
            var control = Descendants(options).Single(x => x.Name == name);
            var expected = changedCaptions.TryGetValue(name, out var textId) ? UiText.Get(textId) : text;
            Require(control.Text == expected, "Inherited Japanese Options caption or changed English caption is wrong: " + id);
        }
        Require(Control<TabPage>(options, "GeneralConfigTabPage").Text ==
                new ComponentResourceManager(typeof(ConfigForm)).GetString("GeneralConfigTabPage.Text", japanese),
            "Inherited Japanese General page was not localized");
        Require(Control<TabPage>(options, "LanguageConfigTabPage").Text == UiText.Get("Language_Tab") &&
                Control<Label>(options, "LanguageAttributionLabel").Text == UiText.Get("Language_Attribution"),
            "New Language-tab controls did not retain their English text and attribution");
        var columns = Control<DataGridView>(options, "LayerConfigGridView").Columns;
        Require(columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Name").HeaderText == UiText.Get("Options_LayerName") &&
                columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Visible").HeaderText == UiText.Get("Options_LayerVisible") &&
                columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Order").HeaderText == UiText.Get("Options_LayerOrder"),
            "New layer-column captions did not remain English under Japanese");
    }

    private static Dictionary<string, string> CheckInheritedJapaneseResources(Type formType, string sourcePath)
    {
        var texts = ReadCatalog(sourcePath).Where(x => x.Key.EndsWith(".Text", StringComparison.Ordinal))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var resources = new ComponentResourceManager(formType);
        var japanese = CultureInfo.GetCultureInfo("ja-JP");
        Require(texts.Count > 5, "Inherited Japanese resource catalog is missing: " + sourcePath);
        foreach (var (id, text) in texts)
            Require(NormalizeLineEndings(resources.GetString(id, japanese) ?? "") == NormalizeLineEndings(text),
                "Embedded Japanese text differs from the unchanged upstream source: " + formType.Name + "." + id);
        return texts;
    }

    private static void CheckChineseControls(string root, MainForm main, string culture)
    {
        var directory = Path.Combine(root, "src", "CS2MapView.Form");
        var mainTexts = ReadStringCatalog(Path.Combine(directory, "MainForm." + culture + ".resx"));
        main.OnZoomOrViewPositionChanged();
        var toolItems = Menus(main.MainMenuStrip!.Items).Cast<ToolStripItem>().Concat(
            Descendants(main).OfType<ToolStrip>().SelectMany(x => x.Items.Cast<ToolStripItem>())).ToArray();
        foreach (var (id, text) in mainTexts.Where(x => x.Key.EndsWith(".Text", StringComparison.Ordinal)))
        {
            if (id == "$this.Text") continue; // MainForm retains the runtime product title.
            var name = id[..^".Text".Length];
            var liveText = toolItems.FirstOrDefault(x => x.Name == name)?.Text
                ?? Descendants(main).SingleOrDefault(x => x.Name == name)?.Text;
            var expected = name == "StatusBarDisplayInfoLabel"
                ? UiText.Format("Status_ViewInfo", main.Context.ViewContext.ScaleFactor * 100, main.Context.ViewContext.Angle * 180 / Math.PI, "")
                : text;
            Require(NormalizeLineEndings(liveText ?? "") == NormalizeLineEndings(expected),
                "Chinese MainForm control differs from its source value: " + culture + "/" + id);
        }
        var changedCaptions = new Dictionary<string, string>
        {
            ["RenderAirplaneLinesCheckbox"] = "Options_BlimpRoutes", ["RenderAirplaneStopsCheckbox"] = "Options_BlimpStops",
            ["RenderShipLinesCheckbox"] = "Options_FerryRoutes", ["RenderShipStopsCheckbox"] = "Options_FerryHarbours"
        };
        using var options = NewOptions(main);
        var configTexts = ReadStringCatalog(Path.Combine(directory, "ConfigForm." + culture + ".resx"));
        foreach (var (id, text) in configTexts.Where(x => x.Key.EndsWith(".Text", StringComparison.Ordinal)))
        {
            var name = id[..^".Text".Length];
            var actual = id == "$this.Text" ? options.Text : Descendants(options).Single(x => x.Name == name).Text;
            var expected = changedCaptions.TryGetValue(name, out var textId) ? UiText.Get(textId) : text;
            Require(NormalizeLineEndings(actual) == NormalizeLineEndings(expected),
                "Chinese Options control differs from its source/resource value: " + culture + "/" + id);
        }
        Require(Control<TabPage>(options, "LanguageConfigTabPage").Text == UiText.Get("Language_Tab") &&
                Control<Label>(options, "LanguageAttributionLabel").Text == UiText.Get("Language_Attribution"),
            "Chinese Language tab did not use the resource catalog and fallback");
        var columns = Control<DataGridView>(options, "LayerConfigGridView").Columns;
        Require(columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Name").HeaderText == UiText.Get("Options_LayerName") &&
                columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Visible").HeaderText == UiText.Get("Options_LayerVisible") &&
                columns.Cast<DataGridViewColumn>().Single(x => x.DataPropertyName == "Order").HeaderText == UiText.Get("Options_LayerOrder"),
            "Chinese layer columns did not use their resource IDs");
    }

    private static string[] MenuIds(IEnumerable<string> captions) => captions.Select(x => x[..x.IndexOf('\t')])
        .Order(StringComparer.Ordinal).ToArray();

    private static void CheckProperties(object value)
    {
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value))
        {
            Require(property.DisplayName == UiText.Get("Feature_Property_" + property.Name), "Property display name bypassed localization: " + property.Name);
            Require(!string.IsNullOrWhiteSpace(property.Category), "Localized feature category is empty: " + property.Name);
            _ = property.Description;
            if (property.GetValue(value) is FeatureObject nested) CheckProperties(nested);
        }
    }

    private static void CheckLanguageOnlyApply(MainForm main, string settingsPath)
    {
        var settings = main.Context.UserSettings;
        foreach (var (previous, selected) in new (string?, string)[]
        {
            (null, "ja-JP"), ("ja-JP", "en"), ("en", "zh-Hans"), ("zh-Hans", "zh-Hant"), ("zh-Hant", "ja-JP"), ("zh-TW", "zh-Hans")
        })
        {
            settings.UILanguage = previous;
            settings.SaveAsync().GetAwaiter().GetResult();
            var before = Serialize(settings);
            using var options = NewOptions(main);
            var languages = Control<ComboBox>(options, "UILanguageCombobox");
            Require(languages.Items.Cast<UiChoice>().Select(x => x.Value).SequenceEqual(new[] { "en", "ja-JP", "zh-Hans", "zh-Hant" }),
                "Language selector does not offer the four registered languages");
            Require(((UiChoice)languages.SelectedItem!).Value == UiLocalization.ResolveCulture(previous, UiLocalization.SystemUICulture).Name,
                "Language tab did not load the effective saved or system language");
            Require(languages.Items.OfType<UiChoice>().Single(x => x.Value == "ja-JP").ToString() == UiText.Get("Language_JapanesePartial"),
                "Partial Japanese language choice is missing its English label");
            Require(languages.Items.OfType<UiChoice>().Single(x => x.Value == "zh-Hans").ToString() == UiText.Get("Language_ChineseSimplified") &&
                    languages.Items.OfType<UiChoice>().Single(x => x.Value == "zh-Hant").ToString() == UiText.Get("Language_ChineseTraditional"),
                "Chinese language choices did not use their text IDs");
            SelectChoice(languages, selected);
            options.ApplyConfigurationAsync().GetAwaiter().GetResult();
            Require(settings.UILanguage == selected && UserSettings.LoadOrDefault(false, settingsPath).UILanguage == selected,
                "Applying only a language choice did not persist its culture value");
            var after = Serialize(settings);
            before.Root!.Element(nameof(UserSettings.UILanguage))?.Remove();
            after.Root!.Element(nameof(UserSettings.UILanguage))?.Remove();
            Require(XNode.DeepEquals(before, after), "Applying only the language changed unrelated user settings");
            Require(CultureInfo.CurrentUICulture.Name == "en", "Language Apply changed the current UI before restart");
        }
    }

    private static void CheckCancel(MainForm main, string settingsPath)
    {
        foreach (var (saved, selected) in new[] { ("ja-JP", "zh-Hans"), ("zh-Hans", "zh-Hant"), ("zh-Hant", "en") })
        {
            main.Context.UserSettings.UILanguage = saved;
            main.Context.UserSettings.SaveAsync().GetAwaiter().GetResult();
            var settingsBefore = Serialize(main.Context.UserSettings);
            var fileBefore = File.ReadAllBytes(settingsPath);
            var configBefore = Serialize(BetaSettings.Config);
            using var options = NewOptions(main);
            SelectChoice(Control<ComboBox>(options, "UILanguageCombobox"), selected);
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Control<Button>(options, "FormCancelButton"), [EventArgs.Empty]);
            Require(options.DialogResult == DialogResult.Cancel && XNode.DeepEquals(settingsBefore, Serialize(main.Context.UserSettings)) &&
                    XNode.DeepEquals(configBefore, Serialize(BetaSettings.Config)) && fileBefore.SequenceEqual(File.ReadAllBytes(settingsPath)),
                "Cancel changed the settings object, configuration or persisted preference: " + saved);
        }
    }

    private static void CheckStableSettingValues(MainForm main, string settingsPath)
    {
        var font = CS2MapView.Theme.StringTheme.GetAvailableFonts().First();
        var settings = main.Context.UserSettings;
        settings.CustomBuildingNameFont = settings.CustomDistrictNameFont = settings.CustomStreetNameFont = font;
        BetaSettings.Config.BuildingFont = BetaSettings.Config.DistrictFont = BetaSettings.Config.RoadNameFont = font;
        using (var options = NewOptions(main))
        {
            var contours = Control<ComboBox>(options, "ContourIntervalCombobox");
            Require(contours.Items.Cast<UiChoice>().Select(x => x.Value).SequenceEqual(new[] { "None", "Default", "Low" }),
                "Localized contour choices changed their stable values");
            SelectChoice(contours, "Low");
            Require(contours.SelectedItem!.ToString() == UiText.Get("Options_ContourLow"), "Contour choice did not use its text ID");
            foreach (var name in new[] { "BuildingFontCombobox", "DistrictFontCombobox", "StreetFontCombobox" })
            {
                var fonts = Control<ComboBox>(options, name);
                fonts.SelectedIndex = 0;
                Require(fonts.SelectedItem is UiChoice { Value: "" } && fonts.SelectedItem.ToString() == UiText.Get("Options_UseDefaultFont"),
                    "Default-font choice is not a localized caption with an independent value");
            }
            options.ApplyConfigurationAsync().GetAwaiter().GetResult();
        }
        var loaded = UserSettings.LoadOrDefault(false, settingsPath);
        Require(BetaSettings.Config.DisplayContourLines && BetaSettings.Config.ContourIntervals == "Low" &&
                XDocument.Load(BetaSettings.ConfigPath).Root!.Element("ContourIntervals")!.Value == "Low",
            "Applying contour choices changed the stable Low value");
        Require(loaded.CustomBuildingNameFont == null && loaded.CustomDistrictNameFont == null && loaded.CustomStreetNameFont == null,
            "Applying default fonts persisted the UI caption as a font family");
        using var reopened = NewOptions(main);
        Require(Control<ComboBox>(reopened, "ContourIntervalCombobox").SelectedItem is UiChoice { Value: "Low" } &&
                Control<ComboBox>(reopened, "BuildingFontCombobox").SelectedItem is UiChoice { Value: "" },
            "Reopening localized options lost stable contour/default-font choices");
    }

    private static ConfigForm NewOptions(MainForm main)
    {
        var options = new ConfigForm { AppRoot = main };
        options.InitializeForChecks().GetAwaiter().GetResult();
        return options;
    }

    private static void RenderOptionsTab(MainForm main, string culture, string pageName, string fileName, string reports)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        main.Context.UserSettings.UILanguage = culture;
        using var options = new OffscreenConfigForm
        {
            AppRoot = main, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000)
        };
        try
        {
            // Show outside the desktop so WinForms creates and paints actual child controls.
            // Let the normal Load event initialize the form exactly once.
            options.Show();
            var tabs = Control<TabControl>(options, "ConfigTab");
            var page = tabs.TabPages.Cast<TabPage>().Single(x => x.Name == pageName);
            tabs.SelectedTab = page;
            options.PerformLayout();
            page.PerformLayout();
            System.Windows.Forms.Application.DoEvents();
            options.Update();
            Require(page.Visible && Control<Button>(options, "SubmitButton").Visible,
                "Real Options page did not display its controls and Submit button");
            using var bitmap = new Bitmap(options.Width, options.Height);
            options.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            if (pageName == "LanguageConfigTabPage")
            {
                var hint = Control<Label>(page, "LanguageRestartHintLabel");
                var selector = Control<ComboBox>(page, "UILanguageCombobox");
                var attribution = Control<Label>(page, "LanguageAttributionLabel");
                Require(page.Text == UiText.Get("Language_Tab") && hint.Text == UiText.Get("Language_RestartHint") &&
                        attribution.Text == UiText.Get("Language_Attribution") && hint.Visible && selector.Visible && attribution.Visible,
                    "Real Language tab did not display its resource-backed controls and attribution");
                if (culture is "en" or "ja-JP")
                    Require(attribution.Text.Contains("gansaku") && attribution.Text.Contains("MIT"),
                        "English/Japanese Language tab is missing its existing attribution");
                Require(CountDarkPixels(bitmap, CaptureBounds(options, hint)) > 30 &&
                        CountDarkPixels(bitmap, CaptureBounds(options, selector)) > 30 &&
                        CountDarkPixels(bitmap, CaptureBounds(options, attribution)) > 30,
                    "Language-tab preview did not contain its actual hint, selector and attribution");
            }
            else
            {
                var evidence = pageName == "GeneralConfigTabPage" ? Control<Control>(page, "ThemeLabel")
                    : Control<Control>(page, "RenderRailwaysCheckbox");
                var expected = new ComponentResourceManager(typeof(ConfigForm)).GetString(evidence.Name + ".Text", CultureInfo.GetCultureInfo(culture));
                Require(evidence.Visible && evidence.Text == expected && CountDarkPixels(bitmap, CaptureBounds(options, evidence)) > 15,
                    "Localized Options preview did not display its source resource caption: " + evidence.Name);
            }
            bitmap.Save(Path.Combine(reports, fileName), ImageFormat.Png);
            if (pageName == "LayerConfigTabPage") CheckModeControlBounds(options, culture, reports);
        }
        finally { options.Hide(); }
    }

    private static void CheckModeControlBounds(ConfigForm options, string culture, string reports)
    {
        var rows = new List<string> { "Group\tControl\tText\tLeft\tTop\tWidth\tHeight" };
        var overlaps = new List<string>();
        foreach (var groupName in new[] { "groupBox5", "groupBox6" })
        {
            var group = Control<GroupBox>(options, groupName);
            var checks = group.Controls.OfType<CheckBox>().Where(x => x.Visible).ToArray();
            foreach (var check in checks)
                rows.Add(string.Join("\t", groupName, check.Name, check.Text, check.Left, check.Top, check.Width, check.Height));
            for (var i = 0; i < checks.Length; i++)
                for (var j = i + 1; j < checks.Length; j++)
                    if (checks[i].Bounds.IntersectsWith(checks[j].Bounds))
                        overlaps.Add(checks[i].Name + " " + checks[i].Bounds + " overlaps " + checks[j].Name + " " + checks[j].Bounds);
        }
        File.WriteAllLines(Path.Combine(reports, "localization-mode-control-bounds-" + culture + ".tsv"), rows);
        File.WriteAllLines(Path.Combine(reports, "localization-mode-control-overlaps-" + culture + ".txt"), overlaps);
        if (culture.StartsWith("zh", StringComparison.Ordinal))
            Console.WriteLine("Chinese layout report: " + culture + ", " + overlaps.Count + " intersecting mode-control pairs");
        else Require(overlaps.Count == 0, "Visible transit captions overlap: " + string.Join("; ", overlaps));
    }

    private static Rectangle CaptureBounds(System.Windows.Forms.Form form, Control control)
    {
        var screen = control.PointToScreen(Point.Empty);
        return new Rectangle(screen.X - form.Left, screen.Y - form.Top, control.Width, control.Height);
    }

    private static int CountDarkPixels(Bitmap bitmap, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(Point.Empty, bitmap.Size));
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 160 && pixel.G < 160 && pixel.B < 160) count++;
            }
        return count;
    }

    private sealed class OffscreenConfigForm : ConfigForm
    {
        protected override bool ShowWithoutActivation => true;
    }

    private static XDocument Serialize<T>(T value)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new XmlSerializer(typeof(T)).Serialize(writer, value);
        return XDocument.Parse(writer.ToString());
    }

    private static IEnumerable<ToolStripMenuItem> Menus(ToolStripItemCollection items)
    {
        foreach (var item in items.OfType<ToolStripMenuItem>())
        {
            yield return item;
            foreach (var child in Menus(item.DropDownItems)) yield return child;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T Control<T>(Control root, string name) where T : Control => Descendants(root).OfType<T>().Single(x => x.Name == name);
    private static void SelectChoice(ComboBox combo, string value) => combo.SelectedItem = combo.Items.OfType<UiChoice>().Single(x => x.Value == value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class FileSnapshots(IEnumerable<string> paths)
    {
        private readonly Dictionary<string, byte[]?> _files = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null, StringComparer.OrdinalIgnoreCase);

        public void Restore()
        {
            foreach (var (path, bytes) in _files)
            {
                if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllBytes(path, bytes);
            }
        }
    }
}
