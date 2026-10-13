using CS2MapView.Beta;
using CS2MapView.CS1.Transit;
using SkiaSharp;
using System.ComponentModel;
using CS2MapView.Drawing;
using CSLMapView.XML;
using Color = SkiaSharp.SKColor;
using NativeBuildingPreference = CS2MapView.Config.BuildingPreference;

namespace CS2MapView.Form;

/// <summary>Feature controls supplement the inherited settings dialog; changes apply live.</summary>
internal sealed class BetaFeatureForm : System.Windows.Forms.Form
{
    private readonly MainForm _main;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, Text = "Press Enter or Apply to commit numeric edits. Changes save only in the beta settings." };
    private readonly SemaphoreSlim _applyGate = new(1);
    private PropertyGrid _featureGrid = null!;
    private Task _pendingApply = Task.CompletedTask;
    private readonly object _applyStateLock = new();
    private long _applyRevision;
    private bool _applyWorkerRunning;
    private bool _closingAfterApply;
    private bool _waitingToClose;
    private bool _lastApplySucceeded = true;
    private readonly List<StationDistanceInput> _distanceInputs = [];
    internal Task PendingApplyForChecks => _pendingApply;
    public BetaFeatureForm(MainForm main, int selectedTab = 0)
    {
        _main = main;
        Text = "CS1 features, routes and buildings";
        Width = 940; Height = 760; StartPosition = FormStartPosition.CenterParent;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var features = new TabPage("Map and station features");
        var propertyGrid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new FeatureObject(main.Session?.Config ?? BetaSettings.Config), HelpVisible = true, ToolbarVisible = true };
        _featureGrid = propertyGrid;
        propertyGrid.PropertyValueChanged += (_, _) => QueueApply();
        var configForDistances = main.Session?.Config ?? BetaSettings.Config;
        var distances = new TableLayoutPanel { Dock = DockStyle.Top, Height = 114, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        distances.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        distances.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        distances.Controls.Add(new Label { Text = "Cluster search radius (m)", AutoSize = true }, 0, 0);
        distances.Controls.Add(new Label { Text = "Transfer link distance (m)", AutoSize = true }, 1, 0);
        StationDistanceInput Distance(string name, double value, Action<double> write)
        {
            var input = new StationDistanceInput { Name = name, Minimum = 0, Maximum = 512, DecimalPlaces = 1, Increment = 1, Width = 160,
                Value = (decimal)Math.Clamp(double.IsFinite(value) ? value : 0, 0, 512) };
            input.ValueChanged += (_, _) => { write((double)input.Value); QueueApply(); };
            _distanceInputs.Add(input);
            return input;
        }
        distances.Controls.Add(Distance("StationClusterRadiusInput", configForDistances.RouteMapConfig.StationClusterRadius,
            value => configForDistances.RouteMapConfig.StationClusterRadius = value), 0, 1);
        distances.Controls.Add(Distance("StationLinkDistanceInput", configForDistances.RouteMapConfig.StationLinkDistance,
            value => configForDistances.RouteMapConfig.StationLinkDistance = value), 1, 1);
        distances.Controls.Add(new Label { Text = "Search for aligned stops. Default: 90 m.", AutoSize = true }, 0, 2);
        distances.Controls.Add(new Label { Text = "Limit links between symbols. Default: 100 m.", AutoSize = true }, 1, 2);
        var policyHint = new Label { Text = "Both distances follow the selected or legacy transfer rules below.", AutoSize = true };
        distances.Controls.Add(policyHint, 0, 3); distances.SetColumnSpan(policyHint, 2);
        features.Controls.Add(propertyGrid); features.Controls.Add(distances); tabs.TabPages.Add(features);

        var routes = new TabPage("Service visibility, colours and shorthands");
        if (main.Session != null)
        {
            var rows = new SortableBindingList<RouteRow>(main.Session.TransportManager.TransportList.Select(t => new RouteRow(main.Session, t.Id)).ToList());
            var grid = MakeGrid(rows);
            grid.Columns[nameof(RouteRow.Id)].ReadOnly = true;
            grid.Columns[nameof(RouteRow.Name)].ReadOnly = true;
            grid.Columns[nameof(RouteRow.Mode)].ReadOnly = true;
            grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) QueueApply(); };
            grid.Name = "ServiceListGrid";
            grid.Columns[nameof(RouteRow.Stops)].ReadOnly = grid.Columns[nameof(RouteRow.LengthMetres)].ReadOnly = true;
            grid.Columns[nameof(RouteRow.LengthMetres)].DefaultCellStyle.Format = "N0";
            var serviceActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
            void ServiceButton(string name, string text, Action action)
            {
                var button = new Button { Name = name, Text = text, AutoSize = true };
                button.Click += (_, _) => { grid.EndEdit(); action(); grid.Refresh(); QueueApply(); };
                serviceActions.Controls.Add(button);
            }
            ServiceButton("ShowAllServicesButton", "Show all", () => { foreach (var route in main.Session!.TransportManager.TransportList) route.ViewerVisible = true; });
            ServiceButton("HideAllServicesButton", "Hide all", () => { foreach (var route in main.Session!.TransportManager.TransportList) route.ViewerVisible = false; });
            ServiceButton("RedrawServicesButton", "Redraw map", () => { });
            var serviceContext = new ContextMenuStrip();
            serviceContext.Items.Add("Choose colour…", null, (_, _) =>
            {
                if (grid.CurrentRow?.DataBoundItem is not RouteRow row) return;
                using var dialog = new ColorDialog { FullOpen = true };
                if (SKColor.TryParse(row.Colour, out var current)) dialog.Color = System.Drawing.Color.FromArgb(current.Red, current.Green, current.Blue);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                row.Colour = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"; grid.Refresh(); QueueApply();
            });
            serviceContext.Items.Add("Clear colour override", null, (_, _) => { if (grid.CurrentRow?.DataBoundItem is RouteRow row) { row.Colour = ""; grid.Refresh(); QueueApply(); } });
            Disposed += (_, _) => serviceContext.Dispose();
            grid.ContextMenuStrip = serviceContext;
            grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; };
            routes.Controls.Add(grid); routes.Controls.Add(serviceActions);
        }
        else routes.Controls.Add(new Label { Text = "Load a CS1 map to edit its services.", Dock = DockStyle.Top });
        tabs.TabPages.Add(routes);

        var buildings = new TabPage("Building preferences");
        var config = main.Session?.Config ?? BetaSettings.Config;
        var assets = main.Session?.Raw.BuildingList.GroupBy(b => (b.SteamId, b.ShortName)).Select(g => new BuildingRow(config.BuildingPreferences, g.First(), g.Count())).OrderBy(b => b.Asset).ToList();
        var buildingGrid = assets != null ? MakeGrid(new SortableBindingList<BuildingRow>(assets)) : MakeGrid(new BindingList<NativeBuildingPreference>(config.BuildingPreferences));
        if (assets != null)
        {
            buildingGrid.Columns[nameof(BuildingRow.Asset)].ReadOnly = true;
            buildingGrid.Columns[nameof(BuildingRow.SteamId)].ReadOnly = true;
        }
        else
        {
            buildingGrid.AllowUserToAddRows = buildingGrid.AllowUserToDeleteRows = true;
            if (buildingGrid.Columns.Contains("IsSettingEmpty")) buildingGrid.Columns.Remove("IsSettingEmpty");
        }
        buildingGrid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) QueueApply(); };
        buildingGrid.UserDeletedRow += (_, _) => QueueApply();
        var hint = new Label { Dock = DockStyle.Bottom, Height = 58, Text = "Preferences apply to this asset. HideShape: true/false/blank. NameVisibility: Visible, FullVisible, Invisible or blank. MapSymbol accepts an installed SVG symbol name. Blank values restore defaults." };
        buildingGrid.Name = "BuildingListGrid";
        if (assets != null)
            foreach (var property in new[] { nameof(BuildingRow.Service), nameof(BuildingRow.SubService), nameof(BuildingRow.ItemClass), nameof(BuildingRow.Count) })
                buildingGrid.Columns[property].ReadOnly = true;
        var buildingActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
        var symbolSet = new ComboBox { Name = "BuildingSymbolSetCombobox", DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        symbolSet.Items.AddRange(["ja+", "ja", "maki"]); symbolSet.SelectedItem = config.MapSymbolStyle;
        symbolSet.SelectedIndexChanged += (_, _) => { config.MapSymbolStyle = symbolSet.Text; QueueApply(); };
        buildingActions.Controls.Add(new Label { Text = "Default symbols", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        buildingActions.Controls.Add(symbolSet);
        var reset = new Button { Name = "ResetBuildingOverridesButton", Text = "Reset building overrides", AutoSize = true };
        reset.Click += (_, _) => { config.BuildingPreferences.Clear(); buildingGrid.Refresh(); QueueApply(); };
        buildingActions.Controls.Add(reset);
        if (assets != null) InstallBuildingContext(buildingGrid);
        buildings.Controls.Add(buildingGrid); buildings.Controls.Add(hint); buildings.Controls.Add(buildingActions); tabs.TabPages.Add(buildings);
        tabs.SelectedIndex = Math.Clamp(selectedTab, 0, tabs.TabPages.Count - 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", Name = "FeatureCloseButton", AutoSize = true };
        close.Click += (_, _) => Close();
        var apply = new Button { Text = "Apply", Name = "FeatureApplyButton", AutoSize = true };
        apply.Click += (_, _) => { if (CommitPendingEditors()) QueueApply(); };
        buttons.Controls.Add(close); buttons.Controls.Add(apply);
        Controls.Add(tabs); Controls.Add(buttons); Controls.Add(_status);
        FormClosing += ClosingWithPendingEdit;
    }

    private void InstallBuildingContext(DataGridView grid)
    {
        IEnumerable<BuildingRow> Selected() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<BuildingRow>()
            .DefaultIfEmpty(grid.CurrentRow?.DataBoundItem as BuildingRow).Where(x => x != null)!;
        void Change(Action<BuildingRow> change)
        {
            grid.EndEdit();
            foreach (var row in Selected()) change(row);
            grid.Refresh(); QueueApply();
        }
        var context = new ContextMenuStrip();
        var symbolImages = new List<System.Drawing.Image>();
        ToolStripMenuItem Group(string title) { var item = new ToolStripMenuItem(title); context.Items.Add(item); return item; }
        var shape = Group("Building shape");
        shape.DropDownItems.Add(new ToolStripMenuItem("Use default", null, (_, _) => Change(r => r.HideShape = "")) { Name = "BuildingShapeDefaultMenu" });
        shape.DropDownItems.Add(new ToolStripMenuItem("Show", null, (_, _) => Change(r => r.HideShape = "false")) { Name = "BuildingShapeShowMenu" });
        shape.DropDownItems.Add(new ToolStripMenuItem("Hide", null, (_, _) => Change(r => r.HideShape = "true")) { Name = "BuildingShapeHideMenu" });
        var names = Group("Building name");
        foreach (var (caption, value) in new[] { ("Use default", ""), ("Show custom name", "Visible"), ("Show name or asset name", "FullVisible"), ("Hide", "Invisible") })
        {
            var selected = value;
            names.DropDownItems.Add(new ToolStripMenuItem(caption, null, (_, _) => Change(r => r.NameVisibility = selected)) { Name = "BuildingName_" + (value == "" ? "Default" : value) });
        }
        var symbols = Group("Map symbol");
        symbols.DropDownItems.Add(new ToolStripMenuItem("Use default", null, (_, _) => Change(r => r.MapSymbol = "")) { Name = "BuildingSymbolDefaultMenu" });
        symbols.DropDownItems.Add(new ToolStripMenuItem("Hide", null, (_, _) => Change(r => r.MapSymbol = "system.none")) { Name = "BuildingSymbolHideMenu" });
        foreach (var family in new[] { "ja", "maki" })
        {
            var menu = new ToolStripMenuItem(family); symbols.DropDownItems.Add(menu);
            foreach (var (symbolName, svg) in MapSymbolPictureManager.Instance.GetSymbols(family) ?? new Dictionary<string, Svg.Skia.SKSvg>())
            {
                var fullName = family + "/" + symbolName;
                var item = new ToolStripMenuItem(symbolName, null, (_, _) => Change(r => r.MapSymbol = fullName)) { Name = "BuildingSymbol_" + fullName };
                if (svg.Picture is { } picture && picture.CullRect.Width > 0 && picture.CullRect.Height > 0)
                {
                    using var bitmap = new SKBitmap(20, 20);
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Clear(SKColors.Transparent);
                        var bounds = picture.CullRect;
                        var scale = Math.Min(18 / bounds.Width, 18 / bounds.Height);
                        canvas.Translate(1, 1); canvas.Scale(scale); canvas.Translate(-bounds.Left, -bounds.Top); canvas.DrawPicture(picture);
                    }
                    item.Image = BetaOutput.ToWinBitmap(bitmap);
                    symbolImages.Add(item.Image);
                }
                menu.DropDownItems.Add(item);
            }
        }
        context.Disposed += (_, _) => { foreach (var image in symbolImages) image.Dispose(); };
        grid.ContextMenuStrip = context;
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex];
            if (!row.Selected) grid.ClearSelection();
            row.Selected = true; grid.CurrentCell = row.Cells[Math.Max(0, e.ColumnIndex)];
        };
        Disposed += (_, _) => context.Dispose();
    }

    private static DataGridView MakeGrid(object rows)
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, BindingContext = new BindingContext(), DataSource = rows, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false };
        grid.DataError += (_, e) => { e.ThrowException = false; MessageBox.Show("Enter a valid value; colours use #RRGGBB or #AARRGGBB."); };
        return grid;
    }

    private void QueueApply()
    {
        lock (_applyStateLock)
        {
            _applyRevision++;
            if (_applyWorkerRunning) return;
            _applyWorkerRunning = true;
            _pendingApply = ApplyUntilStableAsync();
        }
    }

    private async Task ApplyUntilStableAsync()
    {
        // Let a burst of spinner/text changes settle before building its first snapshot.
        await Task.Yield();
        try
        {
            while (true)
            {
                long revision;
                lock (_applyStateLock) revision = _applyRevision;
                await ApplyAsync();
                lock (_applyStateLock)
                {
                    if (revision != _applyRevision) continue;
                    _applyWorkerRunning = false;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_applyStateLock) _applyWorkerRunning = false;
            _lastApplySucceeded = false;
            if (!IsDisposed) _status.Text = ex.Message;
        }
    }

    private bool CommitPendingEditors()
    {
        // Changing selection through the public control API commits its active text editor.
        // Closing the window alone does not validate a pending nested numeric value.
        foreach (var input in _distanceInputs) input.CommitEditor();
        var selected = _featureGrid.SelectedGridItem;
        if (selected?.GridItemType == GridItemType.Property && selected.Parent is { } parent)
        {
            _featureGrid.SelectedGridItem = parent;
            if (ReferenceEquals(_featureGrid.SelectedGridItem, selected)) return false;
        }
        foreach (var grid in Descendants(this).OfType<DataGridView>())
            if (!grid.EndEdit()) return false;
        return true;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async void ClosingWithPendingEdit(object? sender, FormClosingEventArgs e)
    {
        if (_closingAfterApply) return;
        if (_waitingToClose) { e.Cancel = true; return; }
        if (!CommitPendingEditors()) { e.Cancel = true; return; }
        if (_pendingApply.IsCompleted) { e.Cancel = !_lastApplySucceeded; return; }
        e.Cancel = true;
        _waitingToClose = true;
        Enabled = false;
        await _pendingApply;
        if (IsDisposed) return;
        if (!_lastApplySucceeded) { Enabled = true; _waitingToClose = false; return; }
        _closingAfterApply = true;
        BeginInvoke(new Action(Close));
    }
    private async Task ApplyAsync()
    {
        await _applyGate.WaitAsync();
        _lastApplySucceeded = false;
        try { _status.Text = "Applying changes…"; await _main.ApplyFeaturesAsync(); _status.Text = (_main.Session?.Config ?? BetaSettings.Config).RouteMapConfig.UseCustomTransferModes
            ? "Applied. Selected transfer modes are active."
            : "Applied. Legacy transfer rules are active."; _featureGrid.Refresh(); _lastApplySucceeded = true; }
        catch (Exception ex) { _status.Text = ex.Message; MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _applyGate.Release(); }
    }

    private sealed class StationDistanceInput : NumericUpDown
    {
        internal void CommitEditor() => ValidateEditText();
    }
    private sealed class RouteRow
    {
        private readonly BetaMapSession _session;
        private readonly int _id;
        private TransportInfo _route => _session.TransportManager.TransportList.First(t => t.Id == _id);
        public RouteRow(BetaMapSession session, int id) { _session = session; _id = id; }
        public int Id => _route.Id;
        public string Name => _route.Name;
        public string Mode => _route.TransportType;
        public int Stops => _route.Stops.Count;
        [DisplayName("Length (m)")] public double LengthMetres => _route.Stops.SelectMany(s => s.PointsToNextStop).Sum(part => part.Points.Zip(part.Points.Skip(1), (a, b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2))).Sum());
        public bool Visible { get => _route.ViewerVisible; set => _route.ViewerVisible = value; }
        public string Shorthand { get => _route.ViewerDefinedLineNo ?? ""; set => _route.ViewerDefinedLineNo = string.IsNullOrWhiteSpace(value) ? null! : value; }
        public string Colour
        {
            get => _route.UserDefinedColor is Color color ? $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}" : "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { _route.UserDefinedColor = null; return; }
                if (!SKColor.TryParse(value, out var color)) throw new ArgumentException("Colour must be #RRGGBB or #AARRGGBB.");
                _route.UserDefinedColor = color;
            }
        }
    }

    private sealed class BuildingRow
    {
        private readonly List<NativeBuildingPreference> _prefs;
        public string Asset { get; }
        public string SteamId { get; }
        public string Service { get; }
        public string SubService { get; }
        public string ItemClass { get; }
        public int Count { get; }
        public BuildingRow(List<NativeBuildingPreference> prefs, XMLBuilding sample, int count)
        {
            _prefs = prefs; SteamId = sample.SteamId; Asset = sample.ShortName;
            Service = sample.Service; SubService = sample.SubService; ItemClass = sample.ItemClass; Count = count;
        }
        private NativeBuildingPreference? Preference => _prefs.FirstOrDefault(p => (p.SteamId ?? "") == SteamId && p.Name == Asset);
        private NativeBuildingPreference Editable
        {
            get
            {
                var pref = Preference;
                if (pref != null) return pref;
                pref = new NativeBuildingPreference { SteamId = SteamId, Name = Asset };
                _prefs.Add(pref); return pref;
            }
        }
        public string HideShape
        {
            get => Preference?.HideShape?.ToString().ToLowerInvariant() ?? "";
            set { if (!string.IsNullOrWhiteSpace(value) && !bool.TryParse(value, out _)) throw new ArgumentException("HideShape accepts true, false, or blank."); Editable.HideShape = string.IsNullOrWhiteSpace(value) ? null : bool.Parse(value); }
        }
        public string NameVisibility
        {
            get => Preference == null || Preference.NameVisibility == CS2MapView.Config.NameVisibility.Undefined ? "" : Preference.NameVisibility.ToString();
            set { if (!string.IsNullOrWhiteSpace(value) && !Enum.TryParse<CS2MapView.Config.NameVisibility>(value, true, out _)) throw new ArgumentException("NameVisibility accepts Visible, FullVisible, Invisible, or blank."); Editable.NameVisibility = string.IsNullOrWhiteSpace(value) ? CS2MapView.Config.NameVisibility.Undefined : Enum.Parse<CS2MapView.Config.NameVisibility>(value, true); }
        }
        public string MapSymbol { get => Preference?.MapSymbol ?? ""; set => Editable.MapSymbol = string.IsNullOrWhiteSpace(value) ? null! : value; }
    }
}

[TypeConverter(typeof(ExpandableObjectConverter))]
internal sealed class FeatureObject : ICustomTypeDescriptor
{
    private readonly object _value;
    public FeatureObject(object value) => _value = value;
    private static bool IsComplex(Type type) => !type.IsPrimitive && !type.IsEnum && type != typeof(string) && type != typeof(decimal);
    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    public PropertyDescriptorCollection GetProperties() => new(TypeDescriptor.GetProperties(_value).Cast<PropertyDescriptor>()
        .Where(p => p.IsBrowsable && !new[] { "CombineNearbyDirections", "DirectionMergeDistance", "StopsMergingRange", "StationClusterRadius", "StationLinkDistance", "Version", "BuildingPreferences", "SampleRouteNumbering", "IgnoreBuildingShape", "IgnoreMapSymbols", "IgnoreSegments", "RenderAirplaneLine", "RenderAirplaneStop", "RenderShipLine", "RenderShipStop", "RenderMetroStop", "RenderTrainStop", "RenderMonorailStop", "RenderFerryStop", "HideCargoLines", "SafeStationClusterRadius", "SafeStationLinkDistance", "ShowRailInfrastructure", "ShowTransitRoutes", "ShowTransitLabels", "ShowStationPremises" }.Contains(p.Name))
        .Select(p => (PropertyDescriptor)new FeatureProperty(p, _value)).ToArray());
    public AttributeCollection GetAttributes() => AttributeCollection.Empty;
    public string? GetClassName() => _value.GetType().Name;
    public string? GetComponentName() => null;
    public TypeConverter GetConverter() => new ExpandableObjectConverter();
    public EventDescriptor? GetDefaultEvent() => null;
    public PropertyDescriptor? GetDefaultProperty() => null;
    public object? GetEditor(Type editorBaseType) => null;
    public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;
    public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
    public object GetPropertyOwner(PropertyDescriptor? pd) => this;
    public override string ToString() => _value.GetType().Name.Replace("Config", " settings");

    private sealed class FeatureProperty : PropertyDescriptor
    {
        private readonly PropertyDescriptor _property;
        private readonly object _owner;
        public FeatureProperty(PropertyDescriptor property, object owner) : base(property, [new CategoryAttribute(GetCategory(property.Name))]) { _property = property; _owner = owner; }
        private static string GetCategory(string name) => name.Contains("Font") ? "Typography" : name is "Railway" || name.StartsWith("RenderRail") ? "Rail infrastructure" : name.Contains("Station") || name == "FrameStopTracks" ? "Station areas" : name == "RouteMapConfig" || name.StartsWith("RenderTransport") ? "Transit and station display" : "Map appearance and layers";
        public override Type ComponentType => typeof(FeatureObject);
        public override bool IsReadOnly => _property.IsReadOnly || IsComplex(_property.PropertyType)
            || (_owner is CS2MapView.CS1.Config.TransportRouteMapConfig route &&
                (_property.Name.StartsWith("Transfer") && !route.UseCustomTransferModes
                 || _property.Name is "MergeBusTramStop" or "MergeTrainMetroStaion" && route.UseCustomTransferModes));
        public override Type PropertyType => IsComplex(_property.PropertyType) ? typeof(FeatureObject) : _property.PropertyType;
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component)
        {
            var value = _property.GetValue(((FeatureObject)component!)._value);
            return value != null && IsComplex(_property.PropertyType) ? new FeatureObject(value) : value;
        }
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value)
        {
            if (_property.Name is "StationClusterRadius" or "StationLinkDistance" && (value is not double distance || !double.IsFinite(distance) || distance < 0 || distance > 512))
                throw new ArgumentOutOfRangeException(_property.Name, "Station distances must be from 0 to 512 metres.");
            if (_property.Name is "CanvasSize" or "RenderAreaSize" && (value is not double size || !double.IsFinite(size) || size <= 0))
                throw new ArgumentOutOfRangeException(_property.Name, "Map sizes must be finite positive numbers.");
            _property.SetValue(((FeatureObject)component!)._value, value);
        }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
