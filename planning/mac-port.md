# The macOS port — what it will actually take

**Status:** written 2026-09-07, when the Mac release was deliberately deferred by a few weeks.
**Purpose:** so that in a few weeks this is a checklist rather than a discovery exercise. Every
claim below was re-derived against the tree on 2026-09-07 — the older memory note `mac-todo`
(2026-08-21) predates the harness panel, the browser fetch, the DPAPI credential store and the MCP
bridge, and its numbers are stale.

Nothing here has been run on a Mac. What *has* been done is to push every question as far as it can
go from a Windows box, which turned two unknowns into facts and one into a named blocker.

---

## 0. The headline

**The build has almost certainly never been attempted on macOS, and it will not succeed as it
stands.** That is not a code-quality problem — `Physalia.Core` is completely clean (see §3) — it is
a *reference* problem, and it is concentrated in one place: how the project acquires Grasshopper.

Do §1 before estimating anything else. Until the non-Windows TFM restores and compiles, every other
item is speculation about a build nobody has seen.

---

## 1. Blocker: the non-Windows TFM does not restore, and Grasshopper comes from a Windows package

The csproj already multi-targets by OS —
`net7.0-windows` on Windows, plain `net7.0` elsewhere (`Physalia.GH.csproj:11-12`) — and already
carries Mac `HintPath` ItemGroups for **Eto**, **Rhino.Runtime.Code**, **RhinoCodePlatform.GH** and
**RhinoCodePlatform.GH1**, pointing into the Rhino 8 app bundle. So the intent was there. Two things
were never finished.

### 1a. `System.Drawing.Common` downgrade fails the restore — **verified from Windows**

Forcing the non-Windows TFM (`dotnet build -p:TargetFrameworks=net7.0`) fails at restore:

```
error NU1605: Detected package downgrade: System.Drawing.Common from 8.0.11 to 7.0.0
  Physalia.GH -> GhJSON.Grasshopper 1.1.1 -> System.Drawing.Common (>= 8.0.11)
  Physalia.GH -> System.Drawing.Common (>= 7.0.0)
```

It does **not** happen on `net7.0-windows`, because that TFM gets `System.Drawing` from the Windows
Desktop framework rather than resolving the package the same way. The pin to 7.0.0 exists for the
net7.0 target and CA1416; raising it to 8.0.11 for the non-Windows TFM only is the obvious first
move, but confirm it does not change what ILRepack merges.

**This is the first thing a Mac build hits, and it costs an afternoon to diagnose in the dark. It
took ten minutes from Windows.**

### 1b. The `Grasshopper` NuGet package is the WINDOWS build — **verified by reflection**

`~/.nuget/packages/grasshopper/8.24.25281.15001/lib/net7.0/Grasshopper.dll` — the only non-net48
asset in the package, so the one both TFMs use — references:

| | |
|---|---|
| `System.Windows.Forms 4.0.0.0` | the menu API (`AppendAdditionalMenuItems(ToolStripDropDown)`) |
| `RhinoWindows` | Windows-only Rhino UI |
| `Eto.Wpf` | the WPF Eto platform |
| `PresentationCore`, `PresentationFramework`, `WindowsFormsIntegration` | WPF |

So on macOS the project would be compiling against Windows Grasshopper. The package also ships no
Mac-specific lib folder (`net48` and `net7.0` only), and `RhinoCommon` in this graph has **only
`net48`**.

**The fix is the pattern the csproj already uses for Eto**: on the non-Windows TFM, drop the
`Grasshopper` `PackageReference` and add `HintPath` references to `Grasshopper.dll`, `GH_IO.dll` and
`RhinoCommon.dll` from inside `Rhino 8.app`, beside the four assemblies that are already referenced
that way. Paths will need confirming on a real install — the existing Mac ItemGroup gives the shape:

```
/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework/Versions/Current/Resources/
    ManagedPlugIns/RhinoCommon.framework/Versions/Current/<name>.dll
```

Grasshopper itself lives under `ManagedPlugIns/GrasshopperPlugin.framework/...` on Mac — verify.

### 1c. Then answer the one question that sizes the rest of the job

**Does the Mac reference set give us a compilable `System.Windows.Forms`?** 27 files import it
unguarded (§2), and **25 of those touch nothing but `ToolStripDropDown` / `ToolStripMenuItem` /
`ContextMenuStrip`** — i.e. Grasshopper's own right-click menu API, which we are obliged to override
because that is the signature `IGH_DocumentObject` declares.

- **If yes** (Mac Rhino ships a WinForms shim that satisfies Grasshopper's own reference): those 25
  files need no source change at all, and the port is small — §2's short list plus §4.
- **If no**: all 25 need a menu abstraction, and the port is a week rather than a day.

There is no way to answer this from Windows; it falls out of §1b the moment the non-Windows TFM
compiles. **Answer it before promising a date.**

---

## 2. The WinForms surface, classified by what it actually uses

29 files import `System.Windows.Forms`. Two are already inside `#if WINDOWS` and can be ignored
here. The rest split cleanly, and the split is the whole estimate:

### Menus only — 25 files, no source change if §1c says yes

`ArrowAttributeBase`, `HarnessAttrib`, `PickerAttrib`, `BudgetGuard`, `SignalSwitch`, `Picker`,
`ClusterGrounder`, `ComponentCatalogGrounder`, `ImageSources`, `ScriptIO`, `RuntimeHealthCheck`,
`SnapshotToolComponentBase`, `HarnessOut`, `ApiCall`, `LlmToolComponentBase`, `McpServer`,
`ConversationLog`, `ProjectFolderMenu`, `StatefulComponentBase`, `ContentPruner`,
`ComponentTransmitter`, `ScriptTransmitterBase`, `TransmitterLink`, `SignalSourceBase`,
`HarnessComponent`.

CLAUDE.md records that `ContextMenuStrip` works on the GH canvas where `Eto.ContextMenu` does not —
but that is a *Windows* observation and none of it has been exercised on Mac. The three custom
attributes are the ones to try first, since they own mouse capture and drag.

### Real Windows UI — 4 items, each needing a decision

| Where | What | Note |
|---|---|---|
| **`Widgets/HarnessPanel.cs`** | a genuine top-level `Form` — `TextBox`, `Button`, `MessageBox`, `Screen` | **the single biggest item.** And it cannot simply become a canvas widget again: it is a Form *because* `GH_Canvas` derives from `Control`, not `ContainerControl`, so a child cannot hold keyboard focus and typing went to the Rhino command line. The Mac answer is an `Eto.Forms.Form` — which is also what the chat window already is, so the pattern exists in-repo. |
| `Widgets/HarnessPanelHost.cs` | positions the panel against the canvas | follows the panel |
| `HarnessComponent.cs` | `OpenFileDialog` (Load Harness from .gh) | one-line swap to `Eto.Forms.OpenFileDialog` |
| `ImageSources.cs` | `Clipboard` (paste an image) | `Eto.Forms.Clipboard` |
| `PickerAttrib.cs` | `Cursor` | cosmetic |

---

## 3. Already Mac-safe — do NOT spend time re-auditing these

Checked 2026-09-07:

- **`Physalia.Core` contains no `System.Windows.*` reference of any kind.** The boundary rule held,
  which is the reason this port is a UI job and not a rewrite: every provider, signal, compaction,
  packaging, MCP and recording path is portable as written.
- **DPAPI is runtime-guarded, not compiled in.** `SecretStores.For` branches on
  `OperatingSystem.IsWindows()` and hands back `FileSecretStore` (plaintext, owner-only mode)
  elsewhere. The crypt32 P/Invoke in `WindowsDataProtection` is never reached. **Do not "fix" this by
  weakening Windows encryption to match platforms** — the proper Mac answer is one new
  `ISecretStore` over the Keychain plus one line in `SecretStores.For`, and nothing above that seam
  changes.
- **`McpExecutable.Resolve`** guards its PATHEXT handling behind `IsWindows`.
- **`PdfNativeLibrary`** already branches win/osx/linux and handles PDFium filing macOS under
  `osx-arm64`/`osx-x64` while SkiaSharp ships one universal binary under a bare `osx`. The
  `runtimes/` tree in `bin` carries osx-x64, osx-arm64 and linux natives today — verified present.
- **`CopyMcpBridge`** globs `**\*`, so it stages whatever the apphost is called.
- `LocalApplicationData` maps to `~/.local/share`; `UseShellExecute = true` opens a browser via
  `open`.
- **`McpServer.BridgeExecutable()` — fixed 2026-09-07.** It hardcoded `Physalia.McpBridge.exe`, so on
  macOS it returned null and *every remote MCP server* reported the bridge missing while local stdio
  servers kept working — a platform fault disguised as a server-specific one. It now probes both
  apphost spellings. Note the consequence that remains: **a package built on Windows carries no
  macOS apphost**, so the Mac build must produce its own.

---

## 4. Absent-by-design on Mac: the five `#if WINDOWS` files

These compile away rather than breaking, so the plug-in loads and the features are simply missing:

| File | What is lost | Port route |
|---|---|---|
| `Panels/ChatWindow.cs` | **only ~150 of 4295 lines are guarded** — the window is already Eto. The islands are WebView2 specifics and a `user32` P/Invoke for focus | the big one is `CoreWebView2.DownloadStarting`, which gives a settable `ResultFilePath` and has no WKWebView equivalent; CLAUDE.md already records that the fetch then lands in the browser's own folder |
| `Panels/BrowserFetch.cs` | the in-window Cloudflare fetch redirect | as above |
| `Widgets/ChatWidget.cs` | the canvas widget that opens the window | partially guarded |
| `Components/Extra/Serializer.cs` | interactive .ghjson export (`SaveFileDialog` + canvas `KeyDown`) | `Eto.Forms.SaveFileDialog` + an Eto key hook; the HUD overlay and the GhJSON calls are already portable |
| `Widgets/SerializeWidget.cs` | the widget driving the above | follows |

`Components/Extra/Deserializer.cs` is unguarded and cross-platform already.

---

## 5. Suggested order

1. **§1a** raise `System.Drawing.Common` for the non-Windows TFM. Minutes.
2. **§1b** repoint Grasshopper/GH_IO/RhinoCommon at the Mac app bundle. The real work of step one.
3. Get `dotnet build` to *compile* on macOS. **Stop and re-estimate here** — §1c is answered by
   whatever the compiler says about the 25 menu files.
4. **§2** `HarnessPanel` → Eto. Largest single item; the chat window is the precedent.
5. The three small swaps: `OpenFileDialog`, `Clipboard`, `Cursor`.
6. Load in Mac Rhino and walk the pre-ship passes. Expect the custom attributes (drag, mouse
   capture) to be where the surprises are.
7. Decide whether the Keychain `ISecretStore` and the WebView2-dependent features ship in the first
   Mac release or are declared absent.

## 6. What to say in the meantime

The Windows release can state plainly that macOS is not yet supported. Two features would be
**absent rather than broken** on Mac even after the build works — the in-window browser fetch and
the interactive .ghjson export — and one would be **weaker**: credentials fall back to a
plaintext-but-owner-only file until the Keychain store exists. That last one is worth saying out
loud in release notes rather than leaving for someone to discover.
