#!/usr/bin/env bash
# Builds the mod and writes to build/:
#   thunderstore/<namespace>-<name>-<version>.zip  Thunderstore upload or r2modman "Import local mod"
#   nexus/<AssemblyName>-<version>.zip             Nexus Mods upload: BepInEx/plugins/... to extract into the game folder
# The version comes from $VERSION (CI sets it to the tag); otherwise it's the latest tag with the minor bumped, as a snapshot.
set -euo pipefail
cd "$(dirname "$0")"
{ command -v dotnet && command -v zip; } >/dev/null || exec nix-shell -p dotnet-sdk_8 zip --run ./package.sh
if [ -n "${VERSION:-}" ]; then
  v="${VERSION#v}" snapshot=""
else
  last=$(git describe --tags --abbrev=0 2>/dev/null || echo v0.0.0)
  IFS=. read -r major minor _ <<< "${last#v}"
  v="$major.$((minor + 1)).0" snapshot="-SNAPSHOT"
fi
# tcli and BepInEx's [BepInPlugin] only accept plain x.y.z, so -SNAPSHOT only goes in the DLL's informational version and the zip names
export DOTNET_ROLL_FORWARD=Major # tcli targets net6
rm -rf build
dotnet build -c Release -nologo -p:Version="$v" -p:InformationalVersion="$v$snapshot"
dll=$(dotnet msbuild -p:Configuration=Release -getProperty:TargetPath)
name=$(basename "$dll" .dll)

mkdir -p build/plugin && cp "$dll" build/plugin/
dotnet tool restore
dotnet tcli build --package-version "$v"
[ -z "$snapshot" ] || for f in build/thunderstore/*.zip; do mv "$f" "${f%.zip}$snapshot.zip"; done

mkdir -p "build/nexus/BepInEx/plugins/$name" && cp "$dll" "build/nexus/BepInEx/plugins/$name/"
(cd build/nexus && zip -qr "$name-$v$snapshot.zip" BepInEx && rm -rf BepInEx)
