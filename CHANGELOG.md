# Changelog

All notable changes to Stream Music Request are documented here.

## [1.2.2] - 2026-10-03

### Added
- Added the SMR application icon to the main window and all custom dialog windows.
- Added the SMR icon as the application executable icon.
- Unified message boxes and dialog presentation with the application's dark visual style.

### Fixed
- Fixed AppMessageBox resource resolution so error notifications no longer crash the application when the dialog is displayed.
- Fixed custom dialog positioning so notifications open relative to the active application window and remain on the same monitor as the application.
- Fixed the nullable owner warning in AppMessageBox.

## [1.2.1] - 2026-10-03

### Fixed
- Donation Alerts logout now immediately updates the connection status in Settings and the main window.
- Donation Alerts authorization state changes are propagated immediately when the saved authorization is cleared.
- Donation Alerts connection status is now kept synchronized after authorization and logout operations.

## [1.2.0] - 2026-10-02

### Added
- Автоматическое восстановление соединения Donation Alerts после разрыва WebSocket.
- Экспоненциальная задержка повторных попыток подключения Donation Alerts от 5 до 60 секунд.
- Donation Alerts production integration for music orders.
- Donation Alerts authorization restoration on application startup.
- Donation Alerts normal-priority and SUPER-PRIORITY playback modes.
- Donation Alerts thresholds for RUB, EUR and USD.
- Synchronized Donation Alerts connection state between Settings and the main window.
- Persistent Twitch connection state with the current Twitch display name shown in the main window.

### Improved
- Multiple SUPER-PRIORITY donations are now processed sequentially instead of being rejected while another SUPER-PRIORITY order is playing.
- A failed playback startup no longer stops the viewer queue; the failed track is skipped and the next queued item is attempted.
- Main window status text is kept synchronized with the actual Twitch and Donation Alerts services.
- Removed obsolete main-window "Повторить трек" and redundant Twitch reward labels.

### Fixed
- Twitch username disappearing from the main-window connection status after closing Settings.
- Donation Alerts connection changes were previously logged but not reflected in the main-window UI.
- Queue processing after transient Media Foundation playback startup failures.

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

### Notes
- Donation Alerts integration was completed after the 1.1.0 release and is documented in 1.2.0.
- Temporary test projects remain in the repository until the final cleanup before release.

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
