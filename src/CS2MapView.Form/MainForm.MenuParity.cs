using CS2MapView.Beta;
using CS2MapView.CS1.Config;
using System.Globalization;

namespace CS2MapView.Form;

public partial class MainForm
{
    private void InitializeMenuParity()
    {
        var styles = new ToolStripMenuItem(UiText.Get("Menu_MapStyle")) { Name = "CS1StyleMenu" };
        foreach (var style in BetaSettings.StyleCollection.Children)
        {
            var name = style.FileName;
            var item = new ToolStripMenuItem(name) { Name = "MapStyle_" + name };
            item.Click += async (_, _) => await TrackMenuFeatureAsync(() => FeatureConfig.SelectedStyle = name);
            _featureMenuChecks.Add((item, () => FeatureConfig.SelectedStyle == name));
            styles.DropDownItems.Add(item);
        }
        ViewMenuItem.DropDownItems.Insert(1, styles);

        var stationAreas = new ToolStripMenuItem(UiText.Get("Menu_StationAreas")) { Name = "StationAreasMenu" };
        var stationVisible = new ToolStripMenuItem(UiText.Get("Menu_ShowStationAreas")) { Name = "StationAreaVisibleMenu" };
        stationVisible.Click += async (_, _) => await TrackMenuFeatureAsync(() => FeatureConfig.RenderStationPremises = !FeatureConfig.RenderStationPremises);
        _featureMenuChecks.Add((stationVisible, () => FeatureConfig.RenderStationPremises));
        stationAreas.DropDownItems.Add(stationVisible);
        var railMenu = ViewMenuItem.DropDownItems.OfType<ToolStripMenuItem>().First(x => x.Name == "CS1RailMenu");
        var frame = railMenu.DropDownItems.OfType<ToolStripMenuItem>().First(x => x.Name == "FrameStopTracksMenu");
        railMenu.DropDownItems.Remove(frame); stationAreas.DropDownItems.Add(frame);
        foreach (var color in Enum.GetValues<StationAreaColor>())
        {
            var selected = color;
            var item = new ToolStripMenuItem(UiText.Get("Menu_StationAreaColor_" + color)) { Name = "StationAreaColor_" + color };
            item.Click += async (_, _) => await TrackMenuFeatureAsync(() => FeatureConfig.StationPremisesColor = selected);
            _featureMenuChecks.Add((item, () => FeatureConfig.StationPremisesColor == selected));
            stationAreas.DropDownItems.Add(item);
        }
        ViewMenuItem.DropDownItems.Insert(2, stationAreas);

        var zoom = ViewMenuItem.DropDownItems.OfType<ToolStripMenuItem>().First(x => x.Name == "ZoomMenu");
        var previousZoomItems = zoom.DropDownItems.Cast<ToolStripItem>().ToArray();
        _mapOnlyMenus.RemoveAll(item => previousZoomItems.Contains(item));
        zoom.DropDownItems.Clear();
        foreach (var item in previousZoomItems) item.Dispose();
        foreach (var scale in new[] { .25f, .3f, .4f, .5f, .6f, .7f, .85f, 1f, 1.15f, 1.3f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f })
        {
            var selected = scale;
            var item = new ToolStripMenuItem(UiText.Format("Format_Percent", scale * 100)) { Name = "Zoom" + (scale * 100).ToString("0", CultureInfo.InvariantCulture) + "Menu" };
            item.Click += async (_, _) => { LastMenuAction = SetNavigationScaleAsync(selected); await LastMenuAction; };
            zoom.DropDownItems.Add(item); _mapOnlyMenus.Add(item);
        }
        var chooseArea = new ToolStripMenuItem(UiText.Get("Menu_ChooseOutputArea"), null, (_, _) =>
        {
            using var dialog = new SaveImageForm(true) { AppRoot = this }; dialog.ShowDialog(this);
        }) { Name = "ChooseOutputAreaMenu" };
        FileMenuItem.DropDownItems.Insert(2, chooseArea); _mapOnlyMenus.Add(chooseArea);

        var debug = new ToolStripMenuItem(UiText.Get("Menu_Debug")) { Name = "CS1DebugMenu" };
        foreach (var (kind, caption) in new[] {
            (BetaDiagnosticKind.Buildings, UiText.Get("Menu_Diagnostic_Buildings")), (BetaDiagnosticKind.Segments, UiText.Get("Menu_Diagnostic_Segments")),
            (BetaDiagnosticKind.SegmentDetails, UiText.Get("Menu_Diagnostic_SegmentDetails")), (BetaDiagnosticKind.SimplifiedMap, UiText.Get("Menu_Diagnostic_SimplifiedMap")),
            (BetaDiagnosticKind.TransportOverview, UiText.Get("Menu_Diagnostic_TransportOverview")), (BetaDiagnosticKind.TransportSegments, UiText.Get("Menu_Diagnostic_TransportSegments")) })
        {
            var selected = kind;
            var item = new ToolStripMenuItem(caption, null, (_, _) => SaveDiagnostic(selected)) { Name = "Diagnostic_" + kind };
            debug.DropDownItems.Add(item); _mapOnlyMenus.Add(item);
        }
        HelpMenuItem.DropDownItems.Insert(0, debug);
        HelpMenuItem.DropDownOpening += (_, _) => SynchronizeFeatureMenus();
        SynchronizeFeatureMenus();
    }

    private void SaveDiagnostic(BetaDiagnosticKind kind)
    {
        if (Session == null) return;
        var extension = kind == BetaDiagnosticKind.SimplifiedMap ? "cslmap" : kind == BetaDiagnosticKind.TransportOverview ? "json" : "tsv";
        using var dialog = new SaveFileDialog { Title = UiText.Format("Dialog_SaveDiagnostic", UiText.Get("Diagnostic_" + kind)), FileName = kind + "." + extension,
            Filter = UiText.Format("FileFilter_Diagnostic", extension.ToUpperInvariant(), extension), AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { BetaDiagnostics.Write(Session, kind, dialog.FileName); StatusBarMessageLabel.Text = UiText.Format("Message_SavedFile", dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
