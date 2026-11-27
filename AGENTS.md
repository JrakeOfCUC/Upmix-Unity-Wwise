# Collaboration workflow

- Before work, inspect git status and fetch origin. Update a clean working branch with git pull --ff-only. Preserve user changes.
- Use separate task branches for concurrent work. Never force-push or automatically merge other work.
- After each completed task, run appropriate checks, review the diff, commit task-related files, and push. The user requests synchronization after completed tasks.
- Verify the remote commit before reporting success. Report the branch, commit, checks and any synchronization failure.
- If authentication fails, preserve local work and clearly report that it has not been synchronized.
- Never commit credentials, Unity Library/Temp/Logs/UserSettings, build caches or generated IDE files. Review large assets and third-party SDKs before importing. Keep Unity assets and their .meta files together.
- Record setup and handoff information in tracked documentation for other devices.

## Another device

Clone https://github.com/JrakeOfCUC/Upmix-Unity-Wwise.git and open the clone as the workspace. Configure Git authentication and commit identity on each device. Fetch and check out the task branch; pull --ff-only before working when the working tree is clean. Commit and push completed work. Avoid simultaneous edits to the same branch on multiple devices.

Git synchronizes committed files, not installed tools, local caches, credentials or chat history.
