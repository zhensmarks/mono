#!/bin/bash
# build-macos.sh - BMachine macOS Build Script
# Usage: ./build-macos.sh [x64|arm64|both]
# Note: Run this script on macOS to build natively

set -e

ARCH="${1:-both}"
PROJECT_PATH="src/BMachine.App/BMachine.App.csproj"
PUBLISH_BASE="publish-macos"

echo "========================================"
echo "  BMachine v2 - macOS Build Script"
echo "========================================"
echo ""

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "[ERROR] .NET SDK not found! Please install from https://dotnet.microsoft.com/download"
    exit 1
fi

# 1. Cleanup
echo "[1/5] Cleaning previous builds..."
rm -rf "$PUBLISH_BASE"
mkdir -p "$PUBLISH_BASE"
echo "[OK] Cleanup completed."
echo ""

# Build function
build_for_arch() {
    local arch=$1
    local rid="osx-$arch"
    local out_dir="$PUBLISH_BASE/$rid"
    
    echo "  - Building for $rid..."
    dotnet publish "$PROJECT_PATH" -c Release -r "$rid" --self-contained -o "$out_dir" > /dev/null 2>&1
    
    # Remove PDB files
    find "$out_dir" -name "*.pdb" -delete 2>/dev/null || true
    
    echo "    [OK] $rid build completed."
}

# 2. Build
echo "[2/5] Publishing for macOS..."

case "$ARCH" in
    x64)
        build_for_arch "x64"
        ;;
    arm64)
        build_for_arch "arm64"
        ;;
    both)
        build_for_arch "x64"
        build_for_arch "arm64"
        ;;
    *)
        echo "[ERROR] Invalid architecture: $ARCH (use x64, arm64, or both)"
        exit 1
        ;;
esac

echo "[OK] macOS builds completed."
echo ""

# 3. Sync content
echo "[3/5] Syncing content..."

for target in "$PUBLISH_BASE"/osx-*; do
    if [ -d "$target" ]; then
        # Scripts
        rm -rf "$target/Scripts"
        cp -r Scripts "$target/"
        
        # Plugins
        if [ -d "plugins" ]; then
            mkdir -p "$target/plugins"
            cp -r plugins/* "$target/plugins/" 2>/dev/null || true
        fi
    fi
done

echo "  - Scripts and Plugins synced."
echo "[OK] Content synced."
echo ""

# 4. Create .app bundle
echo "[4/5] Creating .app bundles..."

create_app_bundle() {
    local arch=$1
    local source_dir="$PUBLISH_BASE/osx-$arch"
    local staging_dir="$PUBLISH_BASE/staging-$arch"
    local app_name="BMachine.app"
    local app_path="$staging_dir/$app_name"
    
    if [ ! -d "$source_dir" ]; then
        return
    fi
    
    echo "  - Creating $app_name for $arch..."
    
    # Clean and create staging directory (this will contain ONLY BMachine.app)
    rm -rf "$staging_dir"
    mkdir -p "$staging_dir"
    
    # Create .app structure inside staging
    mkdir -p "$app_path/Contents/MacOS"
    mkdir -p "$app_path/Contents/Resources"
    
    # Copy executable and dependencies to MacOS folder
    cp -r "$source_dir/"* "$app_path/Contents/MacOS/"
    
    # Create Info.plist
    cat > "$app_path/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>BMachine</string>
    <key>CFBundleDisplayName</key>
    <string>BMachine</string>
    <key>CFBundleIdentifier</key>
    <string>com.bmachine.app</string>
    <key>CFBundleVersion</key>
    <string>2.0.1</string>
    <key>CFBundleShortVersionString</key>
    <string>2.0.1</string>
    <key>CFBundleExecutable</key>
    <string>BMachine.App</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>appicon</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

    # Create PkgInfo file (required for proper .app recognition)
    echo -n "APPL????" > "$app_path/Contents/PkgInfo"

    # Make executable
    chmod +x "$app_path/Contents/MacOS/BMachine.App"
    
    echo "    [OK] $app_name for $arch created."
}

create_app_bundle "x64"
create_app_bundle "arm64"

echo "[OK] App bundles created."
echo ""

# 5. Create DMG
echo "[5/5] Creating DMG installers..."

create_dmg() {
    local arch=$1
    local staging_dir="$PUBLISH_BASE/staging-$arch"
    local dmg_path="$PUBLISH_BASE/BMachine-$arch.dmg"
    
    if [ ! -d "$staging_dir/BMachine.app" ]; then
        echo "  [SKIP] No .app bundle found for $arch"
        return
    fi
    
    echo "  - Creating DMG for $arch..."
    
    # Remove old DMG if exists
    rm -f "$dmg_path"
    
    # Check if create-dmg tool is available (prettier DMG with background)
    if command -v create-dmg &> /dev/null; then
        create-dmg \
            --volname "BMachine" \
            --window-pos 200 120 \
            --window-size 600 400 \
            --icon-size 100 \
            --icon "BMachine.app" 150 200 \
            --app-drop-link 450 200 \
            --hide-extension "BMachine.app" \
            "$dmg_path" \
            "$staging_dir" 2>/dev/null && \
            echo "    [OK] $dmg_path created (with create-dmg)." && return
    fi
    
    # Fallback: Use built-in hdiutil (simpler but works)
    hdiutil create \
        -volname "BMachine" \
        -srcfolder "$staging_dir" \
        -ov \
        -format UDZO \
        "$dmg_path" 2>/dev/null && \
        echo "    [OK] $dmg_path created." || \
        echo "    [ERROR] DMG creation failed for $arch"
}

create_dmg "x64"
create_dmg "arm64"

echo ""
echo "========================================"
echo "  macOS BUILD COMPLETE!"
echo "========================================"
echo ""
echo "Output:"

for item in "$PUBLISH_BASE"/*; do
    if [ -e "$item" ]; then
        name=$(basename "$item")
        if [ -d "$item" ]; then
            size=$(du -sh "$item" 2>/dev/null | cut -f1)
            echo "  📁 $name ($size)"
        else
            size=$(du -h "$item" 2>/dev/null | cut -f1)
            echo "  💿 $name ($size)"
        fi
    fi
done

echo ""
echo "To install: Double-click the .dmg file and drag BMachine to Applications"
echo ""
