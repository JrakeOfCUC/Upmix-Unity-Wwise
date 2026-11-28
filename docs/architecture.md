# Architecture and work queue

Baseline: 25b4727 on codex/spatial-layout-design. The application plays separated stems as positioned emitters in Unity, changes songs at the current playback position, and supports editable layouts and a concert-hall return network.

## Dependencies and ownership

| Owner | Inputs and public boundary | State and output |
| --- | --- | --- |
| SampleScene, SpatialStemPlayer | Scene-serialized startup song, emitter references and event names. Unity lifecycle and IMGUI. | Per-track song, playing IDs, pause, mute, solo, EQ, send levels. Loads banks, records which loads it owns, retires emitters. |
| ListenerCameraController | Unity input, player MixerOpen property | Camera pose and mouse capture. |
| SpatialStemLabel | Scene label and emitter | World-space label and display name used by player. |
| SpatialStemPlayer layout methods | CaptureLayout and InstallLayout. Layout v1/v2, song enum names, track event names. | Editable objects and per-song JSON under persistentDataPath. Temp-file replacement on save. Errors surface through switchError. |
| SpatialStemPlayer.HallPreset | Player installation lifecycle, Wwise integration and native send ABI | Hall bank ownership, return objects, native buffer. Cleanup must follow playback lifecycle. |
| MacOutputSelector | macOS standalone Wwise device discovery | Output device selection. Windows and Editor code follow separate compile paths. |
| build_song_banks | Source project, stem folders and WwiseConsole CLI | Disposable Library staging project, song role validation and Windows/Mac StreamingAssets. Console codes 0 and 2 proceed to output validation. |
| spatial_hall_banks | Work-unit directory and mutable bank list after configure_effects | Augments the main bus work unit and appends a media-free hall bank. |
| wwise_work_unit | Semantic object key, or owner/effect/slot ID | Stable UUID and inline effect XML structure. No build-directory or process state. |
| docs/spatial-layout/render_layout.py | Design coordinates and layer annotations | Design JSON, PNG, SVG. Runtime layout saves have a separate schema. |

Dependency paths: camera → player → Wwise Unity API → platform native libraries. Scene → player → labels. Build CLI → song effects / hall builder → shared work-unit writer. Generated bank IDs, bus names, events and RTPC names connect the offline and runtime paths. HallPresetSmoke exercises private player contracts via reflection and requires the native integration and banks.

The inspected application entry points expose local files and native audio services. No application account, remote API, authentication or service retry contract was identified. Unity package manifests and ProjectSettings own engine configuration. Wwise SDK sources and generated bindings follow their vendor's versioning. The reproducible local setup and excluded inputs are described in HANDOFF.md.

## Decisions and progress

- 89d2015. Consolidated duplicated EffectSlot XML and UUID construction. Constructors retain the song and hall namespaces and callers supply the precise slot ID. Append-only property creation and hall preset property replacement have different semantics. Keep them distinct. Four serialized fixture trees matched the pre-migration output byte for byte. The CLI can now be imported for tests.
- 0a511e3. Clean clones contain platform folder metadata but lack bank metadata templates and platform directories. The publisher creates missing destinations and derives missing metadata deterministically, preserving existing GUIDs. Both platform outputs are validated before copying. Filesystem failures during copying can still interrupt publication. Rerun after fixing the failure.
- Validation entry point: python -B -m unittest discover -s tests. The GitHub workflow runs the suite on push and pull request. Native playback needs a separately provisioned checkout.

## Open work by dependency

1. Pin layout v1/v2 round trips, invalid input and playback rollback with the Unity smoke harness before separating layout persistence and editor UI from SpatialStemPlayer. Preserve serialized field names, enum ordinals, GUIDs and reflection entry points.
2. Song titles, durations and song IDs use parallel runtime tables. Bank generation additionally encodes localized input-folder names and absent harmony. Establish the exact catalog contract before consolidating. Folder names can differ from display titles.
3. Stage and return coordinates appear in the design renderer and runtime preset. Establish a tested build-time mapping that keeps design-only and runtime save formats separate.
4. Bank generation reuses a mutable Library directory. Exercise stale output, interrupted copy and concurrent runs against independent staging directories before changing publication semantics.
5. Native send layout, device selection, callback ordering and audio quality require actual platform checks. Record Windows and macOS results separately. Defer changes to room voicing, song availability or output device fallback to a product decision.
