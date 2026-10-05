# 快速开始

1. 先安装并启用自己的虚拟显示器驱动。本包不提供驱动。
2. 在解压后的根目录打开 Windows PowerShell 5.1，运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\code\configure-display.ps1
```

3. 确认输出中哪个编号是虚拟屏。用实际编号替换以下示例 `1`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\code\configure-display.ps1 -Index 1
```

该步骤只生成本机设备标识，不切换显示器。不要选择真实外接显示器，不要上传生成的 test-target.txt / test-adapter.txt。

4. 双击 `release/ScreenHotkey.exe`。默认不自动关屏或启用传感器；Ctrl+Alt+F9 手动切换，Ctrl+Alt+F12 应急恢复。也可以运行 `release/VirtualScreenTest.exe --restore`。
5. 验证恢复正常后，再按 README 修改 config.yaml，并重启托盘程序。不要改变目录结构。

程序未签名；请从项目发布页下载，并核对 SHA256SUMS.txt。读取配置时会产生本地日志，不要随问题反馈直接上传。
