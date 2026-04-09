#!/bin/bash

# Photo Sorter Installation Script
# Double-click this file to install the Photo Sorter application

echo "📸 Photo Sorter Installation"
echo "============================"
echo ""
echo "This will install Photo Sorter to your Applications folder."
echo ""

# Check if the .app bundle exists
if [ ! -d "Photo Sorter.app" ]; then
    echo "❌ Error: Photo Sorter.app not found!"
    echo "Please run create-mac-app.sh first to build the application."
    exit 1
fi

# Ask for confirmation
read -p "Install Photo Sorter to /Applications? (y/n): " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Installation cancelled."
    exit 0
fi

# Install to Applications folder
echo "📦 Installing Photo Sorter..."
sudo rm -rf "/Applications/Photo Sorter.app" 2>/dev/null
sudo cp -r "Photo Sorter.app" "/Applications/"

# Create Desktop shortcut (optional)
read -p "Create shortcut on Desktop? (y/n): " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "📋 Creating Desktop shortcut..."
    rm -f "$HOME/Desktop/Photo Sorter" 2>/dev/null
    ln -s "/Applications/Photo Sorter.app" "$HOME/Desktop/Photo Sorter"
fi

# Fix permissions
echo "🔧 Setting permissions..."
sudo chmod -R 755 "/Applications/Photo Sorter.app"

echo ""
echo "✅ Installation complete!"
echo ""
echo "🎯 To run Photo Sorter:"
echo "   1. Open Finder"
echo "   2. Go to Applications folder"
echo "   3. Double-click 'Photo Sorter'"
echo ""
echo "   Or use Spotlight:"
echo "   Press ⌘ + Space, type 'Photo Sorter', press Enter"
echo ""
echo "🛠️  To uninstall:"
echo "   Run 'Uninstall Photo Sorter.command' or manually delete:"
echo "   /Applications/Photo Sorter.app"
echo ""

# Open Applications folder
read -p "Open Applications folder now? (y/n): " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
    open "/Applications"
fi

# Keep terminal open
echo "Press any key to close..."
read -n 1 -s