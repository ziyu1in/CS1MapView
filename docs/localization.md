# Interface localization

CS1MapView uses .NET `.resx` resources. English is the base language. The
Simplified and Traditional Chinese catalogs were translated by DeepSeek V4.1
Flash and supplied by the user; their wording is retained. Partial Japanese
support is inherited from gansaku's CS2MapView translations in unchanged form
resources. New strings and missing or stale catalog entries use the neutral
English fallback. No new Japanese translations or localization files from the
older CSLMapView are included.

## Choosing a language

Open **Options → Language** to choose and save the interface language. Restart
the viewer to apply the choice to all windows.

| Language | Culture |
| --- | --- |
| English | `en` |
| Japanese (partial) | `ja-JP` |
| Simplified Chinese | `zh-Hans` |
| Traditional Chinese | `zh-Hant` |

The choice is stored as `UILanguage` in `CS1MapView.settings`. When the choice is
empty or absent, the viewer uses the Windows interface language if it is available;
otherwise it uses English. Unavailable saved choices also use English. Changing
the interface language in this tab leaves regional number formats and saved map
settings unchanged.

Regional Chinese preferences map to a script: `zh-CN` and `zh-SG` use Simplified
Chinese; `zh-TW`, `zh-HK` and `zh-MO` use Traditional Chinese. A neutral `zh`
preference uses Simplified Chinese. Explicit `zh-Hans` and `zh-Hant` choices use
their corresponding scripts.

The existing startup switch overrides the interface language for that launch
and also selects regional number formats:

```powershell
./src/CS2MapView.Form/bin/Release/net8.0-windows/CS1MapView.exe -locale=ja-JP
```

The interface uses the requested language when it is available, with English
fallback for untranslated text. An unavailable interface language uses English.
The switch does not overwrite the saved language choice. Render command numeric
arguments continue to use the invariant format, such as `--rotate 12.5`.

## Resource files

| File | Text covered |
| --- | --- |
| `src/CS2MapView.Form/Localization/UiText.resx` | Dynamic menus, feature settings, table headings, dialogs, export messages, CLI help and Language tab |
| `src/CS2MapView.Form/MainForm.resx` and `ConfigForm.resx` | Inherited designer-managed form captions and layout |
| `src/CS2MapView.Form/MainForm.ja-JP.resx` and `ConfigForm.ja-JP.resx` | gansaku's existing Japanese translations for inherited form controls, included unchanged |
| `src/CS2MapView.Core/Resources.resx` | Import errors and map-building progress messages |
| `src/CS2MapView.Form/Localization/UiText.<culture>.resx` | User-supplied Chinese feature text, with `<culture>` set to `zh-Hans` or `zh-Hant` |
| `src/CS2MapView.Form/MainForm.<culture>.resx` and `ConfigForm.<culture>.resx` | User-supplied Chinese form text for both scripts |
| `src/CS2MapView.Core/Resources.<culture>.resx` | User-supplied Chinese import and progress text for both scripts |

The two Japanese form resources are restored byte-for-byte from the original
CS2MapView baseline and included in the build. Credit for their existing
Japanese translations belongs to gansaku. Their original resource IDs, text and
layout data remain unchanged. New captions and features read neutral English
resources, so the partial Japanese interface can contain both languages.

The Chinese adaptation includes eight resource files across the two scripts.
Validation is technical only: resource XML, current IDs, formatting placeholders,
resource loading and language selection. It does not assess translation meaning,
terminology or style, and does not rewrite the supplied wording. Entries without
a usable current ID use English fallback; stale IDs do not create new UI text.

There is no spreadsheet import step. Edit the resource files directly.

## Adding and translating text

1. Add a descriptive, stable ID to the neutral resource, such as
   `Menu_MapStyle` or `Export_ImageSaved`. Keep existing IDs stable.
2. Reference the ID through `UiText.Get(id)`, or use `UiText.Format(id, values)`
   for messages with placeholders. Inherited Forms controls continue to use
   their existing `ApplyResources` mechanism.
3. When translations are requested later, add entries with the same IDs to the
   culture resource. Omit untranslated entries to use English fallback.
   Preserve placeholder indices
   such as `{0}`, `{1:0.0}` and any intended newlines. Preserve menu accelerators
   introduced by `&` without duplicating them in the same menu.
4. Keep settings values separate from display captions. `UiChoice` associates a
   stable value with a resource ID; captions must not be serialized as
   contour presets, font names, locale names or other settings keys.
5. Build and run the localization checks. They exercise fallback, formatting,
   dynamic property captions, saved language choices, Apply/Cancel and the
   mapping from resource-backed options back to stable values.

```powershell
./build.ps1
./tests/CS1MapView.Checks/bin/Release/net8.0-windows/CS1MapView.Checks.exe . 'C:/path/to/reference.cslmap.gz' --localization-only
```

When adding a UI language later, add `UiText.<culture>.resx` and the required
form/core overrides, then add its culture code and a resource ID for its native
language name to `UiLocalization.Languages`. English, Japanese, Simplified Chinese
and Traditional Chinese are already registered. Japanese uses the inherited form
resources; its untranslated feature strings use English until translations are
explicitly added. The SDK builds the satellite resource
assemblies; keep the culture directories with the runnable application.

City names, asset names, custom line shorthands, style/font/symbol identifiers
and editable enum values remain data. UI localization does not translate them.
