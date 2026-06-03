# 几净Mini

其他语言版本: [中文](README.zh-CN.md) | [English](README.md)

ScourgifyMini 是一个轻量级 Windows 托盘工具，支持为 Window 快速访问开启无痕模式。

在无痕模式开启期间 Windows 快速访问(包括最近使用的文件和常用文件夹)将不会有任何新增项。

注意：开启无痕模式后不会有新增项，但同样无法再从快速访问中删除相关项；如需删除，请先关闭无痕模式。

## 功能

- 无需安装，便携使用
- 数据隔离，方便清理
- 一键无痕，保护隐私

## 运行要求

- Windows 10 或 Windows 11
- .NET Framework 4.8

## 使用方式

1. 运行 `ScourgifyMini.exe`。
2. 通过托盘图标菜单切换：
   - **开机启动**
   - **无痕模式**
   - **语言**
3. 使用托盘菜单退出程序。

## 构建

使用 Visual Studio 打开 `ScourgifyMini.sln`，或使用支持 .NET Framework WPF 项目和 Fody 6.x 的 MSBuild 版本构建。

项目目标框架为 .NET Framework 4.8，并使用 Costura.Fody 打包托管依赖。

## 许可证

MIT
