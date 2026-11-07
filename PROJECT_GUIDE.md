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
| `Assets/Scenes/SampleScene.unity` | ✅ index 0 | **Current** search screen (2-column grid via `ResultsGridLayout`) |
| `Assets/Scenes/UnderProgress.unity` | ❌ | Older UI experiment |
| `Assets/Scenes/PreviewScene_new.unity` | ✅ | 3D preview (design A Neon Showroom) |
| `Assets/Scenes/ARScene.unity` | ✅ | AR placement + gestures |

Scene names are hard-coded as strings in: `SketchfabAPIManager.cs` (→ `PreviewScene_new`), `ARScreenUI.cs`/`ARUIManager.cs` (→ `PreviewScene_new`), `PreviewScreenUI.cs`/`PreviewUIManager.cs` (→ `SampleScene`, `ARScene`). **If you rename a scene, update all of them.**

**Design concepts canvas** (home + preview concepts): https://claude.ai/artifact/JFGPS56K7DHe1zSUBQn3pr

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
- [x] **Premium search row** (`SearchBarStyler.cs`, applied from `SearchUI.Start`): glass pill field with cyan border, drawn magnifier icon, "Search 3D models" placeholder, cyan caret/selection, clear (×) button, cyan glow while typing; cyan gradient pill button (`UIGradient.cs`) with breathing halo + press spring (`UIPressRelay.cs`). Row is aligned to the card edges.
- [x] Results area widened to fill the frame: `ResultsScrollView` width 1740 in SampleScene + UnderProgress
- [x] Fixed: only 24 results ever loaded (auto-load/next-page was blocked by `isFetchingPage` still being true during parsing)
- [x] Stale results from an older search are discarded (`searchId`)
- [x] `SearchUI` finds the results ScrollRect automatically if its field is empty
- [x] Hard cap on cards per search: `maxTotalResults` (default 240)
- [ ] Recycle cards (pooling) if we ever need more than ~240 results
- [ ] Untick `loadInfiniteNoLimit` on `SketchfabAPIManager` in SampleScene + UnderProgress (currently ticked; the cap protects us but scroll-to-load is better)

- [x] **Recommended for you slideshow** (`RecommendationsPanel.cs`, added by `SearchUI.Start`; shown while no results are visible): one big model card at a time (real thumbnail, caption pill, title, model · author, VIEW), swipe or auto-advance every 4.5 s, page dots; tap → download screen → preview (`SketchfabAPIManager.OpenModel`). Content: "Because you searched …" (most-liked free model for the last 2 searches, stored in PlayerPrefs `RecentSearches`) + "Anime pick" (Naruto, Gojo, Itachi always + 4 rotating from `AnimeCharacters`). Uses `sort_by=-likeCount`, falls back to default order.
  - Tap a card → searches that name and shows **all** its models (e.g. every "gojo satoru" model) in the results grid; the **VIEW** button opens that exact model.
  - ‹ › arrow buttons on the slideshow (wrap around). While search results are showing, a "‹ Recommended" pill sits above the grid (grid gets `ResultsGridLayout.ExtraTopPadding`); it — or the Android back button — clears the search (`SketchfabAPIManager.ResetSearch`) and brings the slideshow back.
- [x] **Content safety** (`ContentFilter.cs`): every model shown (search results + slideshow) must pass — Sketchfab `isAgeRestricted` must be false and the name/tags must not contain adult/suggestive keywords (whole-word list in `ContentFilter.BlockedWords`). Slideshow no longer sorts by likes (that surfaced fan-service). Slide title = real model name; caption = reason (e.g. "ANIME PICK · NARUTO UZUMAKI").
- [ ] Consider an extra safety layer before store release (e.g. only staff-picked models in recommendations, or a report button)

### A2. Visual quality & typography
- [x] Fonts: **Inter** (body: Regular/SemiBold/Bold) + **Rajdhani** (accents: button, %, badge) in `Assets/Resources/RealityDropUI/Fonts` (OFL licenses included). `UIFonts.cs` builds high-quality TMP assets at runtime (90pt sampling, 2048 atlas, SDFAA). Use `UIFonts.Apply(text, UIFonts.SemiBold)` for any new text.
- [x] UI textures (`bg.png`, `Bg/Frame2.png`, `Bg/bg.jpg`): uncompressed, max 4096, no mipmaps
- [x] URP `Mobile_RPAsset` Render Scale 0.8 → 1.0 (sharper camera feed / 3D models)
- [x] Download screen uses ~1024px thumbnail; list keeps ~512px
- [x] Safe area: `SafeAreaFitter.cs` scales the main screen into `Screen.safeArea` (Android "render outside safe area" is on). Applied from `SearchUI.Awake`. Preview/AR scenes don't have it yet.
- [ ] **`bg.png` is only 853×1844** (contains the neon frames + logo) — needs a 1080×2340 (or 1440×3120) re-export, or frames redrawn in code + separate hi-res logo
- [ ] Apply `UIFonts` + safe area to PreviewScene_new and ARScene UI

### B. Download & Load: ~75%
- [x] Fetch GLB download link
- [x] Download zip + extract `.glb` to `persistentDataPath/Models`
- [x] glTFast loading + material sanitizing
- [x] **Download progress screen** (`DownloadOverlay.cs`): dimmed background, glass panel with neon border, model thumbnail/title/author, neon progress bar with glow, live %, "x MB of y MB", steps (Preparing → Downloading → Unpacking → Opening), Cancel button. `ModelDownloader` now reports progress/stage and supports cancel; preview scene loads async behind the overlay.
- [x] Message on screen when a download fails (401/403/404, no GLB, network, unzip)
- [x] Extra taps ignored while a download is running
- [ ] Skip re-downloading if the model is already cached; clear out old models to free disk space
- [ ] Warn about or reject very heavy models (high poly count / file size) before loading

### C. Preview Screen: ~80%
- [x] Load, normalize and frame the model
- [x] Orbit (drag), zoom (pinch/scroll), reset, auto-rotate
- [x] "View in AR" hand-off through `ARModelBridge`
- [x] **New preview screen — design A "Neon Showroom"** (chosen from 3 concepts: https://claude.ai/artifact/JFGPS56K7DHe1zSUBQn3pr). `PreviewScreenUI.cs` builds it at runtime (added by `ModelPreviewManager.Awake`) and hides the old UI canvas: top bar (back · title/author · info card), FREE/Sketchfab/size badges, gesture hint, glass dock (Auto-spin · Reset · Scene · Light) + gradient VIEW IN AR, loading + failure states, safe area.
- [x] Orbit camera (`PreviewControls.cs` rewritten): model stays upright; drag orbits with inertia, pinch/wheel zoom clamped to model size, double-tap reset, auto-spin turntable, ignores touches on UI
- [x] `PreviewEnvironment.cs`: neon turntable + contact shadow (`Resources/RealityDropUI/Turntable.shader`), studio backdrop (`Backdrop.shader`), scenes Studio/Room/Outdoor (Resources `Skybox`, `Skybox 1`), light presets Soft/Bright/Dramatic
- [x] `UIIcons.cs`: crisp code-drawn line icons (back, info, spin, reset, scene, light, AR cube, close)
- [x] Model title/author/size passed from search via PlayerPrefs (`LastModelTitle`, `LastModelAuthor`, `LastModelSizeBytes`)
- [x] Back to search returns to the same feed: `SearchSession.cs` (static, in-memory) keeps query, results, next page and scroll position; `SketchfabAPIManager.Start` rebuilds it. Only ~48 thumbnails around the scroll position stay in memory while a model is open; others re-download.
- [ ] Remove the commented-out blocks from `PreviewUIManager.cs`

### D. AR Experience: ~70%
- [x] Plane detection + reticle
- [x] Place by tap or Spawn button, with fallbacks
- [x] ARAnchor attachment (anti-drift)
- [x] Move (drag), scale (pinch), rotate (twist)
- [x] Reset placement
- [x] **Drift root causes fixed (2026-11-07)**: ARScene had (1) a second Camera + ARCameraManager + ARCameraBackground on "Camera Offset" (static camera, same depth → model appeared to slide; double camera cost) and (2) a duplicate ModelPlacement on "XR Origin" (double placement, two anchors fighting). Both removed from the scene; `ModelPlacement.Awake` also guards against them at runtime.
- [x] **ModelPlacement rewritten** for stability: placement only after ~0.6 s of stable tracking, only on confirmed horizontal planes (PlaneWithinPolygon, HorizontalUp, ≥0.04 m²), never floating; ONE anchor (re-anchored only when a drag ends / tap-to-move; old anchor GameObject destroyed); model follows anchor through a 3 mm jitter dead-zone + smoothing; freezes while tracking is lost; drag projects onto the model's floor plane; tap elsewhere glides it; pinch (10%–1000%) + twist; model centered on its footprint; pop-in animation; contact shadow; smoothed neon reticle; plane visuals hidden after placement; AR session matches camera frame rate (60 fps restored when leaving)
- [x] **AR HUD** (`ARScreenUI.cs`, via `UIKit.cs`/`UIPress.cs`): back · title · re-place, coaching pill (preparing → move phone → tap to place → gestures; tracking lost), PLACE HERE button, size % chip; old AR canvas + debug buttons hidden
- [x] **AR placement flow (2026-11-07)**: 1) detected floors/tables glow as neon panels (`ARGlowPlane.cs` + `Resources/RealityDropUI/ARGlowPlane.shader`: constant-width neon edge, dot grid, scanning pulse; replaces the prefab's translucent visual; horizontal planes only) → 2) the preview-style glowing ring (Turntable shader) is the placement indicator, grey while settling, cyan when ready → 3) **PLACE MODEL** button places (taps no longer place) with a ring flare; panels fade out after placing and return on re-place.
- [x] **First device test (OnePlus CPH2717, Android 16): ARCore never tracked** — logcat: 10 s frame gap at AR start (model instantiation), feature extraction ~4 Hz, VIO kNotTracking, hit_test 'No point hit'. Fixes: `ARSession.Reset()` after the model is ready (`ARSceneInitializer.RestartTrackingAfterLoad`), 30 fps ~720p camera config (`ARCameraConfigurator.cs`), light estimation reduced to ambient intensity/color (HDR modes cost CPU). **Retest needed.**
- [ ] **Second device test: still no tracking.** 10 s startup stall gone, but ARCore keeps logging `FEATURE_DSP_CAM_IMU_DESYNC` (camera↔IMU clock offset), RANSAC inlier ratio ~1%, Mali `eglWaitSyncKHR failed`. Points to ARCore calibration/support for this phone (OnePlus CPH2717, Android 16), not placement code. Next: test Google 'View in your space' on the same phone; try Multithreaded Rendering off; try another ARCore-certified phone. AR screen now shows the not-tracking reason (`ARScreenUI.TrackingHint`).
- [x] **Lite mode fallback** (`ModelPlacement.EnterLiteMode`, `ARScreenUI`): if ARCore never tracks within 12 s after the model is ready (or the session is Unsupported), `TrackingFailed` → "USE LITE MODE" button. Lite mode disables the camera's TrackedPoseDriver, rotates the camera from the gyroscope (portrait), hides plane glow/ring, places the model on a virtual floor 1.5 m ahead / 1.3 m below the camera; drag, tap-to-move, pinch, twist and re-place work; no anchors, no walking around (no position tracking).
- [x] 8th Wall evaluated: hosted platform retired 2026-02-28 (hosted projects live until 2027-02-28); engine open-sourced (MIT, 8thwall.org) but it is WebAR (browser), not a Unity SDK → not used. Other Android SDKs (Lightship, Vuforia Ground Plane) still rely on ARCore tracking.
- [x] **Third device test: root cause confirmed** — ARCore logs `imu_transform_sensor: 400 0 0 0 0 0 0 1` (identity camera↔IMU calibration) → ARCore has **no calibration profile for OnePlus CPH2717**; no app-side fix possible. Gyro look-around Lite mode drifted (no translation tracking + gyro yaw drift) → replaced by drift-free **Camera mode** (camera fixed, model steady on screen over the live feed; `liteLookAround` optional, off). Button: USE CAMERA MODE.
- [x] **XR Simulation fix**: SampleScene still had a leftover AR Session + XR Origin AR components (started ARCore/the camera on the search screen; XR Simulation cached that scene's camera → 'Camera has been destroyed' in ARScene, no environment). Disabled in SampleScene (AR Session inactive; ARPlaneManager, ARRaycastManager, ARCameraManager, ARCameraBackground, TrackedPoseDriver off). XR Simulation: Desktop tab loader on, environments installed. `ARCameraConfigurator` skips unsupported providers.
- [x] **AR diagnostics** (`ARDiagnostics.cs`, added by ARSceneInitializer): Logcat tag `[ARDiag]` — header (device, OS, GPU/threading, XR loader, Google Play Services for AR version, ARCore `ArCoreApk.checkAvailability` verdict, camera configs), 1 s ticks (state, reason, camera fps, timestamp backwards/gaps, pose motion, planes, centre raycast, brightness, render fps), state changes, and a plain-words `DIAGNOSIS` after 15 s without tracking. Live panel on screen in Development builds.
- [x] **Fourth device test (with diagnostics): ARCore WORKS on the OnePlus** — `SUPPORTED_INSTALLED`, Services 1.57, SessionTracking at 3.8 s, planes at ~10 s. Earlier 'no calibration' conclusion was wrong; the real blocker was the leftover AR session in SampleScene. Remaining issue: ARCore processing only ~7 camera fps (slow plane detection/lag). Likely cause: our ARCameraConfigurator chose ~720p tracking images; now prefers ARCore's lightest 640×480 @30. Diagnostics tick now logs `cfg=`. **Retest.**
- [x] **Anchoring/plane improvements (market review: ARCore is the Android standard; Lightship/Vuforia run on top of ARCore; root cause = ARCore starved, Google 'VIO frequency low')**: `ARPerformanceProfile.cs` (AR scene only: URP render scale 0.8, MSAA/HDR/post off, restored on exit); diagnostics `trackHz` (pose update rate, flags STARVED < 15 Hz); quick placement in `ModelPlacement.TryFindPlacement`: confirmed plane → infinite extension of a found horizontal plane (≤ 6 m) → feature points below eye level (0.3–4 m), then `SnapToPlaneWhenFound` re-anchors onto the real plane (±0.3 m) once detected. Next options: ARCore Depth hit-tests (AROcclusionManager), Lightship meshing — only after tracking runs at ~30 Hz. **Test a non-Development build.**
- [x] **Fifth device test**: starvation fixed (camFPS 30, trackHz 30, cfg 640×480@30, threading Direct) but ARCore's pose **jumps 1.6–12 m per second** while tracking → planes=0, ring/PLACE MODEL never appear. Device-level VIO problem on OnePlus CPH2717 (matches CAM_IMU_DESYNC logs). Added **Manual mode** (always available via MANUAL ⇄ AR MODE pill; also PLACE MANUALLY after 12 s with no tracking): model placed in front of the camera and kept there; 1-finger drag anywhere = move X/Z, 2-finger slide up/down = raise/lower (Y), pinch = scale, twist = rotate, re-place = back in front; `ExitLiteMode` returns to AR. Diagnostics log `POSE JUMP` warnings (> 2 m/s while tracking).
- [x] **Model animations** (`AnimationControls.cs`): glTFast imports glTF animations as legacy `Animation` clips (AnimationMethod.Legacy, default). Preview (top-left, below badges) and AR (bottom centre, after placing) show ▶/❚❚ + clip name; with 2+ clips the name opens a list (switch with 0.25 s crossfade). Clips loop; first clip auto-plays; hidden for static models. Hooks: `ModelPreviewManager.LoadedModel`, `ModelPlacement.Model`. Icons Play/Pause/ChevronDown in `UIIcons`.
- [x] **Faster downloads + landing page** (`ThumbnailLoader.cs` new: `NetworkPriority` + polite thumbnail queue): opening a model pauses all background traffic (thumbnails, extra pages, recommendations); thumbnails load max 4 at a time, on-screen first; only 1 extra results page up front; download-link request has a 15 s timeout, "Still contacting Sketchfab…" after 4 s, `[Download]` timing logs; model file aborts after 20 s with no data. Recommendations cached 24 h (PlayerPrefs `RecommendationsCacheV1`, refreshed when recent searches change), 3 searches in parallel, 12 results/query, 640 px slide images, paused while results are shown or a download runs.
- [x] **8th Wall XR8 WebAR — the main AR on Android (merged to master 2026-11-07, confirmed working on device)** — on Android, VIEW IN AR opens `StreamingAssets/webar` (XR8 SLAM + three.js r174) in a full-screen WebView (`Plugins/Android/WebARView.java`, `WebARLauncher.cs`) instead of AR Foundation. Content + `/model.glb` served from https://appassets.androidplatform.net (secure context for the camera). Placement copied from the luna-tsingtao reference: XR8 `scale: 'responsive'` (ground = y 0 from frame 1), centre-ray onto the ground, smoothed reticle, ready after 8 cm of phone travel. Gestures: tap/PLACE MODEL, drag on ground, pinch 10–1000%, twist. Animations via AnimationMixer + clip picker. Shim: `requestPermission` → granted on Android (newer Chromium returns denied in WebViews). Logs: logcat tag `RealityDropWebAR`; chrome://inspect. Editor still uses ARScene. XR Engine license + attribution shown in-page.
- [ ] **Test full AR on an ARCore-certified phone** (Samsung Galaxy S/A, Pixel) — required to validate placement/anchors
- [x] **Light estimation** (`ARLightEstimation.cs`): brightness, color, main light direction, ambient SH, smoothed
- [ ] Optional: ARCore depth occlusion (model hidden behind real objects) on supported devices
- [ ] Clean up: delete old AR canvas objects (Spawn/Back/debug buttons, HintText) and `ARUIManager` from ARScene once the new HUD is confirmed on device

- [ ] **Before first device test**: lock orientation to Portrait (currently Auto Rotation); set a real package name (currently the template's `com.UnityTechnologies.com.unity.template.urpblank`); for iPhone: enable ARKit in XR Plug-in Management (iOS) + add a Camera Usage Description

### E. Snapshot: done on Android (XR8 WebAR) ✅
- [x] Shutter button in the AR UI (shown after placing) — `webar/index.html`
- [x] Capture: the WebGL canvas is read right after drawing (`canvas.toDataURL` in `onRender`), so the HTML UI is never in the photo; the reticle is hidden once placed. Photo size = render size (2x CSS px)
- [x] Save to gallery: `WebARView.savePhoto` → MediaStore `Pictures/RealityDrop` (Android 10+, no permission needed); Android 7–9 → app Pictures folder + media scan
- [x] Flash + thumbnail card "Saved to Gallery" + Share (Android share sheet, `WebARView.sharePhoto`)
- [ ] Photo resolution = render resolution (1.6x CSS px, about 660×1430 on a 1080p phone); a full-res capture would need a separate high-res render pass

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

- 2026-11-08 — WebAR: reverted the big/distance-scaled indicator to the original ring (it lowered FPS on device); render back to 2x (the auto 1.25x drop made models pixelated); removed the WebView hardware layer (extra full-screen copy per frame). Photo capture kept — `webar/app.js`, `WebARView.java`
- 2026-11-07 — WebAR: removed the pixel-dust effect (laggy); frame rate: render scale 1.6x (auto 1.25x if < 24 FPS for 6 s), WebView renderer priority IMPORTANT + hardware layer, FPS in the 2 s logcat line; photo capture (shutter, flash, save to Pictures/RealityDrop, share) — `webar/app.js`, `webar/index.html`, `webar/style.css`, `WebARView.java`
- 2026-11-07 — WebAR: bigger glowing placement indicator (0.2 m radius, halo + rotating dashed arc) that scales with distance (0.55x–2.6x, ~1x at 1.5 m); PLACE MODEL plays a blue spiral pixel-dust burst (360 particles, 1.8 s) + ground shockwave and the model grows out of it; `?preview=1` desktop check mode — `webar/app.js`
- 2026-11-07 — Merged xr8-webar into master: XR8 WebAR is now the Android AR path (tested working on OnePlus CPH2717); AR Foundation ARScene kept for the Editor — `PROJECT_GUIDE.md`
- 2026-11-07 — (xr8-webar) Fix black camera feed: Unity's activity has hardwareAccelerated=false, so the WebView now lives in its own hardware-accelerated full-screen Dialog window; bundled Inter font — `WebARView.java`, `webar/style.css`, `webar/fonts`
- 2026-11-07 — (branch xr8-webar) 8th Wall XR8 WebAR in an Android WebView — `StreamingAssets/webar/*` (new), `Plugins/Android/WebARView.java` (new), `WebARLauncher.cs` (new), `PreviewUIManager.cs`, `PreviewScreenUI.cs`
- 2026-11-07 — Download overlay: rotating neon ring + animated ". . ." on the stage text; removed the "Still contacting Sketchfab" message — `DownloadOverlay.cs`, `SketchfabAPIManager.cs`
- 2026-11-07 — Download priority, polite thumbnail loading, timeouts/timing logs, cached + parallel recommendations — `ThumbnailLoader.cs` (new), `SketchfabAPIManager.cs`, `Model/ModelDownloader.cs`, `RecommendationsPanel.cs`
- 2026-11-07 — Animation play/pause + clip picker on preview and AR — `AnimationControls.cs` (new), `PreviewScreenUI.cs`, `ARScreenUI.cs`, `ModelPreviewManager.cs`, `ModelPlacement.cs`, `UIIcons.cs`
- 2026-11-07 — Manual placement mode (free X/Y/Z, scale, rotate; toggle MANUAL ⇄ AR), pose-jump diagnostics — `ModelPlacement.cs`, `ARScreenUI.cs`, `ARDiagnostics.cs`
- 2026-11-07 — AR performance profile, tracking-rate diagnostics, quick placement (extended planes + feature points) with auto-snap to floor — `ARPerformanceProfile.cs` (new), `ARDiagnostics.cs`, `ModelPlacement.cs`, `ARSceneInitializer.cs`
- 2026-11-07 — Camera config now prefers 640×480 @30 (720p cut ARCore to ~7 fps); diagnostics log current camera config — `ARCameraConfigurator.cs`, `ARDiagnostics.cs`
- 2026-11-07 — In-depth AR diagnostics with automatic DIAGNOSIS — `ARDiagnostics.cs` (new), `ARSceneInitializer.cs`
- 2026-11-07 — Disabled leftover AR session/components in SampleScene (fixes XR Simulation + saves battery on device); ARCameraConfigurator guard — `SampleScene.unity`, `ARCameraConfigurator.cs`
- 2026-11-07 — Fallback changed to drift-free Camera mode (gyro look-around off by default) — `ModelPlacement.cs`, `ARScreenUI.cs`
- 2026-11-07 — Lite mode AR fallback for phones where ARCore can't track; tracking-reason hints — `ModelPlacement.cs`, `ARScreenUI.cs`
- 2026-11-07 — Device fix attempt for ARCore not tracking: session reset after model load, light camera config, lighter light estimation — `ARSceneInitializer.cs`, `ARCameraConfigurator.cs` (new), `ARLightEstimation.cs`
- 2026-11-07 — AR: glowing plane panels, ring indicator, PLACE MODEL button + placement flare — `ARGlowPlane.cs`, `ARGlowPlane.shader` (new), `ModelPlacement.cs`, `ARScreenUI.cs`
- 2026-11-07 — Fix: download screen thumbnail was cropped for a 100×100 placeholder rect on first show (looked stretched until the hi-res image arrived) — layout is now rebuilt before cropping and re-cropped on size change — `DownloadOverlay.cs`
- 2026-11-07 — Slideshow arrows + "‹ Recommended" back-to-home from search results — `RecommendationsPanel.cs`, `ResultsGridLayout.cs`, `SketchfabAPIManager.cs` (ResetSearch), `UIIcons.cs` (Next)
- 2026-11-07 — Slideshow: tapping a card shows all models for that name; VIEW opens the single model — `RecommendationsPanel.cs`
- 2026-11-07 — Adult content filter for search + recommendations (`ContentFilter.cs`); slideshow relevance order, real model names
- 2026-11-07 — Main screen recommendations reworked into a model slideshow (recent searches + anime picks); chips + `FlowLayoutGroup.cs` removed; `SketchfabAPIManager.OpenModel` + recent-search memory
- 2026-11-07 — AR overhaul: removed duplicate camera + duplicate ModelPlacement from ARScene; `ModelPlacement.cs` rewritten (stable anchoring, gestures, shadow, reticle); new `ARScreenUI.cs`, `ARLightEstimation.cs`, `UIKit.cs`, `UIPress.cs`; `ARSceneInitializer.cs` (status/error props, adds HUD + light estimation)
- 2026-11-07 — Back from preview restores the search feed (query, cards, scroll position) — `SearchSession.cs` (new), `SketchfabAPIManager.cs`
- 2026-11-07 — Preview screen redesign (design A): `PreviewScreenUI.cs`, `PreviewEnvironment.cs`, `UIIcons.cs`, `Turntable.shader`, `Backdrop.shader` (new); `PreviewControls.cs` rewritten as orbit camera; `ModelPreviewManager.cs` (ready/failed events, orbit framing); `SafeAreaFitter.cs` (no-backdrop option); `SketchfabAPIManager.cs` (saves title/author/size)
- 2026-11-07 — Sharpness + typography pass: Inter/Rajdhani fonts (`UIFonts.cs`), safe area (`SafeAreaFitter.cs`), uncompressed UI textures, render scale 1.0, hi-res download thumbnail, larger/higher-contrast text, full-width Cancel, stronger dim
- 2026-11-07 — Download progress screen with live %, MB, steps and Cancel — `DownloadOverlay.cs` (new), `ModelDownloader.cs`, `SketchfabAPIManager.cs`, `ResultCardView.cs`
- 2026-11-07 — Search row fitted inside the background neon frame (`fitToFrame`, frameSize 1690×185)
- 2026-11-07 — Premium search field + button, results area widened (1740) with even edge padding — `SearchBarStyler.cs`, `UIGradient.cs`, `UIPressRelay.cs` (new), `SearchUI.cs`, `ResultsGridLayout.cs`, both scenes
- 2026-11-07 — Premium iOS-style results: rounded glass cards, glow, skeletons, animations, author line, FREE badge, hidden scrollbar, pill toast — `ResultCardView.cs`, `UIRoundedCorners.cs` (new), `Resources/RealityDropUI/RoundedRect.shader` (new), `ResultsGridLayout.cs`, `SketchfabAPIManager.cs`
- 2026-11-07 — Card layout: thumbnail fills top (cover-cropped via uvRect), title scales with card width, max 2 lines — `ResultCardView.cs` (new), `SketchfabAPIManager.cs`, `ResultsGridLayout.cs`
- 2026-11-07 — Fixed overlapping result cards: responsive 2-column grid — `ResultsGridLayout.cs` (new), `SketchfabAPIManager.cs`
- 2026-11-07 — Fixed search returning only 24 results; stale-search guard; result cap; ScrollRect fallback — `SketchfabAPIManager.cs`, `SearchUI.cs`
- 2026-11-07 — Free-downloadable-only safety filter, on-screen search/download error messages, double-tap guard — `SketchfabAPIManager.cs`
- 2026-11-07 — Created this guide after a full project review — `PROJECT_GUIDE.md`, `CLAUDE.md`
