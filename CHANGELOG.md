# Changelog

All notable changes to ScourgifyMini are documented in this file.

## [0.1.0] - 2026-06-23

### Added

- Added a lightweight Windows tray utility for enabling Incognito Mode for Windows Quick Access.
- Added tray menu controls for Launch at startup, Incognito Mode, Language, About, and Exit.
- Added portable configuration stored next to the executable in `config.toml`.
- Added rolling log files under `logs/` next to the executable.
- Added localized UI resources for English, Simplified Chinese, Traditional Chinese, French, and Russian.
- Added theme-aware tray icons that switch between light and dark variants based on the Windows system theme.

### Changed

- Packaged managed dependencies into the executable for portable single-file release use.
- Embedded localized resources into the main executable so single-file releases do not require satellite resource DLLs.
- Updated release metadata and dependency binding redirects for the current dependency set.

### Fixed

- Preserved the saved Incognito Mode startup preference when startup activation fails.
- Degraded Quick Access locking gracefully when some Windows Quick Access backing files are missing.
- Fixed Release builds falling back to English when localized satellite resource folders are absent.
