# Mermaider - Mermaid Diagram Editor

A local Mermaid diagram editor built with C# Avalonia, featuring code editing, syntax highlighting, real-time preview with zoom/pan, syntax validation, image export and copy, and an AI assistant for generating diagrams via natural language. All rendering is done locally — no data uploaded.

## Features

### Code Editing
- **Syntax Highlighting** - Mermaid syntax highlighting with line numbers
- **Multi-tab** - Edit multiple files independently with automatic tab switching
- **Debounced Rendering** - Auto-renders after 350ms of inactivity to avoid excessive refreshes

### Real-time Preview
- **JavaScript Injection Updates** - Initial load via WebView file navigation; subsequent updates use `ExecuteScriptAsync` to inject JavaScript directly, bypassing file caching and navigation issues
- **Global renderDiagram Function** - The `renderDiagram(code)` function is exposed globally for zero-latency script injection updates
- **Drag to Pan** - Hold left mouse button and drag to move the diagram
- **Scroll to Zoom** - Zoom from 0.2x to 6x via mouse wheel
- **Double-click to Fit** - Auto-fit diagram to viewport on double-click
- **Editor Toggle** - Hide/show the editor for full-screen preview

### Image Operations
- **Save Image** - Export high-resolution PNG images (3x scale)
- **Copy Image** - Copy diagram to system clipboard

### File Operations
- Create / Open / Save Mermaid files (.mmd / .mermaid)
- Command-line argument support for opening files
- Recent files history (up to 10 files)
- Save confirmation dialog when closing unsaved tabs
- Unsaved change detection on application exit

### AI Assistant
- **Natural Language Generation** - Describe your diagram, AI generates Mermaid code automatically
- **Multi-model Support** - OpenAI, Azure OpenAI, Ollama, Custom API
- **One-click Apply** - Apply AI-generated code to the editor instantly, with revert support
- **Conversation History** - Persisted to disk, supports continuous interaction
- **Configurable Parameters** - Temperature, MaxTokens, and other settings
- **Selectable Messages** - Chat message text (including code) is selectable and copyable
- **Multi-line Input** - Input box supports multi-line text; Shift+Enter for newline, Enter to send
- **Draggable Splitter** - Adjustable height splitter between chat history and input area

### UI Features
- **Draggable Splitter** - Editor/preview ratio adjustable via drag
- **Built-in Toggle Button** - Click the button in the splitter bar to hide/show the editor
- **Fluent Theme** - Modern UI style
- **Auto-save Layout** - Editor ratio, zoom level, AI panel state, and other settings auto-saved

### Technical Details
- Preview uses `CoreWebView2.ExecuteScriptAsync` for JavaScript injection, bypassing file:// URL caching and navigation restrictions
- AI configuration Base URL is automatically sanitized (removes trailing `/chat/completions`)
- Preview files are cleaned up by creation time (only keep last 7 days)

## Tech Stack

- **Language**: C# (.NET 10)
- **UI Framework**: Avalonia UI 11.3
- **Architecture**: MVVM (CommunityToolkit.Mvvm)
- **Code Editor**: AvaloniaEdit
- **Preview Rendering**: Mermaid.js (real-time via WebView)
- **Image Export**: Mermaid CLI 11.12.0 (embedded, for high-res image export)
- **WebView**: WebView.Avalonia

## Requirements

### Development Environment
- .NET 10 SDK
- Avalonia templates

### Running Packaged Version
- Windows (requires WebView2 runtime, pre-installed on Windows 10/11)
- No Node.js or other dependencies required — self-contained, just run the executable

## Building

### Development Mode

```bash
dotnet restore
dotnet run
```

Open file via command line:

```bash
dotnet run -- example.mmd
```

### Publish Self-Contained Version

Use the included publish script:

```powershell
.\publish.ps1
```

Or manually:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

> Note: Mermaid CLI tools are embedded as resources — no need to manually copy the `tools` directory.

## Usage

### Opening Files

- Menu: File → Open (Ctrl+O)
- Command line: `Mermaider.exe example.mmd`
- Recent files: File → Recent Files

Supports .mmd and .mermaid extensions.

### Editing Code

Enter or modify Mermaid code in the left editor panel. The right preview area updates automatically.

### Saving Files

- Save: File → Save (Ctrl+S)
- Save As: File → Save As (Ctrl+Shift+S)

### Exporting Images

- Click the "Save" button in the preview area to save image file
- Click the "Copy" button in the preview area to copy image to clipboard

### Preview Controls

- **Pan**: Hold left mouse button and drag in the preview area
- **Zoom**: Scroll mouse wheel in the preview area (up to zoom in, down to zoom out)
- **Fit to Viewport**: Double-click the preview area
- **Toggle Editor**: Click the toggle button in the splitter bar between editor and preview

### AI Assistant

1. Click the "AI Assistant" button at the bottom to expand the panel
2. Describe the diagram you want in the input box (e.g., "Draw a user login flowchart"); Shift+Enter for newline, Enter to send
3. AI will generate the corresponding Mermaid code
4. Click "Apply Code" to insert the generated code into the editor; "Revert" to undo
5. Click the settings icon to configure AI model parameters
6. Chat messages are selectable and copyable; the input area height is adjustable via the drag splitter

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New File |
| Ctrl+O | Open File |
| Ctrl+S | Save File |
| Ctrl+Shift+S | Save As |
| Ctrl+W | Close Current Tab |
| Ctrl+Q | Exit |
| Ctrl+Z | Undo |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+X | Cut |
| Ctrl+C | Copy |
| Ctrl+V | Paste |
| Ctrl+A | Select All |

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) file for details.

## Acknowledgments

This project uses the following open-source projects:

- [Avalonia UI](https://avaloniaui.net/) - Cross-platform UI framework
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) - Code editor control
- [Mermaid.js](https://mermaid.js.org/) - Mermaid diagram rendering engine (preview)
- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli) - Mermaid diagram rendering engine (export)
- [WebView.Avalonia](https://github.com/AvaloniaUI/AvaloniaWebView) - WebView control
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) - MVVM toolkit
