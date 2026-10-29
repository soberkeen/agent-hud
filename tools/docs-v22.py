# -*- coding: utf-8 -*-
# v2.2 文档同步：key 存 Windows 凭据管理器（README + AGENT.md 多处旧说法）
import io

# README
p = 'README.md'
s = io.open(p, encoding='utf-8').read()
old = '''## 数据配置（DeepSeek）

安装目录下的 `hdr-sidebar.ini`（也可从小窗右键菜单「数据配置」打开）：

```ini
deepseek_key=sk-xxxxxxxx     # platform.deepseek.com 创建的 API key
#refresh_minutes=5           # 可选：刷新间隔（分钟）
```

ZCode 用量无需任何配置（自动读本机 ZCode 凭证）。'''
new = '''## 数据配置（DeepSeek）

API Key 在设置页填写（侧边栏右键 → 设置 → 数据源页签 → 「测试连接」可立即验证）。
Key 存放于 **Windows 凭据管理器**（目标名 `AgentHUD/DeepSeekKey`），配置文件不含任何密钥。

ZCode 用量无需任何配置（自动读本机 ZCode 凭证）。'''
assert old in s, 'README section not found'
s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8').write(s)
print('README ok')

# AGENT.md 多处旧说法
p = 'AGENT.md'
s = io.open(p, encoding='utf-8').read()
pairs = [
    ('| DeepSeek 余额 | ⏳ **代码就绪，等用户在 hdr-sidebar.ini 填 deepseek_key**（安装目录） |',
     '| DeepSeek 余额 | ⏳ **代码就绪，等用户在设置页填 API Key**（存 Windows 凭据管理器，v2.2 起 ini 不落密钥） |'),
    ('- `agent-hud.ini`（exe 旁）— `deepseek_key=`（必填项）、可选 `deepseek_base` / `zcode_base` / `zcode_credentials` / `refresh_minutes`。右键菜单「数据配置」可直接打开编辑。',
     '- `agent-hud.ini`（exe 旁）— `deepseek_base` / `zcode_base` / `zc_mood_source` / `refresh_minutes` / `warn_low` / `warn_mid` / `hover_details` / `show_tray` / `show_ds` / `show_zc`。**deepseek_key 不在此文件**（v2.2 起存 Windows 凭据管理器，目标 AgentHUD/DeepSeekKey，旧 ini 明文 key 启动时自动迁移并从文件移除）。'),
    ('1. **DeepSeek key**：用户在安装目录 agent-hud.ini 填 `deepseek_key=`（右键菜单「数据配置」直达）。填完无需重启（≤5 分钟自动刷新）。',
     '1. **DeepSeek key**：用户在设置页粘贴（存 Windows 凭据管理器）。填完无需重启（≤5 分钟自动刷新）。'),
]
for old, new in pairs:
    assert old in s, 'AGENT.md anchor missing: ' + old[:50]
    s = s.replace(old, new)
io.open(p, 'w', encoding='utf-8').write(s)
print('AGENT.md ok')
