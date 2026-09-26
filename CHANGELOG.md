# Changelog

All notable changes to Stream Music Request are documented here.

## [1.1.0] - 2026-09-26

### Added
- OBS integration with a local HTTP widget and local HTTP audio stream.
- Persistent playback settings.
- Persistent music volume between application sessions.
- Default Yandex Music playlist setting with optional automatic loading on application startup.
- Playback settings validation and explicit save action.
- "Open logs folder" button in Settings.
- Twitch connection state synchronization between the main window and Settings.
- Automatic Twitch connection recovery after temporary disconnects.
- User notification when the saved Twitch OAuth session can no longer be restored.

### Improved
- Viewer music playback operations are serialized to prevent race conditions during rapid track transitions.
- Main music automatically skips tracks that cannot be started because playback startup fails.
- Yandex Music direct playback URL resolution retries transient failures.
- Playback settings are exposed in a dedicated Settings section.
- Current Twitch Channel Point reward bindings remain manual and are not auto-created.

### Fixed
- Twitch EventSub recovery after long periods of inactivity.
- The first Twitch music order after application startup.
- Stale audio playback completion events during rapid playback transitions.
- Media Foundation playback startup failures no longer stop the main music sequence.
- Removed unused track cover loading and cover UI.

### Not included in 1.1.0
- Donation Alerts integration remains planned for a later development stage.
- Temporary test projects and development files are intentionally retained until Donation Alerts integration is completed.

## [1.0.0] - Initial working version

### Implemented
- Initial WPF application architecture with separate Core, Twitch, Yandex, Audio, Infrastructure and App layers.
- Twitch OAuth/device authorization and restoration of the saved authorization session.
- Twitch current-user detection and Channel Points integration.
- Manual binding of existing Twitch Channel Point rewards to application actions.
- "Заказ музыки" reward with viewer text input.
- "Пропустить трек" reward for skipping the current track.
- Yandex Music authorization and music data retrieval.
- Loading Yandex Music albums and playlists as main music.
- Validation of Yandex Music links for viewer requests.
- FIFO queue for viewer music requests.
- Switching between main music and viewer-requested music.
- Audio playback through NAudio and Media Foundation.
- Basic playback controls and volume control.
- Local application logging to app.log.
- Initial settings infrastructure for Twitch and Yandex Music.
