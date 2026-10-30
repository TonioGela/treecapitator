# Contributing

## Starting a new mod from this template

1. Create a repo from this template on GitHub ("Use this template").
2. Replace the placeholders:
   - `ValheimMod.csproj`: `AssemblyName` (DLL name), `PluginGuid`, `PluginName`. Optionally rename the file itself.
   - `thunderstore.toml`: `namespace` (your Thunderstore team), `name` (letters, digits and `_`; shown with spaces), `description`, `websiteUrl`.
   - `Plugin.cs`: the `namespace`.
   - `README.md`: the text, and the `YOUR_USER/YOUR_REPO` link.
   - `icon.png`: a 256×256 PNG.
3. For automatic releases, add the repo secrets for the stores you use (a store is skipped when its secrets are missing):
   - Thunderstore: `TCLI_AUTH_TOKEN`, a service account token (Thunderstore → Settings → Teams → your team → Service Accounts).
   - Nexus Mods: `NEXUSMODS_API_KEY` (your Personal API Key, at the bottom of nexusmods.com/settings/api-keys) and `NEXUSMODS_FILE_ID`. Nexus can only add versions to an existing file: create the mod page and upload the first `build/nexus/*.zip` by hand, then take the file ID from the page's Files tab ("Advanced").

## Building

Requires the .NET 8 SDK and `zip` (on Nix, `package.sh` enters a `nix-shell` with both automatically), Valheim, and BepInEx + [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) installed.

```sh
./package.sh
```

This compiles the mod, copies the DLL into `BepInEx/plugins/<AssemblyName>-dev/` so you can launch the game straight away, and writes to `build/`:

- `thunderstore/<namespace>-<name>-<version>.zip`: Thunderstore upload, or r2modman "Import local mod".
- `nexus/<AssemblyName>-<version>.zip`: Nexus Mods upload, `BepInEx/plugins/...` to extract into the game folder.

By default the build looks for Valheim in Steam's standard location and for BepInEx inside the game folder. To use other paths, e.g. an r2modman profile, create a `local.props` (gitignored) next to the project:

```xml
<Project>
  <PropertyGroup>
    <BepInExDir>$(HOME)/.config/r2modmanPlus-local/Valheim/profiles/YOUR_PROFILE/BepInEx</BepInExDir>
    <!-- <ValheimDir>/path/to/Valheim</ValheimDir> -->
  </PropertyGroup>
</Project>
```

Delete `BepInEx/plugins/<AssemblyName>-dev/` before playing with the released version, so the two don't both load.

## Versions and releases

Local and branch builds are versioned as the latest git tag with the minor bumped, e.g. `v1.0.1` → `1.1.0-SNAPSHOT`. Pushing a tag like `v1.2.0` makes CI build that exact version and publish it to every store whose secrets are set.
