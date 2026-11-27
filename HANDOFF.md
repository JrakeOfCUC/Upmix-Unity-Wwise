# Cross-device handoff

Open `WwiseUpmix` in Unity 2022.3.62f3c1. This snapshot includes project source, scenes, settings, package manifests, Wwise integration source, and the bank-generation script.

Wwise integration: 2025.1.8.4193, SDK 2025.1.8 build 9170. Install matching Wwise platform binaries on each device; native libraries and debug symbols are excluded from this source snapshot.

Audio inputs in `音源分离`, generated SoundBanks, build outputs, SDK archives and Unity caches remain local and are not uploaded. Obtain these separately or regenerate the banks before testing playback. `build_song_banks.py` requires a source Wwise project and the Wwise console executable as its two arguments. The cached project under Library is not versioned.

This is a progress snapshot, not a verified standalone playable distribution. Unity compilation and playback have not been validated during this synchronization.

Before changing code: fetch, inspect status and pull --ff-only on a clean branch. After a task: validate, commit, push and verify the remote commit. Use separate branches for simultaneous work across devices.
