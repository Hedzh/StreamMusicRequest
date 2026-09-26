# Changelog

All notable changes to Stream Music Request are documented here.

## [1.1.0] - 2026-09-26

### Added
- OBS integration with local HTTP widget and audio stream.
- Persistent playback settings.
- Persistent music volume between application sessions.
- Default Yandex Music playlist setting with optional automatic loading on application startup.
- Playback settings validation and explicit "Save settings" action.
- "Open logs folder" button in Settings.
- Twitch connection state synchronization between the main window and Settings.
- Automatic Twitch connection recovery after temporary disconnects.
- User notification when Twitch OAuth authorization can no longer be restored.

### Improved
- Viewer music playback operations are serialized to prevent race conditions during track transitions.
- Main music automatically skips tracks that cannot be started because playback startup fails.
- Yandex Music direct playback URL resolution retries transient failures.
- Settings now expose the playback configuration in a dedicated section.
- Current Twitch Channel Point reward bindings remain manual and are not auto-created.

### Fixed
- Twitch EventSub recovery after long periods of inactivity.
- First Twitch music order after application startup.
- Stale audio playback completion events during rapid playback transitions.
- Media Foundation playback startup failures no longer stop the main music sequence.
- Removed unused track cover loading and cover UI.

### Not included in 1.1.0
- Donation Alerts integration remains planned for a later development stage.
- Temporary test projects and development files are intentionally retained until Donation Alerts integration is completed.
