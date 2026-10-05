# 虚拟屏幕关屏工具

在 Windows 中将桌面切换到已安装的虚拟显示器，使笔记本内屏关闭时桌面程序继续运行，并提供热键恢复和独立守护。

## 功能与前提

- 虚拟显示路径切换、窗口状态保存、内屏恢复、托盘和快捷键。
- 可选定时关屏、真实输入恢复、IR 人脸存在检测与 ToF 距离逻辑。
- 需要自行安装兼容的虚拟显示器驱动，并先在 Windows 中确认该显示器工作正常。
- 仅适用于登录后的交互式桌面；状态查询反映显示路径，不直接测量背光。

仓库不附带驱动或维护者的显示器设备标识。IR/ToF 依赖具体硬件，默认关闭；人脸存在检测不提供身份认证。

## 构建

Windows x64，系统 .NET Framework 4.x、WinRT 元数据。在 **Windows PowerShell 5.1** 中：

```powershell
Copy-Item config.example.yaml config.yaml
powershell -NoProfile -ExecutionPolicy Bypass -File .\code\build.ps1
```

编译结果在 `release/`。示例默认关闭自启动、自动关屏、IR 与 ToF，保留手动热键。

## 选择自己的虚拟显示器

先用下列脚本列出活动显示器；此操作只查询，不改变显示状态：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\code\configure-display.ps1
```

确认列表中哪个编号是自己安装的虚拟屏，再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\code\configure-display.ps1 -Index 1
```

`1` 仅是示例，务必按实际列表选择。脚本拒绝内屏，写入 `release/test-target.txt` 和 `release/test-adapter.txt`；外接物理显示器也可能出现在列表中，需要自行识别虚拟屏。两个文件只在本机使用，不要上传。

## 使用与恢复

```powershell
.\release\ScreenHotkey.exe --check-config
.\release\ScreenHotkey.exe
```

`Ctrl+Alt+F9` 切换熄屏/恢复，`Ctrl+Alt+F12` 应急恢复。先确认恢复方式，再进行短时间手动验证。也可运行 `release/VirtualScreenTest.exe --restore`。编辑配置后退出托盘并重启；恢复标记和 `TestResults/` 可能包含截图，不要在会话中删除。

当前代码还含可选传感器规则。不同硬件可能无法编译或获取读数，启用前需要自行验证。本次发布准备只编译和校验配置，未切换显示器、启用摄像头或修改自启动。

## 隐私、许可与致谢

本项目原创部分采用 [MIT License](LICENSE)。第三方依赖和素材遵守各自许可，详见 [第三方说明](THIRD_PARTY_NOTICES.md)。

感谢 **OpenAI GPT** 与 **DeepSeek** 在开发、排查问题和文档整理中的帮助，详见 [致谢](ACKNOWLEDGEMENTS.md)。本项目为个人工具，与游戏厂商、联想或 AI 模型提供方无官方关联。

真实配置、密钥、日志和截图请留在本机，详见 [隐私说明](PRIVACY.md)。首次发布为源码版本，不包含虚拟环境、游戏文件、驱动或预编译程序。

## 下载可执行版本

从 [Releases](https://github.com/xzk09164418-droid/virtual-screen-off/releases) 下载 Windows x64 ZIP 和 SHA256SUMS.txt，解压后先阅读 QUICKSTART.md。无需自行编译；程序未签名，不附带驱动。
