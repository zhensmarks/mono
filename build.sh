#!/bin/bash

# Exit on error
set -e

APP_NAME="BMachine"
PROJECT_PATH="src/BMachine.App/BMachine.App.csproj"
OUTPUT_DIR="Build"
SKIASHARP_VERSION="2.88.8"

echo "🚀 Starting Build Process for $APP_NAME..."

# Detect OS
OS="$(uname -s)"
case "${OS}" in
    Linux*)     OS=Linux;;
    Darwin*)    OS=Mac;;
    CYGWIN*)    OS=Cygwin;;
    MINGW*)     OS=MinGw;;
    *)          OS="UNKNOWN:${OS}"
esac

echo "🖥️  Detected OS: $OS"

if [ "$OS" == "Mac" ]; then
    ARCH="$(uname -m)"
    if [ "$ARCH" == "arm64" ]; then
        LOCAL_TARGET="osx-arm64"
    else
        LOCAL_TARGET="osx-x64"
    fi

    if [ "$1" == "all" ]; then
        echo "🍏 Building for macOS (Full: ARM64 & x64)..."
        TARGETS=("osx-arm64" "osx-x64")
    else
        echo "🍏 Building for macOS (Local: $LOCAL_TARGET)..."
        TARGETS=("$LOCAL_TARGET")
    fi
    
    for TARGET in "${TARGETS[@]}"; do
        echo "   🔨 Publishing $TARGET..."
        dotnet publish "$PROJECT_PATH" -c Release -r "$TARGET" --self-contained true -p:PublishSingleFile=false -o "$OUTPUT_DIR/$TARGET"

        # Create App Bundle Structure
        APP_BUNDLE="$OUTPUT_DIR/$APP_NAME-$TARGET.app"
        CONTENTS="$APP_BUNDLE/Contents"
        MACOS="$CONTENTS/MacOS"
        RESOURCES="$CONTENTS/Resources"

        echo "   📦 Creating App Bundle: $APP_BUNDLE"
        rm -rf "$APP_BUNDLE"
        mkdir -p "$MACOS"
        mkdir -p "$RESOURCES"

        # Copy ALL files from publish dir to MacOS folder
        cp -a "$OUTPUT_DIR/$TARGET/." "$MACOS/"

        # ── FIX: Replace libSkiaSharp.dylib with the correct version ──
        # dotnet publish may copy the wrong version from NuGet cache.
        # We force-copy the correct one matching Avalonia's SkiaSharp 2.88.x
        CORRECT_SKIA="$HOME/.nuget/packages/skiasharp.nativeassets.macos/$SKIASHARP_VERSION/runtimes/osx/native/libSkiaSharp.dylib"
        if [ -f "$CORRECT_SKIA" ]; then
            echo "   🔧 Fixing libSkiaSharp.dylib → v$SKIASHARP_VERSION"
            cp "$CORRECT_SKIA" "$MACOS/libSkiaSharp.dylib"
        else
            echo "   ⚠️  Warning: Correct libSkiaSharp.dylib not found at $CORRECT_SKIA"
        fi

        # Copy Info.plist
        if [ -f "src/BMachine.App/Info.plist" ]; then
            cp "src/BMachine.App/Info.plist" "$CONTENTS/"
        else
            echo "   ⚠️  Warning: Info.plist not found!"
        fi

        # Copy Icon
        if [ -f "src/BMachine.App/app.icns" ]; then
            cp "src/BMachine.App/app.icns" "$RESOURCES/"
        else
            echo "   ⚠️  Warning: app.icns not found!"
        fi

        # ── FIX: Remove macOS quarantine flag so Finder allows double-click ──
        echo "   🔓 Removing Gatekeeper quarantine..."
        xattr -cr "$APP_BUNDLE"

        echo "   ✅ $TARGET bundle ready: $APP_BUNDLE"
    done

elif [ "$OS" == "Linux" ]; then
    echo "🐧 Building for Linux (x64)..."
    dotnet publish "$PROJECT_PATH" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o "$OUTPUT_DIR/linux-x64"
    echo "✅ Linux build complete: $OUTPUT_DIR/linux-x64"

else
    echo "⚠️  Unsupported OS for this script: $OS"
fi

echo "🎉 Build Finished Successfully!"
