# Agent HUD

**Agent HUD——Win11 桌面悬浮侧边栏：一键开关 HDR + DeepSeek 余额 + GLM 订阅用量。**

> 前身项目名 HDR 开关 / HDR Toggle；v2.1 起更名 Agent HUD（仓库名沿用 hdr-toggle）。
A tiny always-on-top sidebar: HDR toggle for your primary display, DeepSeek balance, and GLM Coding Plan quota rings.

原生 WPF · 单文件零依赖 · 免管理员 · 自动边缘吸附半隐藏 · 附带标准安装向导

---

## 功能

- 🟢 **HDR 开关圆钮**：绿色发光 = HDR 开，灰色 = 关；**双击确认防误触**（单击变琥珀「确认？」，3 秒内再点才切换，超时自动还原），3 秒轮询自动同步外部状态
- 🐋 **DeepSeek 余额**：悬停角色图标，余额在图标下方原位滑出显示；key 在设置页配置
- 🐺 **ZCode / GLM Coding Plan 用量**：悬停角色图标，五小时 + 本周双额度圈（圈内套剩余数字）在下方滑出；自动读取本机 ZCode 桌面端登录凭证（零配置）
- 🎭 **会变脸的动态角色**：两个角色图标是 AI 视频片段抽帧的循环动画——余额/额度充足时神态如常，偏低时慌张，见底时发呆（阈值可在设置页调，数据异常时定格不播）
- 🖱️ **悬停弹出、点击固定**：详情面板跟随悬停出现、离开自动消失；点一下图标常驻，再点收回；图标角标常显额度健康状态（绿/黄/红，未配置为灰；ZCode 角色为 5 小时/本周双点）
- ⚙️ **托盘 + 设置页**：托盘左键唤出侧边栏（隐藏走托盘右键菜单，不会误点丢失）、右键进设置；设置含开机自启、角色显隐、额度阈值、GLM 表情驱动来源、刷新间隔、API Key 测试连接
- 🧲 **边缘吸附半隐藏**：拖近屏幕边缘自动吸附并只露一条小边；悬停弹出，移开自动缩回；半隐藏时首击只唤醒不误触
- 📦 **标准安装向导**：可选安装路径、桌面/开始菜单快捷方式、开机自启、控制面板「应用和功能」卸载
- ⌨️ **命令行**：`-state` / `-toggle` / `-on` / `-off` / `-quit` / `-debugdata`，退出码规整，方便脚本与快捷键调用
- 🪶 **轻量**：主程序约 1.4 MB（含内嵌图标与表情动画帧），用 Windows 自带编译器即可构建，运行时零依赖

## ZCode 用量是怎么拿到的

小窗读取 ZCode 桌面端存在本机的登录凭证（`~/.zcode/v2/credentials.json` 的 OAuth token，
AES-GCM 加密，直接在本地解密，不联网传输密钥），用它调用
`/api/monitor/usage/quota/limit` 拿到五小时/本周额度。**不需要额外配置**——
在 ZCode 里重新登录后，小窗下次刷新会自动使用新凭证。

## 安装

从 [Releases](../../releases) 下载 `HDR开关安装器.exe`，双击按向导安装（per-user，不需要管理员）。

## 构建

无需安装任何 SDK——用的是 Windows 系统自带的 .NET Framework 编译器（`csc.exe`，C#5）：

```powershell
.\build.ps1        # 产出 hdr-toggle.exe 与 HDR开关安装器.exe
```

## 命令行用法

```
hdr-toggle.exe -state        打印 HDR 状态（退出码 0=ON 1=OFF 2=错误）
hdr-toggle.exe -toggle       切换一次（退出码=切换后的状态）
hdr-toggle.exe -on|-off      强制开/关
hdr-toggle.exe -quit         退出（托盘+侧边栏）
hdr-toggle.exe -debugdata    测试 DeepSeek/ZCode 两个数据源（打印成败与数值）
hdr-toggle.exe -settings     启动并打开设置页
hdr-toggle.exe -mockcycle    调试演示：合成数据循环切换三种额度状态（不联网）
```

## 数据配置（DeepSeek）

安装目录下的 `hdr-sidebar.ini`（也可从小窗右键菜单「数据配置」打开）：

```ini
deepseek_key=sk-xxxxxxxx     # platform.deepseek.com 创建的 API key
#refresh_minutes=5           # 可选：刷新间隔（分钟）
```

ZCode 用量无需任何配置（自动读本机 ZCode 凭证）。

## 兼容性与一个驱动怪癖的解法

部分显卡驱动（实测：雷神 P275MS PLUS @240Hz）在关闭 HDR 后会把
`advancedColorEnabled` 位**滞留在 1** 不清零，导致按官方位判定会把"关"读成"开"。

本项目用 **SDR 白电平**（`DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL`）做仲裁：
HDR 关闭时白电平恒为 1000（80 nit 基准），开启时随「SDR 内容亮度」抬高。
实测切换同毫秒生效、90 秒长稳零回弹。`tools/diag-hdr-curve.ps1` 是当时的诊断脚本，
遇到类似显示器问题可以直接拿去用。

## 目录结构

```
hdr-toggle.cs      主程序（CLI + WPF 侧边栏 + 数据源 + 凭证解密，全部逻辑）
setup.cs           WinForms 安装向导（内嵌主程序）
app.manifest       主程序清单（asInvoker / supportedOS / dpiAware）
setup.manifest     安装器清单
build.ps1          一键构建（-SetupOnly 只重打安装器，签名流程用）
sign.ps1           Authenticode 签名（配合代码签名证书）
icon-*.png         角色静态图标（内嵌资源，动画加载失败时兜底）
anim-*.png         角色表情动画精灵图（内嵌资源，normal/worry/daze × 两角色）
hdr-toggle.ico     exe 与托盘图标（win32icon）
refs/              角色参考图（重制图标与动画帧的素材）
发布说明.md         发行流程与防误报清单
script-edition/    早期 PowerShell 脚本版（仅 HDR 开关，保留给喜欢脚本的用户）
tools/             诊断与制作小工具
```

## License

[MIT](LICENSE)
