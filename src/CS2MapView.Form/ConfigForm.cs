using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2MapView.Data;
using CS2MapView.Drawing.Terrain;
using System.Globalization;
using log4net;
using Gfw.Common;
using CS2MapView.Drawing.Layer;
using Gfw.Ui.WindowsForms;
using CS2MapView.Config;
using System.DirectoryServices.ActiveDirectory;
using CS2MapView.Drawing.Labels;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace CS2MapView.Form
{
    /// <summary>
    /// 設定フォーム
    /// </summary>
    public partial class ConfigForm : System.Windows.Forms.Form
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ConfigForm));
        /// <summary>
        /// クライアントアプリケーション
        /// </summary>
        public required MainForm AppRoot { get; init; }
        private RenderContext Context => AppRoot.Context;


        private IList<LayerSettings> EdittingLayerOrder;
#nullable disable
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ConfigForm()
        {
            InitializeComponent();
            RenderAirplaneLinesCheckbox.Text = "Blimp routes";
            RenderAirplaneStopsCheckbox.Text = "Blimp stops";
            RenderShipLinesCheckbox.Text = "Ferry routes";
            RenderShipStopsCheckbox.Text = "Ferry harbours";
            RenderFerryLinesCheckbox.Visible = RenderFerryStopsCheckbox.Visible = false;
            HideCargoLinesCheckbox.Visible = false;
            UserPrefabNameCheckbox.Visible = false;
        }
#nullable restore

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            var us = Context.UserSettings;
            ThemeCombobox.Items.Clear();
            ThemeCombobox.Items.AddRange(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Styles"), "*.cmvstyle").Select(Path.GetFileNameWithoutExtension).Cast<object>().ToArray());
            ThemeCombobox.Text = (AppRoot.Session?.Config ?? CS2MapView.Beta.BetaSettings.Config).SelectedStyle;

            StringThemeCombobox.Items.Clear();
            StringThemeCombobox.Items.AddRange(Context.StringThemeCandidates.Select(x => x.Name!).ToArray<object>());
            StringThemeCombobox.Text = "CS1 style typography";
            StringThemeCombobox.Enabled = false;

            ContourIntervalCombobox.Items.Clear();
            ContourIntervalCombobox.Items.AddRange(["None", "Default", "Low"]);
            var featureConfig = AppRoot.Session?.Config ?? CS2MapView.Beta.BetaSettings.Config;
            ContourIntervalCombobox.Text = featureConfig.DisplayContourLines ? featureConfig.ContourIntervals : "None";

            // システムフォントリストを読み込む / Load system fonts
            var availableFonts = Theme.StringTheme.GetAvailableFonts();
            var fontItems = availableFonts.ToArray<object>();

            BuildingFontCombobox.Items.Clear();
            BuildingFontCombobox.Items.Add("(Use Default)"); // デフォルトを使用
            BuildingFontCombobox.Items.AddRange(fontItems);
            BuildingFontCombobox.Text = string.IsNullOrEmpty(us.CustomBuildingNameFont) ? "(Use Default)" : us.CustomBuildingNameFont;

            DistrictFontCombobox.Items.Clear();
            DistrictFontCombobox.Items.Add("(Use Default)");
            DistrictFontCombobox.Items.AddRange(fontItems);
            DistrictFontCombobox.Text = string.IsNullOrEmpty(us.CustomDistrictNameFont) ? "(Use Default)" : us.CustomDistrictNameFont;

            StreetFontCombobox.Items.Clear();
            StreetFontCombobox.Items.Add("(Use Default)");
            StreetFontCombobox.Items.AddRange(fontItems);
            StreetFontCombobox.Text = string.IsNullOrEmpty(us.CustomStreetNameFont) ? "(Use Default)" : us.CustomStreetNameFont;

            VectorizeLandCheckbox.Checked = us.VectorizeTerrainLand;
            VectorizeWaterCheckbox.Checked = us.VectorizeTerrainWater;
            TerrainMaxResolutionTextbox.Text = us.TerrainMaxResolution.ToString(CultureInfo.InvariantCulture);
            WaterMaxResolutionTextbox.Text = us.WaterMaxResolution.ToString(CultureInfo.InvariantCulture);

            DefaultMapSymbolStyleCombobox.Items.Add("ja+");
            DefaultMapSymbolStyleCombobox.Items.Add("ja");
            DefaultMapSymbolStyleCombobox.Items.Add("maki");

            DefaultMapSymbolStyleCombobox.Text = us.DefaultMapSymbolType;

            //レイヤー
            EdittingLayerOrder = new PropertyUtil<LayerSettings>().ListPropertyClone<LayerSettings>(Context.UserSettings.LayerDrawingOrder?.OrderBy(t => t.Order)) ?? new List<LayerSettings>();

            var colFactory = new SortableBindingGridViewPartsFactory<LayerSettings>();

            LayerConfigGridView.Columns.Add(colFactory.CreateTextboxColumn(nameof(LayerSettings.Name), "Name", 100, true, align: DataGridViewContentAlignment.MiddleLeft));
            LayerConfigGridView.Columns.Add(colFactory.CreateCheckBoxColumn(nameof(LayerSettings.Visible), "Visible", 50, false));
            LayerConfigGridView.Columns.Add(colFactory.CreateTextboxColumn(nameof(LayerSettings.Order), "Order", 50, false, format: "0", align: DataGridViewContentAlignment.MiddleRight));

            LayerConfigGridView.DataSource = colFactory.CreateBindingDataSource(EdittingLayerOrder);

            DisableBuildingBorderCheckbox.Checked = us.DisableBuildingBorder;
            HideRICOCheckbox.Checked = us.HideRICOBuildingsShape;

            RenderRailwaysCheckbox.Checked = us.RenderRailTrain;
            RenderMetroCheckbox.Checked = us.RenderRailMetro;
            RenderTramCheckbox.Checked = us.RenderRailTram;
            RenderMonorailCheckbox.Checked = us.RenderRailMonorail;
            RenderCableCarCheckbox.Checked = us.RenderRailCableCar;

            RenderBuildingNameLabelCheckbox.Checked = us.RenderBuildingNameLabel;
            RenderStreetNameLabelCheckbox.Checked = us.RenderStreetNames;
            RenderMapSymbolCheckbox.Checked = us.RenderMapSymbol;
            RenderDistrictNameLabelCheckbox.Checked = us.RenderDistrictNameLabel;
            UserPrefabNameCheckbox.Checked = us.UsePrefabBuildingName;

            RenderBusLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderBusLine;
            RenderTrainLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderTrainLine;
            RenderTramLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderTramLine;
            RenderMetroLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderMetroLine;
            RenderMonorailLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderMonorailLine;
            RenderShipLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderShipLine;
            RenderAirplaneLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderAirplaneLine;
            RenderFerryLinesCheckbox.Checked = us.TransportRouteMapConfig.RenderFerryLine;
            RenderBusStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderBusStop;
            RenderTrainStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderTrainStop;
            RenderTramStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderTramStop;
            RenderMetroStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderMetroStop;
            RenderMonorailStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderMonorailStop;
            RenderShipStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderShipStop;
            RenderAirplaneStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderAirplaneStop;
            RenderFerryStopsCheckbox.Checked = us.TransportRouteMapConfig.RenderFerryStop;

            HideCargoLinesCheckbox.Checked = us.TransportRouteMapConfig.HideCargoLines;

        }
        private bool ValidateContent()
        {
            //レイヤー
            if (Context.UserSettings.LayerDrawingOrder is null || EdittingLayerOrder.Count != Context.UserSettings.LayerDrawingOrder.Count)
            {
                throw new InvalidOperationException();
            }
            if (EdittingLayerOrder.Select(t => t.Order).Distinct().Count() != EdittingLayerOrder.Count)
            {
                //TODO
                MessageBox.Show("Duplicate order value found.");
                return false;
            }
            return true;
        }


        private async void SubmitButton_Click(object sender, EventArgs e)
        {
            try { await ApplyConfigurationAsync(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Settings could not be applied", MessageBoxButtons.OK, MessageBoxIcon.Error); SubmitButton.Enabled = true; }
        }

        internal Task InitializeForChecks()
        {
            ConfigForm_Load(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        internal async Task ApplyConfigurationAsync()
        {
            SubmitButton.Enabled = false;

            if (!ValidateContent())
            {
                SubmitButton.Enabled = true;
                return;
            }
            List<string> layers = [
                ILayer.LayerNameBuildings,
                ILayer.LayerNameDistricts,
                ILayer.LayerNameGrid,
                ILayer.LayerNameLabels,
                ILayer.LayerNameRailways,
                ILayer.LayerNameRoads,
                ILayer.LayerNameTerrain,
                "forest",
                "stationPremises",
                ILayer.LayerNameTransportLines
            ];

            var us = Context.UserSettings;
            bool layerOrderRequiresRedraw = false;
            bool themeChanged = false;
            bool fontChanged = false;
            Dictionary<string, bool> layerChangedFlags = layers.ToDictionary(t => t, t => false);

            // Select from migrated CS1 styles; the translated Skia theme uses this style.
            {
                var featureConfig = AppRoot.Session?.Config ?? CS2MapView.Beta.BetaSettings.Config;
                if (featureConfig.SelectedStyle != ThemeCombobox.Text)
                {
                    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Styles", ThemeCombobox.Text + ".cmvstyle"))) throw new InvalidOperationException("Selected CS1 style is missing.");
                    featureConfig.SelectedStyle = ThemeCombobox.Text;
                    themeChanged = true;
                }
            }

            // カスタムフォント設定を処理 / Handle custom font settings
            {
                string GetFontValue(string comboValue) => comboValue == "(Use Default)" ? null! : comboValue;

                string? newBuildingFont = GetFontValue(BuildingFontCombobox.Text);
                string? newDistrictFont = GetFontValue(DistrictFontCombobox.Text);
                string? newStreetFont = GetFontValue(StreetFontCombobox.Text);

                if (us.CustomBuildingNameFont != newBuildingFont ||
                    us.CustomDistrictNameFont != newDistrictFont ||
                    us.CustomStreetNameFont != newStreetFont)
                {
                    us.CustomBuildingNameFont = newBuildingFont;
                    us.CustomDistrictNameFont = newDistrictFont;
                    us.CustomStreetNameFont = newStreetFont;

                    // 現在のテーマにカスタムフォントを適用
                    Context.StringTheme.ApplyCustomFonts(newBuildingFont, newDistrictFont, newStreetFont);

                    fontChanged = true;
                    layerChangedFlags[ILayer.LayerNameLabels] = true;
                }
            }

            var layerResult = SetLayerValues();
            layerOrderRequiresRedraw |= layerResult.requiresRedraw;
            void LayerFlagOr(string layerName, bool value)
            {
                layerChangedFlags[layerName] = layerChangedFlags[layerName] | value;
            }
            void HandleLayerResult(string layerName)
            {
                if (layerResult.visibilityChangedLayer.Contains(layerName))
                {
                    layerChangedFlags[layerName] = true;
                }
            }
            layers.ForEach(HandleLayerResult);

            LayerFlagOr(ILayer.LayerNameTerrain, SetTerrainValues());

            if (us.DisableBuildingBorder != DisableBuildingBorderCheckbox.Checked)
            {
                us.DisableBuildingBorder = DisableBuildingBorderCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameBuildings, true);
            }
            if (us.HideRICOBuildingsShape != HideRICOCheckbox.Checked)
            {
                us.HideRICOBuildingsShape = HideRICOCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameBuildings, true);
            }
            if (us.RenderRailTrain != RenderRailwaysCheckbox.Checked)
            {
                us.RenderRailTrain = RenderRailwaysCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameRailways, true);
            }
            if (us.RenderRailTram != RenderTramCheckbox.Checked)
            {
                us.RenderRailTram = RenderTramCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameRailways, true);
            }
            if (us.RenderRailMetro != RenderMetroCheckbox.Checked)
            {
                us.RenderRailMetro = RenderMetroCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameRailways, true);
            }
            if (us.RenderRailMonorail != RenderMonorailCheckbox.Checked)
            {
                us.RenderRailMonorail = RenderMonorailCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameRailways, true);
            }
            if (us.RenderRailCableCar != RenderCableCarCheckbox.Checked)
            {
                us.RenderRailCableCar = RenderCableCarCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameRailways, true);
            }

            if (us.RenderBuildingNameLabel != RenderBuildingNameLabelCheckbox.Checked)
            {
                us.RenderBuildingNameLabel = RenderBuildingNameLabelCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameLabels, true);
            }
            if (us.RenderDistrictNameLabel != RenderDistrictNameLabelCheckbox.Checked)
            {
                us.RenderDistrictNameLabel = RenderDistrictNameLabelCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameLabels, true);
            }
            if (us.RenderStreetNames != RenderStreetNameLabelCheckbox.Checked)
            {
                us.RenderStreetNames = RenderStreetNameLabelCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameLabels, true);
            }
            if (us.RenderMapSymbol != RenderMapSymbolCheckbox.Checked)
            {
                us.RenderMapSymbol = RenderMapSymbolCheckbox.Checked;
                LayerFlagOr(ILayer.LayerNameLabels, true);
            }


            if (us.DefaultMapSymbolType != DefaultMapSymbolStyleCombobox.Text)
            {
                us.DefaultMapSymbolType = DefaultMapSymbolStyleCombobox.Text;
                LayerFlagOr(ILayer.LayerNameLabels, true);
            }

            var tsold = us.TransportRouteMapConfig;
            void setTransportCheckbox(string propertyName, bool newValue)
            {
                PropertyInfo? pi = typeof(TransportRouteMapConfig).GetProperty(propertyName);
                if (pi is null)
                {
                    Debug.Print($"property TransportRouteMapConfig.{newValue} not found.");
                    return;
                }

                if ((bool)pi.GetValue(tsold)! != newValue)
                {
                    pi.SetValue(tsold, newValue);
                    LayerFlagOr(ILayer.LayerNameTransportLines, true);
                }
            }
            setTransportCheckbox(nameof(tsold.RenderBusLine), RenderBusLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderTrainLine), RenderTrainLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderTramLine), RenderTramLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderMetroLine), RenderMetroLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderMonorailLine), RenderMonorailLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderShipLine), RenderShipLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderAirplaneLine), RenderAirplaneLinesCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderFerryLine), RenderFerryLinesCheckbox.Checked);

            setTransportCheckbox(nameof(tsold.RenderBusStop), RenderBusStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderTrainStop), RenderTrainStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderTramStop), RenderTramStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderMetroStop), RenderMetroStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderMonorailStop), RenderMonorailStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderShipStop), RenderShipStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderAirplaneStop), RenderAirplaneStopsCheckbox.Checked);
            setTransportCheckbox(nameof(tsold.RenderFerryStop), RenderFerryStopsCheckbox.Checked);

            var featureConfigToSave = AppRoot.Session?.Config ?? CS2MapView.Beta.BetaSettings.Config;
            BetaOutput.ReadFeatureSettings(us, featureConfigToSave);
            if (AppRoot.Session != null)
            {
                AppRoot.Session.RefreshTransport();
            }
            CS2MapView.Beta.BetaSettings.Save(featureConfigToSave);

            // Recreate label contents and independent layer commands whenever settings change.
            // Rearranging an old label manager alone cannot apply visibility/font changes.
            if (AppRoot.MapData != null && Context.SourceMapType != null &&
                (themeChanged || fontChanged || layerOrderRequiresRedraw || layerChangedFlags.ContainsValue(true)))
                await AppRoot.ApplyFeaturesAsync(false);

            if (themeChanged | fontChanged | layerOrderRequiresRedraw | layerChangedFlags.ContainsValue(true))
            {
                await AppRoot.Context.UserSettings.SaveAsync();
            }
            DialogResult = DialogResult.OK;
            AppRoot.InvalidateSkia();
            Close();
        }

        private (bool requiresRedraw, List<string> visibilityChangedLayer) SetLayerValues()
        {
            var prevList = AppRoot.Context.UserSettings.LayerDrawingOrder!;
            var result = new List<string>();
            bool changed = false;
            foreach (var newValue in EdittingLayerOrder)
            {
                var prev = prevList.First(t => t.Name == newValue.Name);

                if (newValue.Visible != prev.Visible || newValue.Order != prev.Order)
                {
                    changed = true;
                }
                if (newValue.Visible != prev.Visible)
                {
                    result.Add(newValue.Name ?? "");
                }
            }
            if (changed)
            {
                AppRoot.Context.UserSettings.LayerDrawingOrder = [.. EdittingLayerOrder];
            }
            return (changed, result);
        }

        private bool SetTerrainValues()
        {
            bool terrainChanged = false;
            var features = AppRoot.Session?.Config ?? CS2MapView.Beta.BetaSettings.Config;
            bool contoursVisible = ContourIntervalCombobox.Text != "None";
            string contourIntervals = ContourIntervalCombobox.Text == "Low" ? "Low" : "Default";
            if (features.DisplayContourLines != contoursVisible || features.ContourIntervals != contourIntervals)
            {
                features.DisplayContourLines = contoursVisible;
                features.ContourIntervals = contourIntervals;
                terrainChanged = true;
            }

            bool prevBoolValue = Context.UserSettings.VectorizeTerrainLand;
            bool newBoolValue = VectorizeLandCheckbox.Checked;
            if (prevBoolValue != newBoolValue)
            {
                Context.UserSettings.VectorizeTerrainLand = newBoolValue; ;
                terrainChanged = true;
            }
            prevBoolValue = Context.UserSettings.VectorizeTerrainWater;
            newBoolValue = VectorizeWaterCheckbox.Checked;
            if (prevBoolValue != newBoolValue)
            {
                Context.UserSettings.VectorizeTerrainWater = newBoolValue;
                terrainChanged = true;
            }

            int prevIntValue = Context.UserSettings.TerrainMaxResolution;
            int? newIntValue = TerrainMaxResolutionTextbox.Text.ParseIntInvariant();
            if (newIntValue.HasValue && newIntValue.Value != prevIntValue)
            {
                Context.UserSettings.TerrainMaxResolution = newIntValue.Value;
                terrainChanged = true;
            }
            prevIntValue = Context.UserSettings.WaterMaxResolution;
            newIntValue = WaterMaxResolutionTextbox.Text.ParseIntInvariant();
            if (newIntValue.HasValue && newIntValue.Value != prevIntValue)
            {
                Context.UserSettings.WaterMaxResolution = newIntValue.Value;
                terrainChanged = true;
            }
            return terrainChanged;
        }

        private void FormCancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void MaxResolution_TextChanged(object sender, EventArgs e)
        {
            if (sender is not TextBox t)
            {
                return;
            }
            var fv = t.Text.ParseIntInvariant();
            if (fv.HasValue)
            {
                if (fv.Value <= 64)
                {
                    t.Text = "64";
                }
                else if (fv.Value > 8192)
                {
                    t.Text = "8192";
                }
            }
            else
            {
                t.Text = "4096";
            }

        }

        private void LayerConfigTabPage_Click(object sender, EventArgs e)
        {

        }
    }
}
