# Agent HUD

**Win11 桌面悬浮侧边栏：HDR 开关 + DeepSeek 余额 + GLM 订阅额度，角色动画化。**
A floating Windows 11 sidebar: HDR toggle, DeepSeek balance and GLM coding-plan quota, with animated characters.

> 前身项目名 HDR 开关 / HDR Toggle；v2.1 起更名 **Agent HUD**，仓库已更名 `soberkeen/agent-hud`。

原生 WPF · 单文件零依赖 · 免管理员 · 自动边缘吸附半隐藏 · 附带标准安装向导

---

## 功能

- 🟢 **HDR 开关**：绿钮双击确认防误触，3 秒轮询同步系统状态
- 🐋 **DeepSeek 余额**：悬停大肥鱼弹出余额抽屉；key 存 Windows 凭据管理器（配置文件不落密钥）
- 🐺 **GLM 订阅额度**：悬停狼女弹出五小时+本周双额度圈；自动读取本机 ZCode 登录凭证，零配置
- 🖱️ **独立双抽屉**：悬停临时展开、单击独立固定（可同时固定两个）、双击启动对应程序（ZCode/DSH）
- 🔔 **通知气泡**：漫画式气泡从角色头顶冒出，方位自适应（贴左往右弹/贴右往左弹）；ZCode hooks 接入后，任务完成/权限请求直接在角色身上弹出，权限确认可点「允许/拒绝」远程回传
- 🧲 **边缘吸附半隐藏** + Home Indicator 把手拖动
- 🎭 **GIF 角色动画**：按额度状态自动切换（标准/紧张/痴呆三档）
- ⚙️ **托盘 + 设置实时预览**：皮肤×3（黑色毛玻璃/白色毛玻璃/macOS Dock）、全屏让位、额度阈值、开机自启
- ⌨️ **命令行**：`-state` `-toggle` `-on|-off` `-quit` `-notify` `-confirm` `-install-hooks` `-debugdata` 等，退出码规整

## 安装

从 [Releases](../../releases) 下载 `AgentHUD-Setup.exe`，双击按向导安装（per-user，不需要管理员）。安装器会自动完成 ZCode hooks 接入和双击启动路径探测。

## 构建

无需安装任何 SDK——Windows 自带的 .NET Framework 编译器（`csc.exe`，C#5）：

```powershell
.uild.ps1        # 产出 agent-hud.exe 与 AgentHUD-Setup.exe
```

## 命令行用法

```
agent-hud.exe -state        打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
agent-hud.exe -toggle       切换一次（退出码=切换后的状态）
agent-hud.exe -on|-off      强制开/关
agent-hud.exe -quit         退出
agent-hud.exe -notify "标题" "正文" [zcode|dsh]   弹通知气泡
agent-hud.exe -confirm --title T --body B [--timeout 120] --wait   弹确认气泡（阻塞，退出码 0=第一个按钮 2=第二个 3=超时）
agent-hud.exe -install-hooks  合并 ZCode hooks + 探测启动路径（安装器自动调用）
agent-hud.exe -debugdata    测试两个数据源（只输出成败与数字，密钥脱敏）
```

## ZCode hooks 接入（通知与权限确认）

在 `~/.zcode/cli/config.json` 的 `hooks` 块中加入（与已有 hooks 合并，`enabled: true` 必需）：

```json
{
  "hooks": {
    "enabled": true,
    "events": {
      "Stop": [{ "hooks": [{ "type": "process",
        "command": "C:\Users\<你>\AppData\Local\Programs\Agent HUD\agent-hud.exe",
        "args": ["-notify", "任务完成", "ZCode 会话已结束", "zcode"], "timeoutMs": 15000 }] }],
      "PermissionRequest": [{ "hooks": [{ "type": "process",
        "command": "C:\Users\<你>\AppData\Local\Programs\Agent HUD\agent-hud.exe",
        "args": ["-confirm", "--title", "权限请求", "--body", "ZCode 请求执行操作", "--app", "zcode", "--timeout", "110", "--wait"],
        "timeoutMs": 120000 }] }]
    }
  }
}
```

`PermissionRequest` 的阻塞回传：HUD 确认气泡点「允许」→ hook 退出码 0（放行）；点「拒绝」→ 退出码 2（阻断）；超时 → 3。DSH 会话可用官方桥接包 `@deepseek-ai/dsh-hooks-claude-code` 复用同一份 hooks.json。

## 数据与密钥

- DeepSeek API Key 存 **Windows 凭据管理器**（目标 `AgentHUD/DeepSeekKey`），配置文件不含任何密钥
- GLM 额度自动读取本机 ZCode 桌面端登录凭证，零配置
- ZCode hooks 事件文件在 `%LOCALAPPDATA%\AgentHUD\events\`，仅本机同权限进程可写

## 兼容性与 HDR 读数怪癖

部分显卡驱动（实测：雷神 P275MS PLUS @240Hz）关闭 HDR 后会把 `advancedColorEnabled` 位滞留在 1，导致按官方位判定把"关"读成"开"。本项目用 **SDR 白电平**仲裁：HDR 关闭时白电平恒为 1000，开启时随「SDR 内容亮度」抬高。`tools/diag-hdr-curve.ps1` 是当时的诊断脚本。

## 目录结构

```
agent-hud.cs      主程序（CLI + WPF 侧边栏 + 数据源 + 凭据解密 + 气泡系统）
setup.cs          WinForms 安装向导（内嵌主程序，装完自动 -install-hooks）
build.ps1         一键构建（-SetupOnly 只重打安装器，签名流程用）
sign.ps1          Authenticode 签名
agent-hud.ico     应用图标
assets/           运行时角色动画与静态兜底图（构建产出）
发布说明.md        发行说明与签名指引
script-edition/   早期 PowerShell 脚本版（仅 HDR 开关功能，历史保留）
tools/            诊断与素材制作工具
```

## License

[MIT](LICENSE)
