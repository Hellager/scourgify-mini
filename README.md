# scourgify-mini

ScourgifyMini is a lightweight tray utility for privacy-focused Windows Quick Access cleanup.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8
- Wincent 0.2.4

## Behavior

- Runs as a single-instance tray application.
- Supports auto-start and localized tray menu text.
- Incognito Mode locks all Windows Quick Access backing files while enabled.
- When Incognito Mode is turned off or the app exits, it unlocks Quick Access and removes newly created Windows Recent `.lnk` shortcuts by default.
