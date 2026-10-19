using CS2MapView.Beta;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CSLMapView.XML;
using SkiaSharp;
using System.Collections;
using System.ComponentModel;
using System.IO.Compression;

namespace CS2MapView.Form;

/// <summary>Exercises the real inherited Forms controls and isolated output without displaying windows.</summary>
internal static class FormsUiPortChecks
{
    public static void Run(BetaOutputHost sourceHost, CSLExportXML raw, string reports)
    {
        var priorContext = SynchronizationContext.Current;
        var priorAutoInstall = WindowsFormsSynchronizationContext.AutoInstall;
        var priorConfig = BetaSettings.Clone(BetaSettings.Config);
        var priorOverrides = BetaOutput.Clone(BetaSettings.RouteOverrides);
        WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var main = new MainForm();
            var config = BetaSettings.Clone(BetaMapSession.Get(sourceHost, raw).Config);
            main.Context.UserSettings = BetaOutput.Clone(sourceHost.Context.UserSettings);
            main.Context.ViewContext = sourceHost.Context.ViewContext.Clone();
            var source = new CS1MapType(raw);
            main.Context.SourceMapType = source;
            BetaMapSession.SetConfigForHost(main, config);
            main.MapData = source.BuildAll(main, null).GetAwaiter().GetResult();
            var session = main.Session!;
            Require(main.Text.StartsWith(ProjectInfo.Name + " —") && main.Text.Contains("Skylines 1"), "MainForm did not identify CS1MapView");

            main.SynchronizeFeatureMenus();
            foreach (var required in new[] { "CS1LayerMenu", "CS1TransitMenu", "CS1RailMenu", "RenderAreaMenu", "RotationMenu", "ZoomMenu", "FitMapMenu", "CS1ListsMenu", "ServiceListMenu", "BuildingListMenu", "SetOutputAreaMenu", "ClearOutputAreaMenu", "ChooseOutputAreaMenu", "CS1StyleMenu", "StationAreasMenu", "StationAreaVisibleMenu", "CS1DebugMenu", "Zoom85Menu" })
                Require(Menus(main.MainMenuStrip!.Items).Any(x => x.Name == required), "Missing direct menu: " + required);
            Require(Menus(main.MainMenuStrip!.Items).First(x => x.Name == "ForestLayerMenu").Checked == session.Config.RenderForest, "Layer menu check state did not reflect settings");
            Require(Menus(main.MainMenuStrip!.Items).First(x => x.Name == "StationModeLettersMenu").Checked == session.Config.RouteMapConfig.ShowStationModeBadges, "Mode-letter menu check state did not reflect settings");
            var forestBefore = session.Config.RenderForest;
            Menus(main.MainMenuStrip!.Items).First(x => x.Name == "ForestLayerMenu").PerformClick();
            main.LastMenuAction.GetAwaiter().GetResult();
            Require(Menus(main.MainMenuStrip!.Items).First(x => x.Name == "ForestLayerMenu").Checked == !forestBefore, "Menu action did not update checked state");
            Require(main.MapData!.Layers.Any(x => x.LayerName == "forest") == !forestBefore, "Menu action did not update actual layer visibility");
            Menus(main.MainMenuStrip!.Items).First(x => x.Name == "ForestLayerMenu").PerformClick();
            main.LastMenuAction.GetAwaiter().GetResult();
            CheckRestoredNavigation(main);
            var beforeRail = session.Config.RenderRail;
            var beforeRoutes = session.Config.RenderTransportRoute;
            var beforeStations = session.Config.RenderTransport;
            var manualRoute = session.TransportManager.TransportList.First();
            var manualVisible = manualRoute.ViewerVisible;
            manualRoute.ViewerVisible = false;
            session.SaveRouteOverrides();
            session.HighlightGroup(session.TransportManager.SelectableStations.First(x => x.Stops.Count > 0).Id);
            Menus(main.MainMenuStrip!.Items).First(x => x.Name == "InfrastructureOnlyMenu").PerformClick();
            main.LastMenuAction.GetAwaiter().GetResult();
            Require(session.Config.Railway.InfrastructureOnly && session.SelectedGroupId == null &&
                    main.MapData!.Layers.Any(x => x.LayerName == ILayer.LayerNameRailways) &&
                    !main.MapData.Layers.Any(x => x.LayerName == ILayer.LayerNameTransportLines),
                    "Infrastructure-only menu did not clear highlight and switch actual layers");
            Menus(main.MainMenuStrip!.Items).First(x => x.Name == "InfrastructureOnlyMenu").PerformClick();
            main.LastMenuAction.GetAwaiter().GetResult();
            Require(!session.Config.Railway.InfrastructureOnly && session.Config.RenderRail == beforeRail &&
                    session.Config.RenderTransportRoute == beforeRoutes && session.Config.RenderTransport == beforeStations &&
                    !session.TransportManager.TransportList.First(x => x.Id == manualRoute.Id).ViewerVisible,
                    "Leaving infrastructure-only lost prior layer choices or restored a manually hidden service");
            session.TransportManager.TransportList.First(x => x.Id == manualRoute.Id).ViewerVisible = manualVisible;
            session.SaveRouteOverrides();

            var beforePremises = session.Config.RenderStationPremises;
            var beforeFont = session.Config.BuildingFont;
            main.ApplyMenuFeatureAsync(() => { session.Config.RenderRail = session.Config.RenderTransportRoute = session.Config.RenderTransport = false; session.Config.RenderStationPremises = true; }).GetAwaiter().GetResult();
            using (var gatedOptions = new ConfigForm { AppRoot = main })
            {
                gatedOptions.InitializeForChecks().GetAwaiter().GetResult();
                Control<ComboBox>(gatedOptions, "BuildingFontCombobox").Text = "Arial";
                gatedOptions.ApplyConfigurationAsync().GetAwaiter().GetResult();
            }
            Require(session.Config.RenderStationPremises && HasStationAreaGeometry(main), "Unrelated options erased or hid station areas while rail and transit layers were hidden");
            var stationToggle = Menus(main.MainMenuStrip!.Items).First(x => x.Name == "StationAreaVisibleMenu");
            var layerStationToggle = Menus(main.MainMenuStrip.Items).First(x => x.Name == "StationPremisesLayerMenu");
            Require(stationToggle.Checked && layerStationToggle.Checked, "Independent station-area menus lost their checked state");
            stationToggle.PerformClick(); main.LastMenuAction.GetAwaiter().GetResult();
            Require(!session.Config.RenderStationPremises && !HasStationAreaGeometry(main) && !stationToggle.Checked && !layerStationToggle.Checked &&
                    !session.Config.RenderRail && !session.Config.RenderTransport && !session.Config.RenderTransportRoute,
                "Turning station areas off changed independent rail/transit preferences or left area geometry visible");
            layerStationToggle.PerformClick(); main.LastMenuAction.GetAwaiter().GetResult();
            Require(session.Config.RenderStationPremises && HasStationAreaGeometry(main) && stationToggle.Checked && layerStationToggle.Checked &&
                    !session.Config.RenderRail && !session.Config.RenderTransport && !session.Config.RenderTransportRoute,
                "Turning station areas on did not restore actual areas independently of hidden rail/transit");
            Require(stationToggle.OwnerItem?.Name == "StationAreasMenu" &&
                    Menus(main.MainMenuStrip.Items).First(x => x.Name == "FrameStopTracksMenu").OwnerItem?.Name == "StationAreasMenu",
                "Station-area controls remain under rail infrastructure");
            session.Config.RenderRail = beforeRail;
            session.Config.RenderTransportRoute = beforeRoutes;
            session.Config.RenderTransport = beforeStations;
            session.Config.RenderStationPremises = beforePremises;
            session.Config.BuildingFont = beforeFont;
            main.ApplyFeaturesAsync().GetAwaiter().GetResult();

            var priorAreaAngle = main.Context.ViewContext.Angle;
            main.Context.ViewContext.Angle = (float)Math.PI / 12;
            var chosenArea = SKRect.Create(660, 1780, 460, 340);
            main.OutputArea = chosenArea;
            main.Context.ViewContext.Angle += (float)Math.PI / 2;
            var expectedArea = CS2MapView.Util.SKPathEx.GetTransformedRectBound(chosenArea, SKMatrix.CreateRotation((float)Math.PI / 2));
            Require(Near(main.OutputArea!.Value, expectedArea), "Rotating the map moved the selected print/export geography");
            var previewBeforeView = main.Context.ViewContext.ViewTransform;
            var previewBeforeLayers = main.MapData!.Layers.ToArray();
            var previewBeforeArea = main.OutputArea.Value;
            using (var preview = new SaveImageForm(true) { AppRoot = main })
            {
                preview.InitializeForChecks().GetAwaiter().GetResult();
                Require(preview.PreviewAreaForChecks.Width > 0 && preview.PreviewAreaForChecks.Height > 0 &&
                        Near(preview.PreviewCropForChecks, previewBeforeArea), "Image preview did not initialise from the selected print/export area");
                CheckAreaSelection(main, preview);
                main.OutputArea = previewBeforeArea;
            }
            Require(main.Context.ViewContext.ViewTransform.Equals(previewBeforeView) && previewBeforeLayers.SequenceEqual(main.MapData.Layers) &&
                    Near(main.OutputArea!.Value, previewBeforeArea), "Preview changed the screen layers, view or selected output area");
            CheckPreviewClosure(main);
            main.OutputArea = null;
            main.Context.ViewContext.Angle = priorAreaAngle;
            var nativeCopy = BetaSettings.Clone(session.Config);
            Require(nativeCopy.RouteMapConfig.StationClusterRadius == session.Config.RouteMapConfig.StationClusterRadius &&
                    nativeCopy.RouteMapConfig.StationLinkDistance == session.Config.RouteMapConfig.StationLinkDistance &&
                    nativeCopy.Railway.Appearance == session.Config.Railway.Appearance, "Native settings round trip changed display choices");
            var migrationFixture = Path.Combine(reports, "native-settings-migration.xml");
            File.WriteAllText(migrationFixture, "<ApplicationConfig><SelectedStyle>apple-light</SelectedStyle><RenderForest>true</RenderForest><Railway><Appearance>Google</Appearance><SeparateTracks>false</SeparateTracks></Railway><RouteMapConfig><StationClusterRadius>95</StationClusterRadius><StationLinkDistance>110</StationLinkDistance><RenderTrainStation>true</RenderTrainStation></RouteMapConfig><BuildingPreferences><Asset steamId='123' name='Sample Asset' hide='true' nameVisibility='FullVisible' symbol='hospital'/></BuildingPreferences><IgnoreBuildingShape><ByName><NameRegex>^Sample Asset$</NameRegex></ByName><ByItemClass/></IgnoreBuildingShape></ApplicationConfig>");
            var migrated = BetaSettings.ImportConfig(migrationFixture);
            Require(migrated.Railway.Appearance == CS2MapView.CS1.Config.RailwayAppearance.Google && !migrated.Railway.SeparateTracks &&
                    migrated.RouteMapConfig.StationClusterRadius == 95 && migrated.RouteMapConfig.StationLinkDistance == 110 &&
                    migrated.RouteMapConfig.RenderTrainStation, "Stable configuration migration lost rail or station choices");
            Require(migrated.BuildingPreferences.Single().HideShape == true &&
                    migrated.BuildingPreferences.Single().NameVisibility == CS2MapView.Config.NameVisibility.FullVisible &&
                    migrated.IgnoreBuildingShape.IsHidden("Sample Asset", ""), "Stable building preferences or ignore rules did not migrate to published Core models");
            var migrationRoundTrip = BetaSettings.Clone(migrated);
            Require(migrationRoundTrip.BuildingPreferences.Single().HideShape == true && migrationRoundTrip.IgnoreBuildingShape.IsHidden("Sample Asset", ""), "Native building preferences or ignore rules failed round trip");
            var styleCopy = BetaSettings.LoadStyle("apple-light");
            Require(styleCopy.Colors.LandColors.Count > 0 && styleCopy.Colors.RouteMapColors.Count >= 3 &&
                    styleCopy.WayBorder.RoadBorder.Size.CalcSize(1) >= 0, "CS1 style import did not populate native colours and widths");
            using (var featureForm = new BetaFeatureForm(main))
            {
                var tabs = Find<TabControl>(featureForm).Single();
                Require(tabs.TabPages.Count == 3, "Features, service editor and building editor tabs missing");
                var grid = Find<PropertyGrid>(featureForm).Single();
                var properties = TypeDescriptor.GetProperties(grid.SelectedObject!);
                Require(properties["SVGTextAsGeometry"] == null && properties["DisableBitmapCache"] == null && properties["TransportFont"] == null, "Unsupported legacy controls remained visible");
                var routeFeatures = properties["RouteMapConfig"]!.GetValue(grid.SelectedObject);
                var routes = TypeDescriptor.GetProperties(routeFeatures!);
                Require(routes["CombineNearbyDirections"] == null && routes["DirectionMergeDistance"] == null, "Deferred route-merging controls survived");
                Require(routes["StopsMergingRange"] == null && routes["StationClusterRadius"] == null && routes["StationLinkDistance"] == null,
                    "Legacy merging range or duplicate nested distance controls remain visible");
                var priorCustomPolicy = session.Config.RouteMapConfig.UseCustomTransferModes;
                routes["UseCustomTransferModes"]!.SetValue(routeFeatures, false);
                Require(routes.Cast<PropertyDescriptor>().Where(x => x.Name.StartsWith("Transfer")).All(x => x.IsReadOnly) &&
                        !routes["MergeBusTramStop"]!.IsReadOnly && !routes["MergeTrainMetroStaion"]!.IsReadOnly,
                    "Selected transfer modes were editable while legacy transfer rules were active");
                routes["UseCustomTransferModes"]!.SetValue(routeFeatures, true);
                Require(routes.Cast<PropertyDescriptor>().Where(x => x.Name.StartsWith("Transfer")).All(x => !x.IsReadOnly) &&
                        routes["MergeBusTramStop"]!.IsReadOnly && routes["MergeTrainMetroStaion"]!.IsReadOnly,
                    "Legacy transfer rules remained editable while selected transfer modes were active");
                routes["UseCustomTransferModes"]!.SetValue(routeFeatures, priorCustomPolicy);
                var originalRadius = session.Config.RouteMapConfig.StationClusterRadius;
                var originalLinkDistance = session.Config.RouteMapConfig.StationLinkDistance;
                var radiusInput = Control<NumericUpDown>(featureForm, "StationClusterRadiusInput");
                var linkInput = Control<NumericUpDown>(featureForm, "StationLinkDistanceInput");
                Require(radiusInput.Minimum == 0 && radiusInput.Maximum == 512 && linkInput.Minimum == 0 && linkInput.Maximum == 512,
                    "Direct station distance controls do not enforce independent metre bounds");
                var originalClusters = session.TransportManager.StationLayout.Clusters.Count;
                var priorDistanceLayers = main.MapData!.Layers.ToArray();
                radiusInput.Value = 0;
                featureForm.PendingApplyForChecks.GetAwaiter().GetResult();
                Require(session.Config.RouteMapConfig.StationClusterRadius == 0 && session.Config.RouteMapConfig.StationLinkDistance == originalLinkDistance &&
                        session.TransportManager.StationLayout.Clusters.Count == session.TransportManager.StationLayout.Points.Count &&
                        session.TransportManager.StationLayout.Clusters.Count > originalClusters &&
                        !priorDistanceLayers.SequenceEqual(main.MapData!.Layers), "Direct cluster input did not rebuild actual station geometry and map layers independently");
                linkInput.Value = 0;
                featureForm.PendingApplyForChecks.GetAwaiter().GetResult();
                Require(session.Config.RouteMapConfig.StationLinkDistance == 0 && session.TransportManager.StationLayout.Links.Count == 0 &&
                        session.TransportManager.StationLayout.Clusters.Count == session.TransportManager.StationLayout.Points.Count,
                    "Direct transfer-link input did not remove connectors independently");
                radiusInput.Value = (decimal)originalRadius; linkInput.Value = (decimal)originalLinkDistance;
                featureForm.PendingApplyForChecks.GetAwaiter().GetResult();
                Require(session.TransportManager.StationLayout.Clusters.Count == originalClusters, "Restoring distance inputs did not restore station grouping");
                var generationField = typeof(MainForm).GetField("_viewGeneration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var generationBefore = (int)generationField.GetValue(main)!;
                for (var value = 1; value <= 20; value++) radiusInput.Value = value;
                featureForm.PendingApplyForChecks.GetAwaiter().GetResult();
                var rebuilds = (int)generationField.GetValue(main)! - generationBefore;
                Require(session.Config.RouteMapConfig.StationClusterRadius == 20 && rebuilds <= 3,
                    "Rapid spinner input queued a full rebuild for every intermediate value or failed to apply the final value");
                radiusInput.Value = (decimal)originalRadius;
                featureForm.PendingApplyForChecks.GetAwaiter().GetResult();

                var serviceGrid = Find<DataGridView>(featureForm).First(g => g.Columns.Contains("Shorthand"));
                Require(serviceGrid.Rows.Count == raw.TransportList.Count, "Service editor dropped services");
                var firstRow = ((IList)serviceGrid.DataSource!)[0]!;
                var rowProperties = TypeDescriptor.GetProperties(firstRow);
                var originalVisible = (bool)rowProperties["Visible"]!.GetValue(firstRow)!;
                rowProperties["Visible"]!.SetValue(firstRow, !originalVisible);
                var serviceId = (int)rowProperties["Id"]!.GetValue(firstRow)!;
                Require(session.TransportManager.TransportList.First(t => t.Id == serviceId).ViewerVisible == !originalVisible, "Service visibility editor did not edit the original service");
                rowProperties["Visible"]!.SetValue(firstRow, originalVisible);
                var originalColour = rowProperties["Colour"]!.GetValue(firstRow);
                var originalShorthand = rowProperties["Shorthand"]!.GetValue(firstRow);
                rowProperties["Colour"]!.SetValue(firstRow, "#FF345678");
                rowProperties["Shorthand"]!.SetValue(firstRow, "UI-test");
                var edited = session.TransportManager.TransportList.First(t => t.Id == serviceId);
                Require(edited.UserDefinedColor!.Value.Red == 0x34 && edited.ViewerDefinedLineNo == "UI-test", "Service colour/shorthand editor ignored edits");
                rowProperties["Colour"]!.SetValue(firstRow, originalColour);
                rowProperties["Shorthand"]!.SetValue(firstRow, originalShorthand);
                var buildingEditor = Find<DataGridView>(featureForm).First(g => g.Columns.Contains("Asset") && g.Rows.Count > 0);
                var buildingRow = ((IList)buildingEditor.DataSource!)[0]!;
                var buildingProperties = TypeDescriptor.GetProperties(buildingRow);
                var buildingHideBefore = buildingProperties["HideShape"]!.GetValue(buildingRow);
                var buildingNameBefore = buildingProperties["NameVisibility"]!.GetValue(buildingRow);
                buildingProperties["HideShape"]!.SetValue(buildingRow, "true");
                buildingProperties["NameVisibility"]!.SetValue(buildingRow, "FullVisible");
                var asset = (string)buildingProperties["Asset"]!.GetValue(buildingRow)!;
                var steam = (string)buildingProperties["SteamId"]!.GetValue(buildingRow)!;
                Require(session.Config.BuildingPreferences.Any(x => x.Name == asset && (x.SteamId ?? "") == steam && x.HideShape == true && x.NameVisibility == CS2MapView.Config.NameVisibility.FullVisible), "Building editor did not modify the published Core model");
                buildingProperties["HideShape"]!.SetValue(buildingRow, buildingHideBefore);
                buildingProperties["NameVisibility"]!.SetValue(buildingRow, buildingNameBefore);
            }

            CheckListActions(main);
            CheckDiagnostics(session, reports);
            CheckTypedDistanceCommit(main);
            using (var options = new ConfigForm { AppRoot = main })
            {
                options.InitializeForChecks().GetAwaiter().GetResult();
                Require(!Control<CheckBox>(options, "HideCargoLinesCheckbox").Visible && !Control<CheckBox>(options, "RenderFerryLinesCheckbox").Visible && !Control<CheckBox>(options, "UserPrefabNameCheckbox").Visible, "CS2 duplicate/cargo controls survived");
                Require(!Control<ComboBox>(options, "StringThemeCombobox").Enabled, "Unused string-theme control remained active");
                var style = Control<ComboBox>(options, "ThemeCombobox");
                Require(style.Items.Contains("apple-light") && style.Items.Contains("lightgray"), "Migrated style choices missing from inherited settings");
                style.SelectedItem = "lightgray";
                Control<CheckBox>(options, "RenderBusLinesCheckbox").Checked = false;
                Control<CheckBox>(options, "RenderStreetNameLabelCheckbox").Checked = false;
                Control<ComboBox>(options, "BuildingFontCombobox").Text = "Arial";
                Control<CheckBox>(options, "ContourLinesCheckbox").Checked = true;
                Control<ComboBox>(options, "ContourIntervalCombobox").SelectedItem = Control<ComboBox>(options, "ContourIntervalCombobox")
                    .Items.Cast<UiChoice>().Single(x => x.Value == "Sparse");
                Control<CheckBox>(options, "HillshadingCheckbox").Checked = true;
                Control<NumericUpDown>(options, "HillshadingStrengthInput").Value = 65;
                var layers = Control<DataGridView>(options, "LayerConfigGridView");
                foreach (DataGridViewRow row in layers.Rows)
                    if (row.DataBoundItem is CS2MapView.Config.LayerSettings layer && layer.Name == "forest") layer.Visible = false;
                options.ApplyConfigurationAsync().GetAwaiter().GetResult();
                Require(!session.Config.RouteMapConfig.RenderBusLine && !session.Config.RenderRoadName, "Inherited route/street controls were not synchronized with the CS1 renderer");
                Require(session.TransportManager.TransportList.Where(t => t.TransportType == "Bus").All(t => !t.IsLineRenderingTarget), "Native transport renderer ignored the inherited bus visibility control");
                Require(!ContainsLabel(main.MapData!.LabelContentsManager!, "StreetNameLabel"), "Native label contents retained hidden street labels");
                Require(session.Config.SelectedStyle == "lightgray" && main.Context.Theme.Colors!.WaterContourColors != null, "Inherited style choice did not rebuild the translated theme");
                Require(session.Config.BuildingFont == "Arial" && main.Context.StringTheme.BuildingName!.FontFamily == "Arial", "Inherited font choice did not affect native label typography");
                Require(session.Config.DisplayContourLines && session.Config.ContourDetailLevel == CS2MapView.CS1.Config.ContourDetail.Sparse,
                    "Contour checkbox/preset controls were ignored");
                Require(session.Config.RenderHillshading && session.Config.HillshadingStrength == 65,
                    "Hillshading controls were not saved independently of contour lines");
                Require(!session.Config.RenderForest && !main.MapData!.Layers.Any(l => l.LayerName == "forest"), "Inherited forest layer visibility was ignored");
                Menus(main.MainMenuStrip!.Items).Single(x => x.Name == "ContourDetailFineMenu").PerformClick();
                main.LastMenuAction.GetAwaiter().GetResult();
                Require(session.Config.ContourDetailLevel == CS2MapView.CS1.Config.ContourDetail.Fine && session.Config.DisplayContourLines && session.Config.RenderHillshading,
                    "Contour preset menu did not apply the selection independently of shading");
                Require(Menus(main.MainMenuStrip!.Items).Count(x => x.Name.StartsWith("ContourDetail") && x.Checked) == 1,
                    "Contour preset menu did not have one checked selection");
                Menus(main.MainMenuStrip!.Items).Single(x => x.Name == "ContourLinesMenu").PerformClick();
                main.LastMenuAction.GetAwaiter().GetResult();
                Require(!session.Config.DisplayContourLines && session.Config.ContourDetailLevel == CS2MapView.CS1.Config.ContourDetail.Fine && session.Config.RenderHillshading,
                    "Hiding contours lost the selected preset or changed shading");
            }

            // Restore reference choices before testing selected, hidden-service exports.
            session.Config.SelectedStyle = config.SelectedStyle;
            session.Config.RouteMapConfig.RenderBusLine = config.RouteMapConfig.RenderBusLine;
            session.Config.RenderRoadName = config.RenderRoadName;
            session.Config.BuildingFont = config.BuildingFont;
            session.Config.DisplayContourLines = config.DisplayContourLines;
            session.Config.ContourIntervals = config.ContourIntervals;
            session.Config.ContourDetailLevel = config.ContourDetailLevel;
            session.Config.RenderHillshading = config.RenderHillshading;
            session.Config.HillshadingStrength = config.HillshadingStrength;
            session.Config.RenderForest = config.RenderForest;
            main.ApplyFeaturesAsync().GetAwaiter().GetResult();
            var candidate = session.TransportManager.SelectableStations.First(s => s.Stops.Count > 0);
            session.HighlightGroup(candidate.Id);
            var servingIds = candidate.Stops.Select(s => s.TransportId).ToHashSet();
            var hidden = session.TransportManager.TransportList.First(t => !servingIds.Contains(t.Id));
            hidden.ViewerVisible = false;
            session.SaveRouteOverrides();
            var view = main.Context.ViewContext;
            BetaOutput.SetView(view, SKRect.Create(660, 1780, 460, 460), (float)Math.PI / 12, 700, 700);
            var beforeView = view.ViewTransform;
            var beforeLayers = main.MapData!.Layers.ToArray();
            var beforeSelected = session.SelectedGroupId;
            var beforeCanvas = session.Config.CanvasSize;
            var rotated = SKRect.Create(view.ViewLeftTop.x, view.ViewLeftTop.y, 700 / view.ViewScaleFromWorld, 700 / view.ViewScaleFromWorld);
            using (var export = BetaOutput.FromScreenAsync(main, rotated, 800, 800).GetAwaiter().GetResult())
            {
                var exportedSession = BetaMapSession.Get(export, raw);
                Require(exportedSession.SelectedGroupId == beforeSelected, "Isolated export lost the selected local group");
                Require(!exportedSession.TransportManager.TransportList.First(t => t.Id == hidden.Id).ViewerVisible, "Highlighted export restored a manually hidden service");
                BetaOutput.Save(export, Path.Combine(reports, "ui-isolated-highlight.png"), 800, 800);
            }
            Require(beforeLayers.SequenceEqual(main.MapData.Layers) && view.ViewTransform.Equals(beforeView) && session.SelectedGroupId == beforeSelected && session.Config.CanvasSize == beforeCanvas && !hidden.ViewerVisible,
                "Isolated output changed interactive layers, view, style size, selection or manual visibility");
            CheckHeightmap(raw, Path.Combine(reports, "heightmap-16bit.png"));
            main.MapData.Dispose(); main.MapData = null;
            File.WriteAllText(Path.Combine(reports, "forms-ui-checks.txt"), "PASS actual hidden Forms host, direct map/transit/rail/area/navigation/list/print menus, checked-state/live layer updates and infrastructure-only restoration, rotated print/export area and real isolated preview and close-during-build disposal, native settings and style import, inherited options, CS1 style/font/contour/layer/route mapping, feature and asset/service editors, restored list bulk/context/sorting/symbol actions, direct style and zoom presets, independent station areas, selected area-only preview, read-only debug exports including terrain/forest reimport, deferred controls, isolated highlighted export, 16-bit source height samples. Offscreen feature-editor commits tested; no user-facing window or printer invoked.");
            Console.WriteLine("PASS Forms controls, isolated highlighted output and 16-bit heightmap");
        }
        finally
        {
            BetaSettings.RouteOverrides.Clear(); BetaSettings.RouteOverrides.AddRange(priorOverrides);
            BetaSettings.Save(priorConfig);
            SynchronizationContext.SetSynchronizationContext(priorContext);
            WindowsFormsSynchronizationContext.AutoInstall = priorAutoInstall;
        }
    }

    private static void CheckRestoredNavigation(MainForm main)
    {
        var session = main.Session!;
        var originalStyle = session.Config.SelectedStyle;
        var alternate = originalStyle == "lightgray" ? "apple-light" : "lightgray";
        var styleMenu = Menus(main.MainMenuStrip!.Items).First(x => x.Name == "MapStyle_" + alternate);
        styleMenu.PerformClick(); main.LastMenuAction.GetAwaiter().GetResult();
        Require(session.Config.SelectedStyle == alternate && styleMenu.Checked && main.Context.Theme.Colors?.LandContourColors.Count > 0,
            "Direct style menu did not rebuild the actual theme and selected state");
        Menus(main.MainMenuStrip.Items).First(x => x.Name == "MapStyle_" + originalStyle).PerformClick(); main.LastMenuAction.GetAwaiter().GetResult();
        var originalScale = main.Context.ViewContext.ScaleFactor;
        var originalTopLeft = main.Context.ViewContext.ViewLeftTop;
        Menus(main.MainMenuStrip.Items).First(x => x.Name == "Zoom85Menu").PerformClick(); main.LastMenuAction.GetAwaiter().GetResult();
        Require(Math.Abs(main.Context.ViewContext.ScaleFactor - .85f) < .0001, "Restored zoom preset did not change the real view scale");
        main.Context.ViewContext.ScaleFactor = originalScale;
        main.Context.ViewContext.ViewLeftTop = originalTopLeft;
    }

    private static bool HasStationAreaGeometry(MainForm main)
    {
        var layer = main.MapData!.Layers.FirstOrDefault(x => x.LayerName == "stationPremises");
        if (layer is not BasicLayer basic) return false;
        var lookup = typeof(BasicLayer).GetProperty("DrawCommands", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(basic)!;
        var commands = (IEnumerable)lookup.GetType().GetMethod("GetAllOrderedObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lookup, null)!;
        return commands.Cast<object>().OfType<CS2MapView.Drawing.PathDrawCommand>().Any(x => x.Path is { IsEmpty: false });
    }

    private static void CheckAreaSelection(MainForm main, SaveImageForm dialog)
    {
        var beforeView = main.Context.ViewContext.ViewTransform;
        var beforeLayers = main.MapData!.Layers.ToArray();
        Require(dialog.Text == "Choose print/export area" && Control<Button>(dialog, "SaveButton").Text == "Use selected area",
            "Dedicated area dialog did not expose rectangle selection independently of exporting a file");
        foreach (var (name, value) in new[] { ("PrintAreaLeftTextbox", "120"), ("PrintAreaTopTextbox", "130"), ("PrintAreaRightTextbox", "360"), ("PrintAreaBottomTextbox", "380") })
            Control<TextBox>(dialog, name).Text = value;
        typeof(SaveImageForm).GetMethod("PrintAreaTextboxs_Leave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, [Control<TextBox>(dialog, "PrintAreaBottomTextbox"), EventArgs.Empty]);
        var crop = dialog.PreviewCropForChecks;
        typeof(SaveImageForm).GetMethod("SaveButton_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, [Control<Button>(dialog, "SaveButton"), EventArgs.Empty]);
        Require(dialog.DialogResult == DialogResult.OK && main.OutputArea.HasValue && Near(main.OutputArea.Value, crop) &&
                main.Context.ViewContext.ViewTransform.Equals(beforeView) && beforeLayers.SequenceEqual(main.MapData.Layers),
            "Area selection did not persist the actual edited rectangle or modified the interactive map");
    }

    internal static void CheckPreviewClosure(MainForm main)
    {
        var beforeView = main.Context.ViewContext.ViewTransform;
        var beforeLayers = main.MapData!.Layers.ToArray();
        var beforeArea = main.OutputArea;
        using var preview = new SaveImageForm { AppRoot = main };
        BetaOutputHost? built = null;
        var pending = preview.InitializeForChecks(host => built = host);
        Require(!pending.IsCompleted, "Close-during-preview regression did not exercise an in-flight native build");
        preview.Dispose();
        pending.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Require(preview.IsDisposed && typeof(SaveImageForm).GetField("PreviewHost", fields)!.GetValue(preview) == null &&
                typeof(SaveImageForm).GetField("MapImage", fields)!.GetValue(preview) == null && built?.MapData?.Layers.Count == 0,
            "Closing during preview retained a late host/bitmap or failed to dispose its actual native layers");
        Require(main.Context.ViewContext.ViewTransform.Equals(beforeView) && beforeLayers.SequenceEqual(main.MapData.Layers) && main.OutputArea == beforeArea,
            "Closing an in-flight preview altered the source view, layers or selected geography");
    }
    private static void CheckListActions(MainForm main)
    {
        var session = main.Session!;
        var routeChoices = session.TransportManager.TransportList.ToDictionary(x => x.Id, x => x.ViewerVisible);
        var routeOrder = session.TransportManager.TransportList.Select(x => x.Id).ToArray();
        var preferences = BetaOutput.Clone(session.Config.BuildingPreferences);
        var symbolSetBefore = session.Config.MapSymbolStyle;
        using var features = new BetaFeatureForm(main, 1) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) };
        features.Show(); System.Windows.Forms.Application.DoEvents();
        try
        {
            var services = Control<DataGridView>(features, "ServiceListGrid");
            Require(services.Columns["Stops"].ReadOnly && services.Columns["LengthMetres"].ReadOnly,
                "Service count and length columns are missing or editable");
            services.Sort(services.Columns["Stops"], ListSortDirection.Ascending);
            var sortedStops = ((IList)services.DataSource!).Cast<object>().Select(x => (int)TypeDescriptor.GetProperties(x)["Stops"]!.GetValue(x)!).ToArray();
            Require(sortedStops.SequenceEqual(sortedStops.Order()) && routeOrder.SequenceEqual(session.TransportManager.TransportList.Select(x => x.Id)),
                "Sorting the service grid failed or changed the source service order");
            services.Sort(services.Columns["LengthMetres"], ListSortDirection.Descending);
            var sortedLengths = ((IList)services.DataSource!).Cast<object>().Select(x => (double)TypeDescriptor.GetProperties(x)["LengthMetres"]!.GetValue(x)!).ToArray();
            Require(sortedLengths.Any(x => x > 0) && sortedLengths.SequenceEqual(sortedLengths.OrderDescending()), "Service lengths are absent or cannot be sorted");
            Control<Button>(features, "HideAllServicesButton").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require(session.TransportManager.TransportList.All(x => !x.ViewerVisible) && session.TransportManager.StationLayout.Points.Count == 0,
                "Hide all did not remove all manually hidden services from the real station display");
            Control<Button>(features, "ShowAllServicesButton").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require(session.TransportManager.TransportList.All(x => x.ViewerVisible) && session.TransportManager.StationLayout.Points.Count > 0,
                "Show all did not restore service choices and actual station geometry");
            foreach (var route in session.TransportManager.TransportList) route.ViewerVisible = routeChoices[route.Id];
            Control<Button>(features, "RedrawServicesButton").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require(session.TransportManager.TransportList.All(x => x.ViewerVisible == routeChoices[x.Id]), "Redraw changed individual service choices");

            Find<TabControl>(features).Single().SelectedIndex = 2;
            System.Windows.Forms.Application.DoEvents();
            var buildings = Control<DataGridView>(features, "BuildingListGrid");
            Require(new[] { "Service", "SubService", "ItemClass", "Count" }.All(x => buildings.Columns.Contains(x) && buildings.Columns[x].ReadOnly),
                "Building asset metadata columns are absent or editable");
            var assetCounts = ((IList)buildings.DataSource!).Cast<object>().Sum(x => (int)TypeDescriptor.GetProperties(x)["Count"]!.GetValue(x)!);
            Require(assetCounts == session.Raw.BuildingList.Count, "Building asset counts lost source buildings");
            buildings.ClearSelection(); buildings.CurrentCell = buildings.Rows[0].Cells[0]; buildings.Rows[0].Selected = true;
            var selected = buildings.Rows[0].DataBoundItem!;
            var properties = TypeDescriptor.GetProperties(selected);
            Menus(buildings.ContextMenuStrip!.Items).First(x => x.Name == "BuildingShapeHideMenu").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Menus(buildings.ContextMenuStrip.Items).First(x => x.Name == "BuildingName_FullVisible").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            var symbol = Menus(buildings.ContextMenuStrip.Items).First(x => x.Name.StartsWith("BuildingSymbol_ja/") && x.Image != null);
            symbol.PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require((string)properties["HideShape"]!.GetValue(selected)! == "true" && (string)properties["NameVisibility"]!.GetValue(selected)! == "FullVisible" &&
                    (string)properties["MapSymbol"]!.GetValue(selected)! == symbol.Name["BuildingSymbol_".Length..],
                "Building context choices or installed symbol previews did not edit the selected asset");
            Menus(buildings.ContextMenuStrip.Items).First(x => x.Name == "BuildingSymbolDefaultMenu").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require((string)properties["MapSymbol"]!.GetValue(selected)! == "", "Use default did not clear a symbol override");
            Control<ComboBox>(features, "BuildingSymbolSetCombobox").SelectedItem = symbolSetBefore == "maki" ? "ja" : "maki";
            features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require(session.Config.MapSymbolStyle != symbolSetBefore, "Default symbol selector did not change the active symbol set");
            Control<Button>(features, "ResetBuildingOverridesButton").PerformClick(); features.PendingApplyForChecks.GetAwaiter().GetResult();
            Require(session.Config.BuildingPreferences.Count == 0 && (string)properties["HideShape"]!.GetValue(selected)! == "" &&
                    (string)properties["NameVisibility"]!.GetValue(selected)! == "", "Reset building overrides did not clear preferences and refresh the asset row");
        }
        finally
        {
            foreach (var route in session.TransportManager.TransportList) route.ViewerVisible = routeChoices[route.Id];
            session.Config.BuildingPreferences.Clear(); session.Config.BuildingPreferences.AddRange(preferences);
            session.Config.MapSymbolStyle = symbolSetBefore;
            main.ApplyFeaturesAsync().GetAwaiter().GetResult();
            features.Close(); System.Windows.Forms.Application.DoEvents();
        }
    }

    private static void CheckDiagnostics(BetaMapSession session, string reports)
    {
        var raw = session.Raw;
        var segmentCount = raw.SegmentList.Count;
        var originalFirstSegment = string.Join(";", raw.SegmentList.First().Points.Select(x => $"{x.X},{x.Y},{x.Z}"));
        var terrainCount = raw.TerrainLines.Count; var forestCount = raw.ForestList.Count;
        foreach (var kind in Enum.GetValues<BetaDiagnosticKind>())
        {
            var path = Path.Combine(reports, "diagnostic-" + kind + (kind == BetaDiagnosticKind.SimplifiedMap ? ".cslmap" : kind == BetaDiagnosticKind.TransportOverview ? ".json" : ".tsv"));
            BetaDiagnostics.Write(session, kind, path);
            Require(new FileInfo(path).Length > 0, "Diagnostic export was empty: " + kind);
            if (kind == BetaDiagnosticKind.SimplifiedMap)
            {
                var imported = CS2MapView.Import.CS1.CS1XMLImporter.Import(path);
                Require(imported.LandArray.SequenceEqual(raw.LandArray) && imported.WaterArray.SequenceEqual(raw.WaterArray) &&
                        imported.ForestByteData.SequenceEqual(raw.ForestByteData), "Simplified-map diagnostic changed terrain, water or forest on reimport");
            }
        }
        var derivedOnly = BetaOutput.Clone(raw);
        derivedOnly.TerrainLines.Clear(); derivedOnly.ForestList.Clear();
        derivedOnly.LandArray = raw.LandArray; derivedOnly.WaterArray = raw.WaterArray; derivedOnly.ForestByteData = raw.ForestByteData;
        var fallbackPath = Path.Combine(reports, "diagnostic-derived-terrain.cslmap");
        using (var file = File.Create(fallbackPath)) new System.Xml.Serialization.XmlSerializer(typeof(CSLExportXML)).Serialize(file, BetaDiagnostics.PrepareSimplifiedCopy(derivedOnly));
        var fallback = CS2MapView.Import.CS1.CS1XMLImporter.Import(fallbackPath);
        Require(fallback.LandArray.SequenceEqual(raw.LandArray) && fallback.WaterArray.SequenceEqual(raw.WaterArray) && fallback.ForestByteData.SequenceEqual(raw.ForestByteData) &&
                derivedOnly.TerrainLines.Count == 0 && derivedOnly.ForestList.Count == 0, "Diagnostic fallback failed to preserve derived data or mutated its source");
        Require(raw.SegmentList.Count == segmentCount && originalFirstSegment == string.Join(";", raw.SegmentList.First().Points.Select(x => $"{x.X},{x.Y},{x.Z}")) &&
                raw.TerrainLines.Count == terrainCount && raw.ForestList.Count == forestCount, "Debug tools modified the original loaded map");
    }
    private static void CheckTypedDistanceCommit(MainForm main)
    {
        var session = main.Session!;
        var radiusBefore = session.Config.RouteMapConfig.StationClusterRadius;
        var linkBefore = session.Config.RouteMapConfig.StationLinkDistance;
        using var features = new BetaFeatureForm(main) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) };
        features.Show();
        System.Windows.Forms.Application.DoEvents();
        var radius = Control<NumericUpDown>(features, "StationClusterRadiusInput");
        var link = Control<NumericUpDown>(features, "StationLinkDistanceInput");
        var editor = Find<TextBox>(radius).Single();
        editor.Focus(); editor.Text = "0";
        var keyDown = radius.GetType().GetMethod("OnTextBoxKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        keyDown.Invoke(radius, [editor, new KeyEventArgs(Keys.Enter)]);
        features.PendingApplyForChecks.GetAwaiter().GetResult();
        Require(session.Config.RouteMapConfig.StationClusterRadius == 0 &&
                session.TransportManager.StationLayout.Clusters.Count == session.TransportManager.StationLayout.Points.Count,
                "Typing cluster radius then Enter did not commit and rebuild the actual station layout");
        radius.Value = (decimal)radiusBefore;
        features.PendingApplyForChecks.GetAwaiter().GetResult();
        var linkEditor = Find<TextBox>(link).Single();
        linkEditor.Focus(); linkEditor.Text = "0";
        features.Close();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!features.IsDisposed && clock.Elapsed.TotalSeconds < 20)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        Require(features.IsDisposed && session.Config.RouteMapConfig.StationLinkDistance == 0 &&
                session.TransportManager.StationLayout.Links.Count == 0,
                "Closing the feature window discarded the active numeric edit or closed before applying it");
        session.Config.RouteMapConfig.StationLinkDistance = linkBefore;
        main.ApplyFeaturesAsync().GetAwaiter().GetResult();
    }
    private static bool Near(SKRect a, SKRect b) => Math.Abs(a.Left - b.Left) < .01 && Math.Abs(a.Top - b.Top) < .01 &&
        Math.Abs(a.Right - b.Right) < .01 && Math.Abs(a.Bottom - b.Bottom) < .01;
    private static IEnumerable<ToolStripMenuItem> Menus(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
            if (item is ToolStripMenuItem menu)
            {
                yield return menu;
                foreach (var child in Menus(menu.DropDownItems)) yield return child;
            }
    }
    private static T Control<T>(System.Windows.Forms.Control parent, string name) where T : System.Windows.Forms.Control
        => (T)parent.Controls.Find(name, true).Single();
    private static bool ContainsLabel(object manager, string typeName)
    {
        var contents = manager.GetType().GetProperty("Contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;
        var labels = (IEnumerable)contents.GetType().GetMethod("GetAllOrderedObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(contents, null)!;
        return labels.Cast<object>().Any(l => l.GetType().Name == typeName);
    }
    private static IEnumerable<T> Find<T>(System.Windows.Forms.Control parent) where T : System.Windows.Forms.Control
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            if (child is T typed) yield return typed;
            foreach (var result in Find<T>(child)) yield return result;
        }
    }
    private static void CheckHeightmap(CSLExportXML raw, string path)
    {
        BetaOutput.SaveHeightmap(raw, path);
        var png = File.ReadAllBytes(path);
        Require(png[24] == 16 && png[25] == 0, "Heightmap is not 16-bit grayscale PNG");
        using var compressed = new MemoryStream();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT") compressed.Write(png, offset + 8, length);
            offset += 12 + length;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); zlib.CopyTo(bytes);
        var samples = bytes.ToArray(); const int n = CSLExportXML.TERRAIN_RESOLUTION;
        Require(samples.Length == (n * 2 + 1) * n, "Heightmap sample count changed");
        foreach (var (x, y) in new[] { (0, 0), (13, 50), (540, 540), (1080, 1080) })
        {
            var pixel = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(samples.AsSpan(y * (2 * n + 1) + 1 + x * 2, 2));
            var original = (ushort)Math.Clamp(raw.LandArray[(n - y - 1) * n + x] * 64f, 0, 65535);
            Require(pixel == original, "Heightmap changed original source quantization or Y orientation");
        }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
