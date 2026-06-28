# Privacy Policy

## Introduction

**ScourgifyMini** is a lightweight Windows tray utility focused on enabling Incognito Mode for Windows Quick Access. We value your privacy, and this policy explains how the application handles data on your device.

## Data Collection and Usage

### Information We Do **Not** Collect

ScourgifyMini explicitly commits to:

* **Not** creating any unique identifiers, trackers, or similar tools
* **Not** collecting usage statistics, performance metrics, or user behavior information
* **Not** uploading user, file, or system data to remote servers
* **Not** incorporating advertisements, telemetry, or analytics tools
* **Not** selling or sharing your personal data with third parties

### Locally Stored Data

ScourgifyMini stores data only next to `ScourgifyMini.exe` for portable use:

1. **Configuration Data**
   Stored in `config.toml`, including:

   * selected language
   * launch-at-startup preference
   * Incognito Mode preference
   * cleanup preference for new Recent shortcuts when Incognito Mode is unlocked

2. **Log Information**
   Saved in the `logs\` directory, containing local troubleshooting records such as:

   * application startup and shutdown records
   * application version and local executable/config/log paths
   * selected language and feature state
   * Quick Access lock/unlock status
   * counts of affected shortcuts
   * error and warning logs

   If Windows reports a failure while deleting a newly created Recent shortcut, the log may include the affected local shortcut path.

   *All logs are used for local troubleshooting only and are **never** uploaded by ScourgifyMini.*

3. **Windows Quick Access Data**
   When Incognito Mode is enabled, ScourgifyMini locally locks the Windows Quick Access data used for recent files and frequent folders so new items are not added while the mode is active.

   When Incognito Mode is disabled or the application exits, ScourgifyMini unlocks those local resources. If cleanup is enabled, it may delete new Recent shortcut files created during the lock period. ScourgifyMini does **not** read the contents of your personal files.

### Network Access Information

ScourgifyMini does **not** make network requests during normal operation.

The About window may contain a project page link. Opening that link is user-initiated and handled by your default browser.

## Data Protection and Deletion

All ScourgifyMini data is stored locally. The application does not perform remote synchronization or uploads.

To completely remove ScourgifyMini's local data, exit the application and manually delete the following items from the directory containing `ScourgifyMini.exe`:

* `config.toml`
* `logs\`

If launch at startup was enabled, disable it from the tray menu before deleting the application, or remove the `ScourgifyMini` entry from the current user's Windows startup registry key.

## Permission Usage Information

System permissions used by ScourgifyMini are only for implementing core functions:

* **File System Access**: For reading and writing configuration files, writing local logs, and locking/unlocking Windows Quick Access related files
* **Windows Quick Access Access**: For preventing new recent files and frequent folders from being added while Incognito Mode is active
* **Registry Access**: For reading the current user theme setting to choose the tray icon style, and for modifying the current user's startup item only when launch at startup is enabled or disabled
* **Single Instance Lock**: For ensuring only one ScourgifyMini instance runs at a time
* **Network Access**: ScourgifyMini does not use network access during normal operation; project links are opened only when requested by the user

## Privacy Policy Changes

If there are significant changes, we will post notifications in the project repository or on the project homepage.

## Contact Information

If you have questions or suggestions about this privacy policy, please contact us through the GitHub project page.

> Last Updated: June 28, 2026
