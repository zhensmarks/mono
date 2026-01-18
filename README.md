# BMachine v2

BMachine is a comprehensive automation dashboard designed to streamline managing creative workflows, including Trello card management, AI-powered image processing (Pixelcut), and Google Drive synchronization.

## Features

### 1. Dashboard Overview
- **Real-time Stats**: View Editing, Revision, Late, and Points metrics at a glance.
- **Floating Widget**: A mini-overlay for quick access to key stats and controls.
- **Activity Log**: Integrated logging panel to track background processes and errors.

### 2. Trello Integration
- **Kanban Lists**: Manage "Editing", "Revision", and "Late" cards directly from the app.
- **Smart Actions**:
  - **Batch Move**: Quickly move multiple cards between lists.
  - **Unlink**: Remove cards from the "Manual" list with a single click.
  - **Color Coding**: Visual cues for card states (e.g., Red for revisions).

### 3. Pixelcut (AI Image Processing)
- **Background Removal**: Drag-and-drop images to automatically remove backgrounds correctly.
- **Upscaling**: Enhance image resolution using AI.
- **Smart Queue**: Manages concurrent processing with auto-retry for failed items.
- **Compact View**: A dedicated, space-saving view for file processing.

### 4. Google Drive Sync
- **Seamless Uploads**: Drag-and-drop files to upload directly to specific Month/Date folders.
- **Multi-Selection**: Support for Shift+Click range selection and standard multi-select.
- **Status Tracking**: Real-time progress bars for uploads.

## Tech Stack
- **Framework**: Avalonia UI (.NET 8)
- **Language**: C#
- **Pattern**: MVVM (CommunityToolkit.Mvvm)
- **Backend**: Python scripts for core AI processing (Pixelcut).

## Getting Started

### Prerequisites
- .NET 8 SDK
- Python 3.10+ (for AI scripts)

### Installation
1. Clone the repository.
2. Restore dependencies:
   ```bash
   dotnet restore
   ```
3. Run the application:
   ```bash
   dotnet run --project src/BMachine.App
   ```

## Configuration
- **Settings**: Accessible via the header (Blue Gear Icon).
- **Theme**: Supports Dark/Light modes and custom accent colors.
