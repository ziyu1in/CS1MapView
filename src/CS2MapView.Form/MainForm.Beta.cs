using CS2MapView.Beta;
using CS2MapView.Data;
using CS2MapView.CS1.Config;
using CS2MapView.Drawing.Layer;
using CS2MapView.Import;
using CS2MapView.Util;
using SkiaSharp;
using System.Drawing.Printing;

namespace CS2MapView.Form;

public partial class MainForm
{
    internal BetaMapSession? Session => Context.SourceMapType is CS1MapType source ? BetaMapSession.Get(this, source.GetImportData()) : null;
    private Size _surfaceSize;
    private Point? _mouseDown;
    private bool _mouseDragged;
    private bool _highlightEnabled = true;
    private int _viewGeneration;
    private readonly SemaphoreSlim _layerGate = new(1);
    private ToolStripMenuItem _highlightMenu = null!;
    private ToolStripMenuItem _clearHighlightMenu = null!;
    private string? _startupMap;
    private SKPoint[]? _outputAreaWorld;
    internal SKRect? OutputArea
    {
        get
        {
            if (_outputAreaWorld == null) return null;
            var rotation = SKMatrix.CreateRotation(Context.ViewContext.Angle);
            var corners = _outputAreaWorld.Select(rotation.MapPoint).ToArray();
            return new SKRect(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        }
        set
        {
            if (value == null) { _outputAreaWorld = null; return; }
            var area = value.Value;
            if (!float.IsFinite(area.Left) || !float.IsFinite(area.Top) || !float.IsFinite(area.Right) || !float.IsFinite(area.Bottom) || area.Width <= 0 || area.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Print/export area must have finite positive dimensions.");
            var inverse = SKMatrix.CreateRotation(-Context.ViewContext.Angle);
            _outputAreaWorld = new[] { new SKPoint(area.Left, area.Top), new SKPoint(area.Right, area.Top), new SKPoint(area.Right, area.Bottom), new SKPoint(area.Left, area.Bottom) }
                .Select(inverse.MapPoint).ToArray();
        }
    }
    private readonly List<(ToolStripMenuItem Item, Func<bool> Read)> _featureMenuChecks = [];
    private readonly List<ToolStripMenuItem> _mapOnlyMenus = [];

    private void InitializeBetaFeatures(string? startupMap)
    {
        Text = ProjectInfo.Name + " — Cities: Skylines 1";
        KeyPreview = true;
        _startupMap = startupMap;
        var features = new ToolStripMenuItem("CS1 features…", null, (_, _) => new BetaFeatureForm(this).ShowDialog(this));
        _highlightMenu = new ToolStripMenuItem("Select station to highlight routes") { Checked = true, CheckOnClick = true };
        _highlightMenu.CheckedChanged += (_, _) => { _highlightEnabled = _highlightMenu.Checked; if (!_highlightEnabled) ClearStationHighlight(); };
        _clearHighlightMenu = new ToolStripMenuItem("Clear route highlight", null, (_, _) => ClearStationHighlight()) { ShortcutKeyDisplayString = "Esc" };
        ViewMenuItem.DropDownItems.AddRange([new ToolStripSeparator(), features, _highlightMenu, _clearHighlightMenu]);
        var heightmap = new ToolStripMenuItem("Save heightmap…", null, SaveHeightmap_Click) { Name = "SaveHeightmapMenu" };
        var print = new ToolStripMenuItem("Print current view/selected area…", null, PrintMap_Click) { Name = "PrintMapMenu" };
        FileMenuItem.DropDownItems.Insert(2, heightmap); FileMenuItem.DropDownItems.Insert(3, print);
        _mapOnlyMenus.Add(heightmap); _mapOnlyMenus.Add(print); _mapOnlyMenus.Add(SaveImageToolStripMenuItem);
        InitializeFeatureMenus();
        InitializeMenuParity();
        Shown += async (_, _) => { if (_startupMap != null) await OpenMapAsync(_startupMap); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { ClearStationHighlight(); e.Handled = true; } };
    }

    internal Task LastMenuAction { get; private set; } = Task.CompletedTask;
    private Task TrackMenuFeatureAsync(Action change) => LastMenuAction = ApplyMenuFeatureAsync(change);
    private ApplicationConfig FeatureConfig => Session?.Config ?? BetaSettings.Config;

    /// <summary>Restore direct access to the stable viewer's map, transit and list commands.</summary>
    private void InitializeFeatureMenus()
    {
        ToolStripMenuItem Check(ToolStripMenuItem parent, string name, string caption, Func<bool> read, Action<bool> write)
        {
            var item = new ToolStripMenuItem(caption) { Name = name };
            _featureMenuChecks.Add((item, read));
            item.Click += async (_, _) => await TrackMenuFeatureAsync(() => write(!read()));
            parent.DropDownItems.Add(item);
            return item;
        }
        var layers = new ToolStripMenuItem("Layers and labels") { Name = "CS1LayerMenu" };
        void Layer(string name, string caption, Func<bool> read, Action<bool> write) => Check(layers, name, caption, read, write);
        Layer("TerrainLayerMenu", "Terrain", () => FeatureConfig.RenderTerrain, v => FeatureConfig.RenderTerrain = v);
        Layer("ForestLayerMenu", "Forest", () => FeatureConfig.RenderForest, v => FeatureConfig.RenderForest = v);
        Layer("BuildingsLayerMenu", "Buildings", () => FeatureConfig.RenderBuilding, v => FeatureConfig.RenderBuilding = v);
        Layer("RoadsLayerMenu", "Roads and paths", () => FeatureConfig.RenderRoad, v => FeatureConfig.RenderRoad = v);
        Layer("RailwaysLayerMenu", "Rail infrastructure", () => FeatureConfig.ShowRailInfrastructure, v => FeatureConfig.RenderRail = v);
        Layer("StationPremisesLayerMenu", "Station areas", () => FeatureConfig.RenderStationPremises, v => FeatureConfig.RenderStationPremises = v);
        Layer("GridLayerMenu", "Tile grid", () => FeatureConfig.RenderGrid, v => FeatureConfig.RenderGrid = v);
        layers.DropDownItems.Add(new ToolStripSeparator());
        Layer("DistrictNameMenu", "District names", () => FeatureConfig.RenderDistrictName, v => FeatureConfig.RenderDistrictName = v);
        Layer("BuildingNameMenu", "Building names", () => FeatureConfig.RenderBuildingName, v => FeatureConfig.RenderBuildingName = v);
        Layer("ParkNameMenu", "Park names", () => FeatureConfig.RenderParkName, v => FeatureConfig.RenderParkName = v);
        Layer("StreetNameMenu", "Street names", () => FeatureConfig.RenderRoadName, v => FeatureConfig.RenderRoadName = v);
        Layer("MapSymbolMenu", "Map symbols", () => FeatureConfig.RenderMapSymbol, v => FeatureConfig.RenderMapSymbol = v);

        var transit = new ToolStripMenuItem("Transit routes and stations") { Name = "CS1TransitMenu" };
        Check(transit, "TransportRoutesMenu", "Routes", () => FeatureConfig.ShowTransitRoutes, v => FeatureConfig.RenderTransportRoute = v);
        Check(transit, "TransportStationsMenu", "Station symbols and transit modal tags", () => FeatureConfig.ShowTransitLabels, v => FeatureConfig.RenderTransport = v);
        Check(transit, "StationModeLettersMenu", "Mode letters", () => FeatureConfig.RouteMapConfig.ShowStationModeBadges, v => FeatureConfig.RouteMapConfig.ShowStationModeBadges = v);
        Check(transit, "SplitStationBadgesMenu", "One transit modal tag per cluster", () => FeatureConfig.RouteMapConfig.SplitStationBadgesByCluster, v => FeatureConfig.RouteMapConfig.SplitStationBadgesByCluster = v);
        Check(transit, "AutomaticRouteColoursMenu", "Automatic route colours", () => FeatureConfig.RouteMapConfig.AutoColoring, v => FeatureConfig.RouteMapConfig.AutoColoring = v);
        Check(transit, "WidenSharedRoutesMenu", "Widen shared routes", () => FeatureConfig.RouteMapConfig.WidenOnSharedLines, v => FeatureConfig.RouteMapConfig.WidenOnSharedLines = v);
        Check(transit, "RouteDirectionArrowsMenu", "Direction arrows", () => FeatureConfig.RouteMapConfig.MarkOneWayRoutes, v => FeatureConfig.RouteMapConfig.MarkOneWayRoutes = v);
        var modes = new ToolStripMenuItem("Modes");
        transit.DropDownItems.Add(modes);
        var routeFields = new[] { ("Bus", "RenderBusLine", "RenderBusStop"), ("Tram", "RenderTramLine", "RenderTramStop"), ("Metro", "RenderMetroLine", "RenderMetroStation"), ("Train", "RenderTrainLine", "RenderTrainStation"), ("Monorail", "RenderMonorailLine", "RenderMonorailStation"), ("Blimp", "RenderBlimpLine", "RenderBlimpStop"), ("Ferry", "RenderFerryLine", "RenderFerryHarbor") };
        foreach (var (mode, routes, stops) in routeFields)
        {
            var menu = new ToolStripMenuItem(mode == "Bus" ? "Bus / trolleybus" : mode); modes.DropDownItems.Add(menu);
            foreach (var (property, caption) in new[] { (routes, "Routes"), (stops, "Stops and stations") })
                Check(menu, mode + property + "Menu", caption,
                    () => (bool)typeof(CS2MapView.CS1.Config.TransportRouteMapConfig).GetProperty(property)!.GetValue(FeatureConfig.RouteMapConfig)!,
                    v => typeof(CS2MapView.CS1.Config.TransportRouteMapConfig).GetProperty(property)!.SetValue(FeatureConfig.RouteMapConfig, v));
        }
        transit.DropDownItems.Add(new ToolStripMenuItem("Service visibility, colours and shorthands…", null, (_, _) => new BetaFeatureForm(this, 1).ShowDialog(this)) { Name = "RoutesEditorMenu" });

        var rail = new ToolStripMenuItem("Rail infrastructure options") { Name = "CS1RailMenu" };
        Check(rail, "InfrastructureOnlyMenu", "Infrastructure only", () => FeatureConfig.Railway.InfrastructureOnly, v => FeatureConfig.Railway.InfrastructureOnly = v);
        Check(rail, "SeparateTracksMenu", "Separate tracks", () => FeatureConfig.Railway.SeparateTracks, v => FeatureConfig.Railway.SeparateTracks = v);
        Check(rail, "ReconnectTracksMenu", "Reconnect tracks", () => FeatureConfig.Railway.ReconnectTracks, v => FeatureConfig.Railway.ReconnectTracks = v);
        Check(rail, "RailTunnelsMenu", "Show tunnels", () => FeatureConfig.Railway.ShowTunnels, v => FeatureConfig.Railway.ShowTunnels = v);
        Check(rail, "RailTypeColoursMenu", "Colour by type", () => FeatureConfig.Railway.ColorByType, v => FeatureConfig.Railway.ColorByType = v);
        Check(rail, "FrameStopTracksMenu", "Frame station tracks", () => FeatureConfig.FrameStopTracks, v => FeatureConfig.FrameStopTracks = v);
        foreach (var appearance in Enum.GetValues<RailwayAppearance>())
        {
            var selected = appearance;
            var item = new ToolStripMenuItem(appearance + " appearance") { Name = appearance + "RailAppearanceMenu" };
            item.Click += async (_, _) => await TrackMenuFeatureAsync(() => FeatureConfig.Railway.Appearance = selected);
            _featureMenuChecks.Add((item, () => FeatureConfig.Railway.Appearance == selected)); rail.DropDownItems.Add(item);
        }
        var area = new ToolStripMenuItem("Map area") { Name = "RenderAreaMenu" };
        foreach (var size in new[] { 9d, 5.1d, 5d, 3d })
        {
            var selected = size;
            var item = new ToolStripMenuItem(size + " × " + size) { Name = "RenderArea" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "Menu" };
            item.Click += async (_, _) => await TrackMenuFeatureAsync(() => FeatureConfig.RenderAreaSize = selected);
            _featureMenuChecks.Add((item, () => Math.Abs(FeatureConfig.RenderAreaSize - selected) < .001)); area.DropDownItems.Add(item);
        }
        var rotate = new ToolStripMenuItem("Rotate") { Name = "RotationMenu" };
        foreach (var degrees in new[] { 0, 90, 180, 270 })
        {
            var selected = degrees;
            var item = new ToolStripMenuItem(degrees + "°") { Name = "Rotate" + degrees + "Menu" };
            item.Click += async (_, _) =>
            {
                Context.ViewContext.Angle = (float)(selected * Math.PI / 180);
                FeatureConfig.Angle = selected;
                OnZoomOrViewPositionChanged();
                await RebuildViewAsync(false);
                BetaSettings.Save(FeatureConfig);
                SynchronizeFeatureMenus();
            };
            _featureMenuChecks.Add((item, () => Math.Abs(Context.ViewContext.Angle * 180 / Math.PI - selected) < .01)); rotate.DropDownItems.Add(item); _mapOnlyMenus.Add(item);
        }
        var zoom = new ToolStripMenuItem("Zoom") { Name = "ZoomMenu" };
        foreach (var scale in new[] { .25f, .5f, 1f, 1.5f, 2f, 3f, 4f })
        {
            var selected = scale;
            var item = new ToolStripMenuItem((scale * 100) + "%") { Name = "Zoom" + (scale * 100) + "Menu" };
            item.Click += async (_, _) => await SetNavigationScaleAsync(selected);
            zoom.DropDownItems.Add(item); _mapOnlyMenus.Add(item);
        }
        var fit = new ToolStripMenuItem("Fit whole map", null, async (_, _) => await FitMapAsync()) { Name = "FitMapMenu" };
        _mapOnlyMenus.Add(fit);
        ViewMenuItem.DropDownItems.Insert(1, layers);
        ViewMenuItem.DropDownItems.Insert(2, transit);
        ViewMenuItem.DropDownItems.Insert(3, rail);
        ViewMenuItem.DropDownItems.Insert(4, area);
        ViewMenuItem.DropDownItems.Insert(5, rotate);
        ViewMenuItem.DropDownItems.Insert(6, zoom);
        ViewMenuItem.DropDownItems.Insert(7, fit);
        ViewMenuItem.DropDownOpening += (_, _) => SynchronizeFeatureMenus();
        var lists = new ToolStripMenuItem("Lists") { Name = "CS1ListsMenu" };
        lists.DropDownItems.Add(new ToolStripMenuItem("Transit services…", null, (_, _) => new BetaFeatureForm(this, 1).ShowDialog(this)) { Name = "ServiceListMenu" });
        lists.DropDownItems.Add(new ToolStripMenuItem("Buildings…", null, (_, _) => new BetaFeatureForm(this, 2).ShowDialog(this)) { Name = "BuildingListMenu" });
        menuStrip1.Items.Insert(2, lists);
        var setArea = new ToolStripMenuItem("Use current view as print/export area", null, (_, _) => SetOutputAreaFromView()) { Name = "SetOutputAreaMenu" };
        var clearArea = new ToolStripMenuItem("Clear print/export area", null, (_, _) => { OutputArea = null; SynchronizeFeatureMenus(); }) { Name = "ClearOutputAreaMenu" };
        _featureMenuChecks.Add((setArea, () => OutputArea.HasValue));
        FileMenuItem.DropDownItems.Insert(2, setArea); FileMenuItem.DropDownItems.Insert(3, clearArea);
        _mapOnlyMenus.Add(setArea); _mapOnlyMenus.Add(clearArea);
        FileMenuItem.DropDownOpening += (_, _) => SynchronizeFeatureMenus();
        SynchronizeFeatureMenus();
    }

    internal async Task ApplyMenuFeatureAsync(Action change)
    {
        try { change(); await ApplyFeaturesAsync(); }
        catch (Exception ex) { Logger.Error("Menu settings failed", ex); StatusBarMessageLabel.Text = ex.Message; }
        finally { SynchronizeFeatureMenus(); }
    }
    internal void SynchronizeFeatureMenus()
    {
        foreach (var (item, read) in _featureMenuChecks) item.Checked = read();
        foreach (var item in _mapOnlyMenus) item.Enabled = MapData != null;
        foreach (var (item, _) in _featureMenuChecks.Where(x => x.Item.Name is "RailwaysLayerMenu" or "TransportRoutesMenu" or "TransportStationsMenu"))
            item.Enabled = !FeatureConfig.Railway.InfrastructureOnly;
    }
    internal void SetOutputAreaFromView()
    {
        var size = ClientToSurface(new Point(SkiaControl.ClientSize.Width, SkiaControl.ClientSize.Height));
        var view = Context.ViewContext;
        OutputArea = SKRect.Create(view.ViewLeftTop.x, view.ViewLeftTop.y, Math.Max(1, size.X) / view.ViewScaleFromWorld, Math.Max(1, size.Y) / view.ViewScaleFromWorld);
        SynchronizeFeatureMenus();
    }
    internal void SetOutputArea(SKRect? area) { OutputArea = area; SynchronizeFeatureMenus(); }

    private async Task SetNavigationScaleAsync(float scale)
    {
        var view = Context.ViewContext;
        var size = ClientToSurface(new Point(SkiaControl.ClientSize.Width, SkiaControl.ClientSize.Height));
        var centre = new SKPoint(view.ViewLeftTop.x + size.X / (2 * view.ViewScaleFromWorld), view.ViewLeftTop.y + size.Y / (2 * view.ViewScaleFromWorld));
        view.ScaleFactor = scale;
        view.ViewLeftTop = (centre.X - size.X / (2 * view.ViewScaleFromWorld), centre.Y - size.Y / (2 * view.ViewScaleFromWorld));
        OnZoomOrViewPositionChanged(); await RebuildViewAsync(false); InvalidateSkia();
    }
    private async Task FitMapAsync()
    {
        var view = Context.ViewContext;
        var bounds = SKPathEx.GetTransformedRectBound(view.WorldRect, SKMatrix.CreateRotation(view.Angle));
        var size = ClientToSurface(new Point(SkiaControl.ClientSize.Width, SkiaControl.ClientSize.Height));
        view.ScaleFactor = Math.Min(Math.Max(1, size.X) / bounds.Width, Math.Max(1, size.Y) / bounds.Height) / view.WorldScaleFactor;
        view.ViewLeftTop = (bounds.Left, bounds.Top);
        OnZoomOrViewPositionChanged(); await RebuildViewAsync(false); InvalidateSkia();
    }
    internal SKPoint ClientToSurface(Point point)
    {
        var sx = _surfaceSize.Width > 0 && SkiaControl.ClientSize.Width > 0 ? (float)_surfaceSize.Width / SkiaControl.ClientSize.Width : 1f;
        var sy = _surfaceSize.Height > 0 && SkiaControl.ClientSize.Height > 0 ? (float)_surfaceSize.Height / SkiaControl.ClientSize.Height : 1f;
        return new SKPoint(point.X * sx, point.Y * sy);
    }

    internal async Task OpenMapAsync(string file)
    {
        if (!file.EndsWith(".cslmap", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".cslmap.gz", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Open a Cities: Skylines 1 .cslmap or .cslmap.gz export.", Text);
            return;
        }
        Enabled = false;
        _rebuildCts?.Cancel();
        Interlocked.Increment(ref _viewGeneration);
        await _layerGate.WaitAsync();
        try
        {
            OutputArea = null;
            BetaOutput.ApplyFeatureSettings(Context.UserSettings, Session?.Config ?? BetaSettings.Config);
            var result = await new ExportFileImporter(this).ImportAsync(file);
            if (result != null) MessageBox.Show(result, Text);
            Context.UserSettings.OpenFileDir = Path.GetDirectoryName(file);
            await Context.UserSettings.SaveAsync();
            UpdateHighlightAvailability();
            OnZoomOrViewPositionChanged();
            InvalidateSkia();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _layerGate.Release(); Enabled = true; }
    }

    private async Task RebuildViewAsync(bool debounce)
    {
        if (MapData == null) return;
        var generation = Interlocked.Increment(ref _viewGeneration);
        _rebuildCts?.Cancel();
        _rebuildCts?.Dispose();
        _rebuildCts = new CancellationTokenSource();
        var token = _rebuildCts.Token;
        var view = Context.ViewContext.Clone();
        try
        {
            if (debounce) await Task.Delay(RebuildDebounceMilliseconds, token);
            await _layerGate.WaitAsync(token);
            try
            {
                if (generation != _viewGeneration || MapData == null) return;
                _isRebuildingLabels = true;
                await MapData.RebuildLayersOnResizeOrRotate(view, ReceiveProgressChanged);
                if (generation != _viewGeneration)
                    await MapData.RebuildLayersOnResizeOrRotate(Context.ViewContext.Clone(), ReceiveProgressChanged);
            }
            finally { _isRebuildingLabels = false; _layerGate.Release(); }
            if (!IsDisposed) { RefreshDisplayInfoLabel(); InvalidateSkia(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Error("Layer rebuild failed", ex); if (!IsDisposed) StatusBarMessageLabel.Text = ex.Message; }
    }

    internal async Task ApplyFeaturesAsync(bool synchronizeNavigation = true)
    {
        var session = Session;
        if (session == null || Context.SourceMapType == null) { BetaSettings.Save(BetaSettings.Config); return; }
        var generation = Interlocked.Increment(ref _viewGeneration);
        _rebuildCts?.Cancel();
        await _layerGate.WaitAsync();
        try
        {
            session.SaveRouteOverrides();
            session.RefreshTransport();
            if (session.Config.Railway.InfrastructureOnly) session.ClearHighlight();
            if (synchronizeNavigation) BetaOutput.ApplyFeatureSettings(Context.UserSettings, session.Config);
            var source = Context.SourceMapType;
            var replacement = await source.BuildAll(this, null);
            if (generation != _viewGeneration) { replacement.Dispose(); return; }
            replacement.FileName = MapData?.FileName;
            var old = MapData;
            MapData = replacement;
            old?.Dispose();
            BetaSettings.Save(session.Config);
            await Context.UserSettings.SaveAsync();
            UpdateHighlightAvailability();
            InvalidateSkia();
        }
        finally { _layerGate.Release(); }
    }

    private void UpdateHighlightAvailability()
    {
        var session = Session;
        _highlightMenu.Enabled = session?.Config.ShowTransitLabels == true;
        _clearHighlightMenu.Enabled = session != null && !session.Config.Railway.InfrastructureOnly && (session.Config.ShowTransitLabels || session.SelectedGroupId.HasValue);
        SynchronizeFeatureMenus();
    }

    private async Task SelectStationAtAsync(Point client)
    {
        if (!_highlightEnabled || Session == null || !Session.Config.ShowTransitLabels) return;
        if (!Context.ViewContext.ViewTransform.TryInvert(out var inverse)) return;
        Session.SelectAtWorld(inverse.MapPoint(ClientToSurface(client)));
        await RebuildViewAsync(false);
    }

    private async void ClearStationHighlight()
    {
        if (Session == null) return;
        Session.ClearHighlight();
        await RebuildViewAsync(false);
    }

    private void SaveHeightmap_Click(object? sender, EventArgs e)
    {
        if (Session == null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG heightmap|*.png", FileName = "heightmap.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { BetaOutput.SaveHeightmap(Session.Raw, dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async void PrintMap_Click(object? sender, EventArgs e)
    {
        if (MapData == null) return;
        using var document = new PrintDocument { DocumentName = MapData.MapName ?? "CSLMapView map" };
        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var rect = new SKRect(0, 0, Math.Max(1, _surfaceSize.Width), Math.Max(1, _surfaceSize.Height));
        var view = Context.ViewContext;
        var rotated = OutputArea ?? SKRect.Create(view.ViewLeftTop.x, view.ViewLeftTop.y, rect.Width / view.ViewScaleFromWorld, rect.Height / view.ViewScaleFromWorld);
        if (OutputArea.HasValue) rect = new SKRect(0, 0, rotated.Width, rotated.Height);
        var imageWidth = (int)Math.Round(4096 * Math.Min(1, rect.Width / rect.Height));
        var imageHeight = (int)Math.Round(4096 * Math.Min(1, rect.Height / rect.Width));
        try
        {
            using var host = await BetaOutput.FromScreenAsync(this, rotated, imageWidth, imageHeight);
            using var skia = BetaOutput.DrawBitmap(host, imageWidth, imageHeight);
            using var image = BetaOutput.ToWinBitmap(skia);
            document.PrintPage += (_, args) =>
            {
                var scale = Math.Min((float)args.MarginBounds.Width / image.Width, (float)args.MarginBounds.Height / image.Height);
                var bounds = new RectangleF(args.MarginBounds.Left + (args.MarginBounds.Width - image.Width * scale) / 2,
                    args.MarginBounds.Top + (args.MarginBounds.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
                args.Graphics!.DrawImage(image, bounds); args.HasMorePages = false;
            };
            document.Print();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
