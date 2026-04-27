# FullStatGUI

| | |
|-|-|
| **Mod id** | `fullstatgui` |
| **Version** | 0.1.0 |
| **Game** | Vintage Story 1.22.0+ |
| **.NET** | 10+ (`net10.0`) |

**Client-only** debug UI: on **Character (C)**, a scrollable panel lists **GetBlended** stats, health/hunger/body temp and other `WatchedAttribute` fields, plus a **full hunger** sub-tree export. For modding and troubleshooting. See `modinfo.json`.

## Build

- Edit **`Directory.Build.props`** or pass `VintageStoryPath` / set `VINTAGE_STORY_PATH` to the folder that contains `VintagestoryAPI.dll`.
- `dotnet build FullStatGUI.csproj -c Release`

**Deploy:** the `.csproj` can copy the DLL + `modinfo` to this folder, `out/ForMods/`, and (on Windows) `%APPDATA%\Roaming\VintagestoryData\Mods\FullStatGUI\` unless you set `FullStatGuNoDeploy=true`.

## Layout

- `src/`, `assets/`, `modinfo.json`

## License

[MIT](LICENSE)

**Author:** adams.
