#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

artifacts="artifacts/release"
plugin="bin/Release/net46/LoreleiAndSaneControls.dll"
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT

command -v zip >/dev/null || { echo "zip is required." >&2; exit 1; }
command -v clang >/dev/null || { echo "clang is required for the Linux AOT build." >&2; exit 1; }
[[ -d packaging/full ]] || { echo "The private packaging/full payload is missing." >&2; exit 1; }

dotnet build LoreleiAndSaneControls.csproj \
    --configuration Release \
    --framework net46

for runtime in win-x64 osx-arm64; do
    dotnet publish LoreleiAndSaneControls.csproj \
        --configuration Release \
        --framework net8.0 \
        --runtime "$runtime" \
        --self-contained true \
        --output "$temporary/$runtime"
done

dotnet publish LoreleiAndSaneControls.csproj \
    --configuration Release \
    --framework net8.0 \
    --runtime linux-x64 \
    --self-contained true \
    --output "$temporary/linux-x64"

mkdir -p "$temporary/full/BepInEx/plugins" "$temporary/minimal/BepInEx/plugins"
cp -R packaging/full/. "$temporary/full/"
cp "$plugin" "$temporary/full/BepInEx/plugins/"
cp "$plugin" "$temporary/minimal/BepInEx/plugins/"

(cd "$temporary/full" && zip -qr "$temporary/Lorelei.zip" .)
(cd "$temporary/minimal" && zip -qr "$temporary/Lorelei_minimal.zip" .)

rm -rf artifacts/release
mkdir -p "$artifacts"
cp "$temporary/win-x64/control_scheme_patcher.exe" "$artifacts/control_scheme_patcher.exe"
cp "$temporary/linux-x64/control_scheme_patcher" "$artifacts/control_scheme_patcher-linux"
cp "$temporary/osx-arm64/control_scheme_patcher" "$artifacts/control_scheme_patcher-macos-arm64"
mv "$temporary/Lorelei.zip" "$temporary/Lorelei_minimal.zip" "$artifacts/"
chmod +x "$artifacts/control_scheme_patcher-linux" "$artifacts/control_scheme_patcher-macos-arm64"

echo "Created release files in $artifacts"
