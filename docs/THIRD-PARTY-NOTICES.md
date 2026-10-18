# 第三方组件说明

## Windows SDK .NET 投影

- 组件：Microsoft.Windows.SDK.NET.Ref 10.0.19041.57，Microsoft。
- 用途：访问 Windows Composition，为 Dock 和隐藏把手绘制实时背景模糊与圆角。
- 随程序分发：Microsoft.Windows.SDK.NET.dll、WinRT.Runtime.dll。
- 许可：Windows SDK 许可，https://aka.ms/WinSDKLicenseURL 。版本和许可地址来自 Microsoft 发布包的 nuspec 元数据。
- 项目：https://github.com/microsoft/CsWinRT 。

Windows Composition 由 Windows 提供；应用不分发 WinUI 或 Win2D，不读取或上传背景像素。

内置字体的许可证原文位于程序目录的“字体许可”文件夹。
