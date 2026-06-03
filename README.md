# ScourgifyMini

Read this in other languages: [English](README.md) | [中文](README.zh-CN.md)

ScourgifyMini is a lightweight Windows tray utility that enables Incognito Mode for Windows Quick Access.

While Incognito Mode is enabled, Windows Quick Access, including recent files and frequent folders, will not receive new items.

## Features

- No installation required, portable use
- Isolated data, easy to clean up
- One-click incognito mode for privacy protection

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8

## Usage

1. Run `ScourgifyMini.exe`.
2. Use the tray icon menu to toggle:
   - **Launch at startup**
   - **Incognito Mode**
   - **Language**
3. Exit from the tray menu.

## Build

Open `ScourgifyMini.sln` in Visual Studio, or build it with an MSBuild version that supports .NET Framework WPF projects and Fody 6.x.

The project targets .NET Framework 4.8 and uses Costura.Fody to package managed dependencies.

## License

MIT
