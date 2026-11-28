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
2. Completed song catalog migration. Edit SONGS in song_catalog.py and run python song_catalog.py. It projects stable scene ordinals and one typed record per song into the player. Bank generation consumes the same entries directly. CI checks the projection and existing persisted identifiers.
3. Completed hall design migration. docs/spatial-layout/hall_design.py owns coordinates, track identity, return labels and initial mute state. The figure renderer and generated Unity preset consume its records. CI verifies both the design manifest and baked C# source. Per-return gains remain audio tuning in the runtime.
4. The build lock covers project preparation, Wwise generation and publication. Expected cached banks are invalidated before invoking Wwise. Publication stages both platforms on the destination volume, preserves existing GUIDs, and rolls back installed files on a recoverable replacement failure. A process or machine loss during replacement requires rerunning the CLI. The cached authoring project path is preserved.
5. Native send layout, device selection, callback ordering and audio quality require actual platform checks. Record Windows and macOS results separately. Defer changes to room voicing, song availability or output device fallback to a product decision.

## Validation on the Windows development machine

The song catalog build used the source Ambisonics project, local stems and WwiseConsole 2025.1.8. All 20 published Windows/Mac banks matched the prior device outputs by SHA-256 (5,751,173,364 bytes). macOS playback requires its own runtime check.

SpatialLayout owns the v1/v2 field schema, per-song filename and atomic file replacement. The player owns validation against the live catalog, installation, synchronization and rollback. The Windows Unity 2022.3.62f3c1 HallPresetSmoke test passed with file creation, replacement and reload as well as legacy restore. Run the Editor with -batchmode -nographics -wwiseEnableWithNoGraphics -runTests -testPlatform EditMode -testFilter HallPresetSmoke -projectPath <project> -testResults <xml>. Provision native Wwise plugin DLLs with their import metadata and Windows banks first.

Single-track switching now replaces just the selected playing ID. It shares post/seek/pause and unused-bank cleanup with layout installation. The Windows smoke test exercises a paused track replacement and checks its neighbor before the full-song switch.

SourceMap owns IMGUI map styles, listener-relative projection, numbering and legend labels. Startup and layout refresh rebuild its labels through one method. The layout selection grid reads the same legend. A headless Windows native smoke run passed after extraction; pixel-level rendering was not measured.

LoadedSoundBank owns the success/borrow/failure interpretation and conditional unload for song, hall and macOS channel-check banks. HallSendBuffer cleanup now runs even after the sound engine has stopped. The native Windows test loaded an unused song bank twice, disposed the borrower, verified the bank was still loaded, then disposed the owner and verified a fresh load succeeded.

Layout installation resolves every catalog reference, source song and position before creating native returns or posting audio. A Windows regression test initially failed on a layout whose final entry referenced an unknown song. It passed after the fix and verifies the original layout, ordered playback IDs and return resources on rejection.

A live stem owns its Wwise playing ID. Layout append, removal, replacement, seek and end-of-event callbacks use this same record. The scene continues to serialize only the emitter and event name. The Windows smoke test confirms append/remove preserves all neighboring IDs.

The mixer partial owns IMGUI events, picker and scroll state, and layout editor controls. MacOutputSelector owns channel-check playback and output selection. The transport owns pause policy. Unity built the macOS standalone application including Assembly-CSharp and the native plugin bundle, and the Windows Editor native smoke passed.

stem_effects.py owns EQ RTPC names, frequencies, filter modes, gain limits and room presets. Its generated StemEffects.cs binds both mix controls and Wwise parameter writes. The final native smoke passed and all 20 generated banks again matched the original hardware checkout by SHA-256.
