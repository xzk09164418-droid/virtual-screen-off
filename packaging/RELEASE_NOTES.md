# v0.1.0 · Windows x64

首个可执行文件版本，由 GitHub Actions 构建；源码提交见包内 BUILD-INFO.txt。

下载 `virtual-screen-off-v0.1.0-windows-x64.zip`，完整解压，按包内 `QUICKSTART.md` 为自己的虚拟显示器生成本地标识，然后运行 `release/ScreenHotkey.exe`。请保留目录结构。

- 需要 Windows x64、.NET Framework 4.x 及已安装并可用的虚拟显示器驱动；驱动不随包提供。
- 包含托盘、切换/恢复工具、显示状态查询和可选 IR/ToF 辅助程序。
- 默认关闭自启动、自动关屏和 IR/ToF；先验证手动切换及恢复。
- `Ctrl+Alt+F9` 切换，`Ctrl+Alt+F12` 应急恢复；亦可运行 `release/VirtualScreenTest.exe --restore`。
- 不包含维护者的显示器标识、截图、日志或恢复数据。
- 编译、只读设备枚举及配置校验通过。未实际关屏或启用传感器验证。
- 程序未进行代码签名。IR/ToF 可用性取决于具体硬件。

SHA-256 见附件 `SHA256SUMS.txt`。原创部分采用 MIT；感谢 GPT 与 DeepSeek。

隐私：发行包不含维护者密钥、设备标识、截图或运行日志。配置脚本会生成本机显示器标识，测试会话可能保存桌面截图和诊断文件；反馈问题前请检查并脱敏，勿上传整个程序目录。
