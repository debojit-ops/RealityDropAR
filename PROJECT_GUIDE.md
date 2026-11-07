# RealityDrop AR — Project Guide

> **Read this first.** This is the guide for the project, and anyone working on it (human or AI) should follow it.
> When you finish a task, tick its box in **§6 Progress Tracker** and add a line to **§8 Change Log**.
> Last full review: 2026-11-07

---

## 1. End Goal (what we are building)

A mobile AR app (Android + iOS) that works like this:

1. **Home / Search**: the user opens the app and types what they want to see (e.g. "sofa", "dragon").
2. **Results grid**: matching **downloadable** models from Sketchfab appear in a **2-column scroll view** (2x2 cards visible per screen). More results load as the user scrolls.
3. **Select**: the user taps a card. The model downloads, with a visible progress indicator.
4. **Preview**: the model appears in a 3D preview screen, where the user can rotate, zoom and reset it.
5. **AR**: the user taps "View in AR", points the camera at a surface and places the model. They can then **move** it (drag), **scale** it (pinch) and **rotate** it (twist), so they can explore it.
6. **Snapshot**: in AR, the user taps a camera button. The app captures the AR view with the UI hidden and **saves it to the phone's gallery/photos**, then shows a confirmation.

Done means: this whole flow runs smoothly on a mid-range phone with no crashes, no noticeable drift, and a clean, finished UI.

---

## 2. Tech Stack

| Item | Value |
|---|---|
| Engine | Unity **6000.2.6f2** (Unity 6) |
| Render pipeline | URP 17.2 (`Assets/Settings/Mobile_RPAsset.asset`) |
| AR | AR Foundation 6.2 + ARCore 6.2 + ARKit 6.2 |
| Model loading | glTFast 6.13 (`com.unity.cloud.gltfast`), runtime `.glb` |
| Networking/JSON | `UnityWebRequest` + Newtonsoft JSON |
| Zip extraction | SharpZipLib (Sketchfab downloads are zipped) |
| UI | uGUI + TextMeshPro |
| Input | Legacy `Input` touch API, used in scripts (Input System package is installed too) |
| Content source | Sketchfab Data API v3 (`/v3/search`, `/v3/models/{uid}/download`) |

---

## 3. Scene Flow

```
SampleScene (Search + results)  ──tap card──►  PreviewScene_new (3D preview)  ──View in AR──►  ARScene (place / explore / snapshot)
        ▲                                              ▲                                              │
        └──────────── back ────────────────────────────┴──────────────── back ────────────────────────┘
```

| Scene | In build? | Purpose |
|---|---|---|
| `Assets/Scenes/SampleScene.unity` | ✅ index 0 | **Current** search screen (results use a 1-column VerticalLayoutGroup) |
| `Assets/Scenes/UnderProgress.unity` | ❌ | **New UI in progress**: has the **2-column GridLayoutGroup** (cell 550x650) plus SearchUI/SketchfabAPIManager. Should replace SampleScene once finished. |
| `Assets/Scenes/PreviewScene_new.unity` | ✅ | 3D preview (orbit/zoom/auto-rotate) |
| `Assets/Scenes/ARScene.unity` | ✅ | AR placement + gestures |

Scene names are hard-coded as strings in: `SketchfabAPIManager.cs` (→ `PreviewScene_new`), `SceneSwitcher.cs` (→ `ARScene`), `ARUIManager.cs` (→ `PreviewScene_new`), `PreviewUIManager.cs`. **If you rename a scene, update all of them.**

---

## 4. Architecture: How Data Moves

1. `SearchUI` → `SketchfabAPIManager.SearchModels(query)` → GET `/v3/search?type=models&downloadable=true&count=24&q=…`
2. `SketchfabAPIManager.ParseResults` instantiates `Prefabs/ThumbnaiItem.prefab` cards into `resultsParent`, loads the ~512px thumbnail, and auto-loads `autoLoadPages` extra pages. `SearchUI` triggers `FetchNextPage()` when the user scrolls near the bottom.
3. Card tap → `FetchDownloadLink(uid)` → GET `/v3/models/{uid}/download` → `glb.url`
4. `ModelDownloader.DownloadAndExtractGLB` downloads the zip and extracts it to `persistentDataPath/Models/`. The resulting path is saved in `PlayerPrefs["LastModelPath"]`, then `PreviewScene_new` loads.
5. `ModelPreviewManager` loads the `.glb` via `GltfLoader.Load` (glTFast), runs `SanitizeMaterials`, normalizes the model and frames it. `PreviewControls` handles orbit and zoom.
6. "View in AR" → `PreviewUIManager` stores the loaded `GltfImport` in the `ARModelBridge` singleton (`DontDestroyOnLoad`), then `ARScene` loads.
7. `ARSceneInitializer` instantiates the model from the bridge. If that fails, it falls back to loading from `LastModelPath` via `ModelLoader`. The model starts hidden.
8. `ModelPlacement` runs a state machine (`Scanning → SurfaceTargeted → Placed`), shows a reticle, places the model on tap or Spawn, and attaches an **ARAnchor** to reduce drift. Gestures: 1-finger drag (re-anchors onto the plane), pinch to scale (clamped 0.05–5), twist to rotate.
9. `ARUIManager` wires the Back / Spawn / Reset buttons.

### Script map
| Script | Role |
|---|---|
| `Scripts/SketchfabAPIManager.cs` | Search, pagination, thumbnails, download-link fetch, scene switch |
| `Scripts/SearchUI.cs` | Search bar/button, endless-scroll trigger |
| `Scripts/Model/ModelDownloader.cs` | Download zip + extract `.glb` |
| `Scripts/Model/GltfLoader.cs` | glTFast import settings, load, material sanitizing |
| `Scripts/Model/ModelLoader.cs` | Loads a `.glb` from disk, caches the instance |
| `Scripts/Model/ModelPreviewManager.cs` | Preview-scene load, normalize, frame, auto-rotate |
| `Scripts/PreviewControls.cs` | Preview orbit/zoom input |
| `Scripts/PreviewUIManager.cs` | Preview buttons, hand-off to AR (file has large commented-out old versions on top) |
| `Scripts/ARModelBridge.cs` | Persists `GltfImport` across scene loads |
| `Scripts/ARSceneInitializer.cs` | Gets the model ready in ARScene |
| `Scripts/Model/ModelPlacement.cs` | Reticle, anchor placement, drag/pinch/twist |
| `Scripts/ARUIManager.cs` | AR scene buttons |
| `Scripts/AppPerformanceManager.cs` | 60 fps target, vsync off, never-sleep |
| `Scripts/ARDebugTool.cs`, `Model/ModelMaterialDebugger.cs` | Debug only |
| `Scripts/Editor/GltfShaderAutoAssigner.cs`, `Model/GltfShaderPreloader.cs` | Make sure glTF shaders are included in builds |
| `Scripts/Thumbnail.cs`, `Scripts/SelectedModel.cs`, `SearchUI.OnThumbnailClicked` | **Legacy / unused path**: the cards are wired directly by `SketchfabAPIManager` |

---

## 5. Rules for Working on This Project

- **Mobile memory is the main constraint.** Destroy textures you no longer need (see `ClearResults`), and don't load full-size thumbnails or unlimited pages (`loadInfiniteNoLimit` must stay `false`).
- **Keep the existing load path:** download → `persistentDataPath/Models` → `LastModelPath` → bridge/fallback. Don't add a second way to load models.
- **AR objects must sit under an `ARAnchor`**, to prevent drift.
- **Shaders:** any new glTF material/shader must also be covered by `GltfShaderAutoAssigner`/`GltfShaderPreloader`, or models render pink in builds.
- Filter out touches that are over UI (`EventSystem.IsPointerOverGameObject(fingerId)`) before handling AR gestures.
- Use the `[ClassName]` prefix on log messages (existing convention).
- Don't commit API tokens (see the open task).
- Test on a real device. AR does not run in the Editor without XR Simulation.

---

## 6. Progress Tracker

**Overall estimate: ~65% done.** The core pipeline works from start to finish. Still missing: the snapshot feature, the 2x2 UI going live, stability and drift fixes, and polish.

### A. Search & Results: ~80%
- [x] Sketchfab search (downloadable models only)
- [x] Thumbnail cards with title + ~512px image
- [x] Pagination / endless scroll + auto-load of extra pages
- [x] Texture cleanup on new search (fixes out-of-memory crashes)
- [x] **2-column (2x2) grid**: `ResultsGridLayout.cs`, added automatically to `resultsParent` by `SketchfabAPIManager.Awake`. It replaces the old VerticalLayoutGroup (negative spacing, fixed 4504 height, 3.86 scale caused overlapping cards) and sizes cells from the viewport width. The card prefab's root AspectRatioFitter is disabled at spawn.
- [ ] Move the finished `UnderProgress` UI into the build as the main scene (or port its grid into SampleScene)
- [x] Only free, downloadable models shown (`downloadable=true` query + per-result `isDownloadable` check in `IsFreeDownloadable`)
- [x] "No results" / network-error messages on screen (`ShowMessage`: uses `statusText` if assigned, else an auto-created toast)
- [x] Loading placeholders while searching (6 pulsing skeleton cards, `skeletonCount`)
- [x] **Premium iOS-style cards** (`ResultCardView.cs`): dark glass, rounded corners + thin cyan border, soft shadow that becomes a cyan glow on press/download, inset rounded thumbnail (cover-cropped, fades in), "FREE" pill, bold title + grey "by author", staggered pop-in, press spring. Everything scales with card width.
- [x] Rounded UI system: `UIRoundedCorners.cs` + `Assets/Resources/RealityDropUI/RoundedRect.shader` (anti-aliased SDF, per-corner radius, border, soft glow; one shared material). Kept in Resources so it ships in builds. Use it for any future rounded UI.
- [x] iOS scrolling: hidden scrollbar (space given back to the grid), elastic bounce, momentum
- [x] Toast restyled as a rounded glass pill
- [x] Fixed: only 24 results ever loaded (auto-load/next-page was blocked by `isFetchingPage` still being true during parsing)
- [x] Stale results from an older search are discarded (`searchId`)
- [x] `SearchUI` finds the results ScrollRect automatically if its field is empty
- [x] Hard cap on cards per search: `maxTotalResults` (default 240)
- [ ] Recycle cards (pooling) if we ever need more than ~240 results
- [ ] Untick `loadInfiniteNoLimit` on `SketchfabAPIManager` in SampleScene + UnderProgress (currently ticked; the cap protects us but scroll-to-load is better)

### B. Download & Load: ~75%
- [x] Fetch GLB download link
- [x] Download zip + extract `.glb` to `persistentDataPath/Models`
- [x] glTFast loading + material sanitizing
- [~] **Download progress UI**: a "Downloading model..." message shows now; still no percent/spinner or cancel
- [x] Message on screen when a download fails (401/403/404, no GLB, network, unzip)
- [x] Extra taps ignored while a download is running
- [ ] Skip re-downloading if the model is already cached; clear out old models to free disk space
- [ ] Warn about or reject very heavy models (high poly count / file size) before loading

### C. Preview Screen: ~80%
- [x] Load, normalize and frame the model
- [x] Orbit (drag), zoom (pinch/scroll), reset, auto-rotate
- [x] "View in AR" hand-off through `ARModelBridge`
- [ ] Back to search keeps the previous results (they currently reload from scratch)
- [ ] Remove the commented-out blocks from `PreviewUIManager.cs`

### D. AR Experience: ~70%
- [x] Plane detection + reticle
- [x] Place by tap or Spawn button, with fallbacks
- [x] ARAnchor attachment (anti-drift)
- [x] Move (drag), scale (pinch), rotate (twist)
- [x] Reset placement
- [~] **Drift**: better but not solved (per commit history). Check anchor and tracking quality, and consider re-anchoring only when a drag ends instead of every frame.
- [ ] Onboarding hint ("move your phone to scan the floor")
- [ ] Shadow under the model for realism (optional)
- [ ] Hide or fade the plane visuals after placement

### E. Snapshot: 0% ❗
- [ ] Camera/shutter button in the AR UI
- [ ] Hide UI (and reticle/planes) → capture with `ScreenCapture.CaptureScreenshotAsTexture()` at end of frame → restore UI
- [ ] Save to gallery: Android → Pictures/RealityDrop, iOS → Photos. Recommended plugin: **NativeGallery** (yasirkula). Needs the Android storage/media permission on older APIs and `NSPhotoLibraryAddUsageDescription` on iOS.
- [ ] Flash effect + "Saved to gallery" toast, plus a thumbnail preview with a Share option (optional, NativeShare)

### F. Stability, Security & Polish: ~30%
- [x] 60 fps target (`AppPerformanceManager`)
- [ ] **Move the hard-coded Sketchfab token** (`SketchfabAPIManager.DefaultToken`) out of source control; it's already committed, so **revoke it and issue a new one**
- [ ] Profile memory on a real device for big models (earlier commits mention lag and crashes)
- [ ] Apply the new UI art (`Assets/GUI Assets`, `Assets/Icons/Future Plan UI/RealityDrop UI Plan.png`) to all 3 scenes
- [ ] Remove dead code (`Thumbnail.cs`, `SelectedModel.cs`, `SearchUI.OnThumbnailClicked`, empty `Scripts/Copies`)
- [ ] App icon, splash screen, product name, bundle ID
- [ ] Test full builds on Android and iOS

---

## 7. Recommended Next Steps (in order)

1. **Snapshot feature (E)**: the biggest missing piece of the end goal.
2. **Make the 2x2 grid live (A)**: finish `UnderProgress.unity` and add it to the build as scene 0.
3. **Download progress + error UI (B)**: the user currently waits with no feedback.
4. **Drift fix (D)**.
5. **Token security (F)**.
6. UI art polish, dead-code cleanup, device testing.

---

## 8. Change Log
Add newest entries at the top: `YYYY-MM-DD — what changed — files`.

- 2026-11-07 — Premium iOS-style results: rounded glass cards, glow, skeletons, animations, author line, FREE badge, hidden scrollbar, pill toast — `ResultCardView.cs`, `UIRoundedCorners.cs` (new), `Resources/RealityDropUI/RoundedRect.shader` (new), `ResultsGridLayout.cs`, `SketchfabAPIManager.cs`
- 2026-11-07 — Card layout: thumbnail fills top (cover-cropped via uvRect), title scales with card width, max 2 lines — `ResultCardView.cs` (new), `SketchfabAPIManager.cs`, `ResultsGridLayout.cs`
- 2026-11-07 — Fixed overlapping result cards: responsive 2-column grid — `ResultsGridLayout.cs` (new), `SketchfabAPIManager.cs`
- 2026-11-07 — Fixed search returning only 24 results; stale-search guard; result cap; ScrollRect fallback — `SketchfabAPIManager.cs`, `SearchUI.cs`
- 2026-11-07 — Free-downloadable-only safety filter, on-screen search/download error messages, double-tap guard — `SketchfabAPIManager.cs`
- 2026-11-07 — Created this guide after a full project review — `PROJECT_GUIDE.md`, `CLAUDE.md`
