# Cross-device handoff

Spatial layout preset (2026-11-28): see `docs/spatial-layout/README.md` on branch `codex/spatial-layout-design`. In the running app, press Esc, open the spatial editor, and choose the Hall B preset. It includes the stage layout, two early reflections and six wet returns. Generate the new `SpatialHallPreset.bnk` along with the song banks before use. The design JSON is not a runtime import file; use the app's Save Layout function for version-2 runtime layouts.

Open `WwiseUpmix` in Unity 2022.3.62f3c1. This snapshot includes project source, scenes, settings, package manifests, Wwise integration source, and the bank-generation script.

Wwise integration: 2025.1.8.4193, SDK 2025.1.8 build 9170. Install matching Wwise platform binaries on each device; native libraries and debug symbols are excluded from this source snapshot.

Audio inputs in `音源分离`, generated SoundBanks, build outputs, SDK archives and Unity caches remain local and are not uploaded. Obtain these separately or regenerate the banks before testing playback. `build_song_banks.py` requires a source Wwise project and the Wwise console executable as its two arguments. The cached project under Library is not versioned.

This is a progress snapshot, not a verified standalone playable distribution. Unity compilation and playback have not been validated during this synchronization.

Before changing code: fetch, inspect status and pull --ff-only on a clean branch. After a task: validate, commit, push and verify the remote commit. Use separate branches for simultaneous work across devices.

For module ownership, migration decisions, validation and open work, see [the architecture map](docs/architecture.md).
