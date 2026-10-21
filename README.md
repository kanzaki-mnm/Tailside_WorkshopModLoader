# Tailside Workshop Mod Loader

Unofficial Workshop support for the Steam version of [Tailside](https://store.steampowered.com/app/2922570/), using BepInEx 5 (Unity Mono).
It loads compatible subscribed Workshop mods while keeping normal `BepInEx/plugins` available. Ordinary character skins do not need this loader.

A **mod environment** is the shared setup that lets the game run compatible mods. Install it once before using those mods.

**Start with [GitHub Releases](https://github.com/kanzaki-mnm/Tailside_WorkshopModLoader/releases) and choose one installation method below.**

## Contents

- [Downloads and requirements](#downloads-and-requirements)
- [Assisted installation](#assisted-installation)
- [Manual installation](#manual-installation)
- [Using mods](#using-mods)
- [Updating, disabling and removing](#updating-disabling-and-removing)
- [Troubleshooting](#troubleshooting)
- [Steam Deck and Proton — untested](#steam-deck-and-proton--untested)
- [Tool languages](#tool-languages)
- [For mod authors](#for-mod-authors)
- [Building from source](#building-from-source)
- [Testing and known limits](#testing-and-known-limits)
- [License](#license)

## Downloads and requirements

Choose **one** installation download from [GitHub Releases](https://github.com/kanzaki-mnm/Tailside_WorkshopModLoader/releases):

| Download | Use it for |
| --- | --- |
| `TailsideModSetup.exe` | Assisted Windows installation and recovery |
| `TailsideWorkshopModLoader-manual.zip` | Manual installation, including existing BepInEx installations |

GitHub's automatic **Source code (zip / tar.gz)** archives are for developers, not installation packages. `ThirdPartyNotices.txt`, `SHA256SUMS.txt` and `UnityDoorstop-4.5.0-source.zip` are reference/source files; they do not need to be copied into the game folder.

The loader itself does **not** require a Workshop subscription. Individual compatible mods still use Workshop.

- Steam version of Tailside, Windows x64, Unity Mono, BepInEx **5.4.23.5**.
- Tested on Windows 11. Windows 10 and a clean Windows machine without development tools have not been tested.
- The optional setup tool includes its .NET runtime. Users do not need Python, the .NET SDK or a separate .NET installation.
- Steam Deck / Proton is untested; see the experimental manual steps below. Native Linux/macOS, IL2CPP and BepInEx 6 are outside the supported scope.

## Assisted installation

1. Close Tailside and download `TailsideModSetup.exe` from Releases.
2. Run it, check the selected Tailside folder, and choose **Install**.
3. Subscribe to a compatible mod, wait for Steam to finish downloading, and launch Tailside through Steam.

The tool checks that the selected folder contains the expected game executable and Mono layout. An unrelated folder is not a valid installation target.

Installation retains a copy at `BepInEx/TailsideModSetup/TailsideModSetup.exe`, so you can delete the original download afterward. You can also download the tool again later to update or remove a compatible tool-managed installation.

Existing manual installations are **not adopted or changed automatically**. Use the manual instructions below instead.

## Manual installation

1. Close Tailside.
2. Download the official [BepInEx 5.4.23.5 Windows x64 archive](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) and extract it beside `Tailside.exe`. Skip this if that BepInEx environment is already installed.
3. Extract the manual loader ZIP and copy its DLL to this exact location:

```text
Tailside/
  Tailside.exe
  BepInEx/
    patchers/
      TailsideWorkshopModLoader.dll
```

Find the game folder through Steam Library → right-click Tailside → Manage → Browse local files.

No manual `BepInEx.cfg` edit is required while this loader is installed. Do not install the retired `WorkshopBridge.dll` alongside it.

## Using mods

Subscribe to individual Workshop mods that explicitly support this loader. Wait for downloads to finish and launch the game through Steam. Restart the game after adding, updating or unsubscribing from mods.

The loader uses Steam's current subscription list and accessible installed-item paths. It skips items that are downloading or awaiting an update. If Steam information is unavailable, it does not load leftover folders as a substitute.

It reads Workshop files in place: it does not copy, rewrite or automatically download them. It does not install anything in the game's Managed folder.

## Updating, disabling and removing

Close Tailside before changing the environment.

### Tool-managed installations

Run the retained tool or a newly downloaded compatible `TailsideModSetup.exe`.

- **Update** appears when a newer bundled environment or tool copy is available. Mods and settings are retained.
- **Temporarily disable / Re-enable** stops or restores the managed environment without deleting mods and settings.
- **Remove** removes verified owned files. Review the removal preview and result.
- **Game won't start? Repair & recovery** provides repair, the game folder and diagnostic reports. Explicit repair backs up changed owned files; unknown conflicts are not silently overwritten.

Keep the installation records under `BepInEx/TailsideModSetup/data` until removal. The tool does not guess ownership when records are missing or invalid.

Removal leaves mods, user settings, unknown or modified files, and the retained tool's own files. After reviewing any warnings, save reports/backups you need, close the tool, and manually delete the remaining `BepInEx/TailsideModSetup` folder if no longer needed. A warning about remaining files should be investigated first.

### Manual installations

**Update the loader:** replace `BepInEx/patchers/TailsideWorkshopModLoader.dll` with the new manual ZIP's DLL. This is separate from individual mods' Workshop updates.

**Disable or remove only the loader:** move that DLL outside BepInEx. BepInEx and normal plugins remain installed. If you continue using normal plugins without this loader, set `HideManagerGameObject = true` in `BepInEx/config/BepInEx.cfg`; the loader's session-only startup protection is no longer applied.

**Stop all BepInEx mods in the standard installation described here:** move the BepInEx-provided `winhttp.dll` outside the game folder. Keep it to restore the environment later. Do not move an unknown DLL belonging to another tool.

To remove remaining manually installed BepInEx files, first back up mods/settings you want to keep, then remove only files you know you installed. This is not a general uninstaller for other loaders or directly modified game files.

## Troubleshooting

- First check whether `BepInEx/LogOutput.log` exists. If BepInEx did not start, check its package and file placement first.
- For Workshop loading, look for `TailsideWorkshopModLoader` entries in `BepInEx/LogOutput.log`, launch through Steam and let downloads finish. The loader does not create a separate log or change BepInEx logging settings.
- The optional tool offers **Save report** under recovery. It remains usable independently of a working game or BepInEx runtime.
- Manual or unverified installations receive instructions rather than automatic file changes. A damaged ownership record requires a trusted backup or manual investigation.
- After a game runtime change, such as Mono being replaced by IL2CPP, the tool refuses unsupported installation/repair/re-enable operations. Removal can still use valid ownership records.

Tool-managed layout:

```text
BepInEx/
  patchers/TailsideWorkshopModLoader.dll
  TailsideModSetup/
    TailsideModSetup.exe
    languages/
    licenses/
    logs/reports/
    data/                 # ownership records, preferences and recovery backups
```

Normal BepInEx `config`, `plugins` and `LogOutput.log` remain in their usual locations. The manual loader does not create the setup folder or ownership records; its diagnostics use BepInEx logging.

## Steam Deck and Proton — untested

These steps have **not** been tested on a Steam Deck or Linux PC. Tailside's Deck compatibility does not verify BepInEx, this loader or individual mods. The setup exe is not guaranteed on Deck; use manual installation.

1. Switch to Desktop Mode, close Tailside and use Steam to browse its game files.
2. Extract **Windows x64** BepInEx 5.4.23.5 beside `Tailside.exe`. Proton runs the Windows game.
3. If Steam's Launch Options field is empty, enter:

   ```text
   WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
   ```

   If you already have launch options, save them first and combine carefully rather than replacing them blindly.
4. Install the manual loader DLL at `BepInEx/patchers/TailsideWorkshopModLoader.dll`.
5. Subscribe to a compatible mod, wait for its download and launch through Steam. Gaming Mode and SD-card libraries are unverified.

Troubleshoot BepInEx startup before Workshop loading. The loader uses paths supplied by Steam; it does not guess drive letters or search leftover folders. When reporting tests, include Proton version, Desktop/Gaming Mode and internal/SD-card storage.

To undo these steps, close the game, move the BepInEx-provided `winhttp.dll` outside the game folder, and remove only the launch-option addition above. For loader-only removal, follow the manual instructions above.

Reference: [BepInEx Proton/Wine guidance](https://docs.bepinex.dev/master/articles/advanced/steam_interop.html#protonwine).

## Tool languages

The app keeps automatic OS-language selection and manual language switching. English and Japanese packs are external JSON files at `BepInEx/TailsideModSetup/languages`. Clean first use is English before external translations are installed; missing translations fall back to English. Updates add newly introduced keys to existing valid English/Japanese packs while preserving existing values and custom keys. Removed packs remain optional; malformed packs are left unchanged.

To add a language, copy `en.json`, set `languageCode` to a BCP 47 tag (for example `fr` or `pt-BR`), set `displayName` to the language's own name, and translate values in `translations`. Keep English keys, `{0}` placeholders and intentional newlines unchanged. Save UTF-8 JSON and restart the tool.

Missing, blank or invalid-format entries use English. Malformed/oversized packs and duplicate language codes are skipped. Limits: 256 KiB per file and up to 100 JSON files. Auto tries exact OS language, base language, then English. Preferences are kept in `data/settings.json`; removed packs are not silently recreated on launch. Install/repair can supply defaults without overwriting existing packs.

## For mod authors

A compatible Workshop item contains:

```text
MyMod/
  mod.json
  plugins/MyMod.dll
```

```json
{
  "schemaVersion": 1,
  "id": "yourname.mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "gameAppId": 2922570,
  "entryAssembly": "plugins/MyMod.dll",
  "minimumLoaderVersion": "1.0.0"
}
```

Match `id` and `version` to the DLL's `BepInPlugin` attributes. Declare one plugin in the entry DLL. Ordinary character skins without `mod.json` are ignored. Old prerelease manifests/settings are not migrated automatically.

Development builds can use normal `BepInEx/plugins`. Before testing a subscribed Workshop build, move the same mod's development copy and its private helper DLLs out of plugins. A normal plugin with the same mod ID takes precedence; duplicate Workshop mod IDs are excluded. Colliding entry-assembly names are also excluded.

Give private helper libraries unique assembly names, preferably including author/mod names. Renaming a file or changing its version alone does not prevent assembly conflicts. Check compatibility when sharing libraries between mods.

The loader adds compatible subscribed mods to BepInEx's loading scope. It does not select dependency versions, replace libraries, isolate mods, or comprehensively detect helper-DLL collisions. Search paths/order differ, so collision behavior is not guaranteed identical to BepInEx alone. Mods run with the game's permissions and may affect the game or other mods.

Loader validation failure skips Workshop candidates while retaining normal-plugin discovery. This does not contain failures inside loaded mods or dependency conflicts. Write your mod's configuration/logs to normal mod-owned locations, never into Workshop distribution files.

For individual Workshop pages, link users to this README's installation section and the Releases page. Explain that the loader is installed once, then mods are subscribed to normally; the loader itself needs no Workshop subscription.

## Building from source

Build requirements are for developers, not users of the setup exe: Windows, PowerShell 7, .NET 10 SDK (verified with 10.0.401), your own Steam Tailside installation, and official BepInEx 5.4.23.5 Windows x64 files.

```powershell
./Build.ps1 -GameRoot 'D:/SteamLibrary/steamapps/common/Tailside' `
  -LoaderCore 'D:/Dependencies/BepInEx/core' -RunChecks
```

Include the optional setup tool:

```powershell
./Build.ps1 -GameRoot 'D:/SteamLibrary/steamapps/common/Tailside' `
  -LoaderCore 'D:/Dependencies/BepInEx/core' `
  -BepInExZip 'D:/Dependencies/BepInEx_win_x64_5.4.23.5.zip' `
  -WithTools -RunChecks
```

`GameRoot` contains `Tailside.exe`; `ManagedDir` defaults to `GameRoot/Tailside_Data/Managed`; `LoaderCore` defaults to `GameRoot/BepInEx/core`. Specify `-ManagedDir` if needed. Without `GameRoot`, the standard Windows Steam installation path is used. Script-relative inputs are resolved from the current working directory.

For loader-local overrides, copy `TailsideWorkshopModLoader/Build.local.props.example` to `Build.local.props` and edit it. Command-line settings take precedence. Never commit the local file. Direct MSBuild calls accept `-p:GameRoot=...`, `-p:LoaderCore=...` and `-p:ManagedDir=...`.

The loader targets netstandard2.1. The tool pins its self-contained runtime to 10.0.12. The loader and tool use project-specific NuGet configs with cleared package sources. For offline builds, matching SDK/reference/runtime packs must already be available; configure trusted feeds explicitly if needed on another machine. Game/Unity DLLs are external build references and must not be committed or bundled into the release.

Outputs, ignored by Git:

- `TailsideWorkshopModLoader/release/BepInEx/patchers/TailsideWorkshopModLoader.dll`
- `TailsideModSetup/release-preview/TailsideModSetup.exe` and third-party notices

The direct tool build requires `./TailsideModSetup/Build.ps1 -DevelopmentPreview -BepInExZip <archive>`. This explicit switch builds a local package; build scripts never upload or publish it. Unattended GitHub Actions builds are not configured; game-reference DLLs must be supplied separately.

Source layout: `TailsideWorkshopModLoader` contains the runtime loader; `TailsideModSetup` contains the optional setup/recovery app, installation and recovery Core, localization, icons and third-party notices. `tests` contains all checks, isolated-game drivers and fixtures. `tests/Loader/SmokeProbe` is a test-only plugin and manifest, never a release payload. The retained management helpers in `TailsideModSetup/Core/Management` and `Core/LegacyBridge` are internal implementation; old APIs and namespaces remain for a separate cleanup, not additional distributed apps.

## Testing and known limits

`Build.ps1 -RunChecks` builds test helpers and runs non-game checks. It does not launch or change the game. Run `./Check-Source.ps1` before staging to check publication candidates for excluded products/state, representative credential patterns, personal paths and documentation links. This is not a complete security audit.

Runtime scripts require an explicitly marked **isolated game copy**, Steam and built test helpers. Never run them against the normal game installation. They back up/restore test files; if a test process cannot be stopped, restoration is deferred and the backup location is reported.

From the repository root, available checks include:

```powershell
./tests/Loader/Run-Checks.ps1 -GameRoot <game> -LoaderCore <core>
./tests/Loader/Test-BuildReferences.ps1 -GameRoot <game> -LoaderCore <core>
./tests/Loader/Test-Runtime.ps1 -SandboxRoot <marked-copy> -FullStartup -WithGraphics
./tests/Loader/Test-SessionProtection.ps1 -SandboxRoot <marked-copy>
./tests/Loader/Test-ProbeCleanup.ps1
./tests/Loader/Test-CleanupLogFailure.ps1 -SandboxRoot <marked-copy>
```

Dependency experiments: `tests/Loader/Collision/Run.ps1 -SandboxRoot <marked-copy> -Mode bep` or `-Mode loader`. Build fixtures with the root check command or `tests/Loader/Collision/Build-Fixtures.ps1`. Injector/fixture DLLs are test-only and must never enter a normal game or release. The optional `-LocationFallback` workaround is historical test instrumentation, not needed by the current loader or a shipping fix.

Verified locally: zero-warning builds, loader metadata/real-DLL and cleanup checks, ownership/recovery checks, unrelated-folder refusal, path-link write protection, state-specific English/Japanese UI, isolated installation/update/removal and normal-plugin coexistence. Current recorded counts include 161 unified Core checks, 37 Setup checks, 34 loader cleanup + 43 loader Core + 25 real-DLL checks, and 112 checks across four recovery/safety UI scenarios. These summarize separate runs, not a claim that every test was rerun together after documentation changes.

Also verified in the marked isolated game: installation/update/removal from downloaded draft assets, vanilla startup after disabling/removing BepInEx, six actual Windows permission-denial cases, and real UAC acceptance/cancellation. Friendly English/Japanese permission and cancellation notices passed 62 UI checks; technical details remain in diagnostic reports. These were separate runs, not one complete rerun of every check. The Releases/help URL is configured.

Maintainer tests on the normal Windows 11 game installation also passed: manual and downloaded-exe installation, temporary disable/re-enable/removal, and real private Workshop subscription/update/unsubscription with both installation methods. Steam removed the files on unsubscription, so retained unsubscribed files were not exercised in these live runs. The private test item was deleted afterward and 12 everyday plugins/settings were restored and loaded successfully. Third-party notices and the fixed UnityDoorstop corresponding-source archive are supplied with the release candidate.

Windows 10, clean Windows and Deck/Proton remain untested. Runtime/tool update is not a single power-loss-proof transaction; retry may be needed if copying the retained exe fails after an environment update.

Historical development/validation notes remain local and are excluded from publication. This README is the maintained public guide.

## License

My original code and project icons are licensed under [0BSD](https://spdx.org/licenses/0BSD).
Third-party components retain their respective licenses. Their original notices are preserved in `TailsideModSetup/Assets` and included with applicable binary distributions.


The assisted installer includes unmodified UnityDoorstop 4.5.0 (LGPL-2.1). Its complete [corresponding source archive](https://github.com/kanzaki-mnm/Tailside_WorkshopModLoader/releases/download/v1.0.0/UnityDoorstop-4.5.0-source.zip), including build scripts, is supplied alongside the installer on the same release. It is not an installation package. Source commit: `33dab9a6733862eb81869ff08431d9478b28784b`. See `ThirdPartyNotices.txt` for component versions, source locations and original license texts.
