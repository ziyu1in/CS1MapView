namespace CS2MapView.Form;

public partial class ConfigForm
{
    private ComboBox _languageSelector = null!;

    private void InitializeLanguageTab()
    {
        var page = new TabPage(UiText.Get("Language_Tab"))
        {
            Name = "LanguageConfigTabPage", UseVisualStyleBackColor = true, Padding = new Padding(16)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(0), Name = "LanguageLayout"
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = UiText.Get("Language_InterfaceLabel"), Name = "LanguageInterfaceLabel",
            AutoSize = true, Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        _languageSelector = new ComboBox
        {
            Name = "UILanguageCombobox", DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 320, AccessibleName = UiText.Get("Language_InterfaceLabel"),
            Margin = new Padding(0, 0, 0, 16)
        };
        _languageSelector.Items.AddRange(UiLocalization.Languages);
        _languageSelector.SelectedIndex = 0;
        layout.Controls.Add(_languageSelector, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = UiText.Get("Language_RestartHint"), Name = "LanguageRestartHintLabel",
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 12)
        }, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = UiText.Get("Language_FallbackHint"), Name = "LanguageFallbackHintLabel",
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 0, 0, 12)
        }, 0, 3);
        layout.Controls.Add(new Label
        {
            Text = UiText.Get("Language_Attribution"), Name = "LanguageAttributionLabel",
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0)
        }, 0, 4);
        page.Controls.Add(layout);
        ConfigTab.TabPages.Add(page);
    }

    private void LoadLanguagePreference()
    {
        var preference = UiLocalization.ResolveCulture(Context.UserSettings.UILanguage, UiLocalization.SystemUICulture).Name;
        var choice = UiLocalization.Languages.FirstOrDefault(x => string.Equals(x.Value, preference, StringComparison.OrdinalIgnoreCase));
        _languageSelector.SelectedItem = choice ?? UiLocalization.Languages[0];
    }

    private string SelectedLanguage => (_languageSelector.SelectedItem as UiChoice)?.Value ?? "en";
}
