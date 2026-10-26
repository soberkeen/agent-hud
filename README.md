# HDR 开关 / HDR Toggle

**Win11 桌面悬浮小窗，一键开关主显示器 HDR。**
A tiny always-on-top widget that toggles your primary display's HDR with a single click.

原生 WPF · 单文件零依赖 · 免管理员 · 自动边缘吸附半隐藏 · 附带标准安装向导

---

## 功能

- 🟢 **桌面悬浮圆钮**：绿色 = HDR 开，灰色 = 关；点击切换，3 秒轮询自动同步外部状态
- 🧲 **边缘吸附半隐藏**：拖近屏幕边缘自动吸附并只露一条小边；悬停弹出，移开自动缩回；半隐藏时首击只唤醒不误触
- 📦 **标准安装向导**：可选安装路径、桌面/开始菜单快捷方式、开机自启、控制面板「应用和功能」卸载
- ⌨️ **命令行**：`-state` / `-toggle` / `-on` / `-off` / `-quit`，退出码规整，方便脚本与快捷键调用
- 🪶 **轻量**：主程序约 20 KB，用 Windows 自带编译器即可构建，运行时零依赖

## 安装

从 [Releases](../../releases) 下载 `HDR开关安装器.exe`，双击按向导安装（per-user，不需要管理员）。

## 构建

无需安装任何 SDK——用的是 Windows 系统自带的 .NET Framework 编译器（`csc.exe`，C#5）：

```powershell
.\build.ps1        # 产出 hdr-toggle.exe 与 HDR开关安装器.exe
```

## 命令行用法

```
hdr-toggle.exe -state     打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
hdr-toggle.exe -toggle    切换一次（退出码=切换后的状态）
hdr-toggle.exe -on|-off   强制开/关
hdr-toggle.exe -quit      关闭运行中的小窗
```

## 兼容性与一个驱动怪癖的解法

部分显卡驱动（实测：雷神 P275MS PLUS @240Hz）在关闭 HDR 后会把
`advancedColorEnabled` 位**滞留在 1** 不清零，导致按官方位判定会把"关"读成"开"。

本项目用 **SDR 白电平**（`DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL`）做仲裁：
HDR 关闭时白电平恒为 1000（80 nit 基准），开启时随「SDR 内容亮度」抬高。
实测切换同毫秒生效、90 秒长稳零回弹。`tools/diag-hdr-curve.ps1` 是当时的诊断脚本，
遇到类似显示器问题可以直接拿去用。

## 目录结构

```
hdr-toggle.cs      主程序（CLI + WPF 小窗，全部逻辑）
setup.cs           WinForms 安装向导（内嵌主程序）
app.manifest       主程序清单（asInvoker / supportedOS / dpiAware）
setup.manifest     安装器清单
build.ps1          一键构建（-SetupOnly 只重打安装器，签名流程用）
sign.ps1           Authenticode 签名（配合代码签名证书）
发布说明.md         发行流程与防误报清单
script-edition/    早期 PowerShell 脚本版（功能等价，保留给喜欢脚本的用户）
tools/             诊断小工具
```

## License

[MIT](LICENSE)
