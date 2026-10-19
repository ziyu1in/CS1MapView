using CS2MapView.CS1.Config;

namespace CS2MapView.Form;

public partial class ConfigForm
{
    private CheckBox _hillshadingCheckbox = null!;
    private NumericUpDown _hillshadingStrength = null!;
    private CheckBox _contourLinesCheckbox = null!;

    private void InitializeTerrainControls()
    {
        // Use the spare second row beside water settings without changing the
        // inherited layouts or their Japanese/Chinese resource coordinates.
        var row = new FlowLayoutPanel {
            Name = "HillshadingSettingsPanel", AutoSize = false, WrapContents = false,
            Location = new Point(ContourLinesIntervalsLabel.Left, VectorizeWaterCheckbox.Top - 4),
            Size = new Size(groupBox1.ClientSize.Width - ContourLinesIntervalsLabel.Left - 8, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _hillshadingCheckbox = new CheckBox {
            Name = "HillshadingCheckbox", Text = UiText.Get("Feature_Property_RenderHillshading"),
            AutoSize = true, Margin = new Padding(0, 5, 12, 0), TabIndex = 0
        };
        var label = new Label {
            Name = "HillshadingStrengthLabel", Text = UiText.Get("Options_HillshadingStrength"),
            AutoSize = true, Margin = new Padding(0, 6, 6, 0)
        };
        _hillshadingStrength = new NumericUpDown {
            Name = "HillshadingStrengthInput", Minimum = 0, Maximum = 100, Value = 82,
            Width = 62, Increment = 5, Margin = new Padding(0, 2, 0, 0), TabIndex = 1,
            AccessibleName = UiText.Get("Feature_Property_HillshadingStrength")
        };
        _hillshadingCheckbox.CheckedChanged += (_, _) => _hillshadingStrength.Enabled = _hillshadingCheckbox.Checked;
        row.Controls.AddRange([_hillshadingCheckbox, label, _hillshadingStrength]);
        groupBox1.Controls.Add(row);
        ContourLinesIntervalsLabel.Visible = false;
        _contourLinesCheckbox = new CheckBox {
            Name = "ContourLinesCheckbox", Text = UiText.Get("Options_ContourLines"), AutoSize = true,
            Location = new Point(ContourLinesIntervalsLabel.Left, VectorizeLandCheckbox.Top), TabIndex = 2
        };
        _contourLinesCheckbox.CheckedChanged += (_, _) => ContourIntervalCombobox.Enabled = _contourLinesCheckbox.Checked;
        groupBox1.Controls.Add(_contourLinesCheckbox);
        ContourIntervalCombobox.Width = groupBox1.ClientSize.Width - ContourIntervalCombobox.Left - 8;
        ContourIntervalCombobox.AccessibleName = UiText.Get("Feature_Property_ContourDetailLevel");
    }

    private void LoadHillshadingControls(ApplicationConfig config)
    {
        _hillshadingCheckbox.Checked = config.RenderHillshading;
        _hillshadingStrength.Value = config.HillshadingStrength;
        _hillshadingStrength.Enabled = config.RenderHillshading;
    }

    private bool SetHillshadingValues(ApplicationConfig config)
    {
        bool changed = config.RenderHillshading != _hillshadingCheckbox.Checked
            || config.HillshadingStrength != (int)_hillshadingStrength.Value;
        config.RenderHillshading = _hillshadingCheckbox.Checked;
        config.HillshadingStrength = (int)_hillshadingStrength.Value;
        return changed;
    }

    private void LoadContourControls(ApplicationConfig config)
    {
        ContourIntervalCombobox.Items.Clear();
        ContourIntervalCombobox.Items.AddRange(ContourDetailChoices.Items);
        ContourIntervalCombobox.SelectedItem = ContourIntervalCombobox.Items.Cast<UiChoice>()
            .Single(x => x.Value == config.ContourDetailLevel.ToString());
        _contourLinesCheckbox.Checked = config.DisplayContourLines;
        ContourIntervalCombobox.Enabled = config.DisplayContourLines;
    }

    private bool SetContourValues(ApplicationConfig config)
    {
        var selected = (ContourIntervalCombobox.SelectedItem as UiChoice)?.Value;
        var detail = Enum.TryParse<ContourDetail>(selected, out var value) ? value : ContourDetail.Balanced;
        bool changed = config.DisplayContourLines != _contourLinesCheckbox.Checked || config.ContourDetailLevel != detail;
        config.DisplayContourLines = _contourLinesCheckbox.Checked;
        config.ContourDetailLevel = detail;
        return changed;
    }
}
