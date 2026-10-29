# -*- coding: utf-8 -*-
# AGENT.md 文档同步（容错版：逐条替换，找不到的跳过并报告）
import io

p = 'AGENT.md'
s = io.open(p, encoding='utf-8').read()
applied, skipped = [], []

pairs = [
    ('| DeepSeek 余额 | ⏳ **代码就绪，等用户在 hdr-sidebar.ini 填 deepseek_key**（安装目录） |',
     '| DeepSeek 余额 | ⏳ **代码就绪，等用户在设置页填 API Key**（存 Windows 凭据管理器，v2.2 起 ini 不落密钥） |'),
    ('- `hdr-sidebar.ini`（exe 旁）— `deepseek_key=`（必填项）、可选 `deepseek_base` / `zcode_base` / `zcode_credentials` / `refresh_minutes`。右键菜单「数据配置」可直接打开编辑。',
     '- `agent-hud.ini`（exe 旁）— `deepseek_base` / `zcode_base` / `zc_mood_source` / `refresh_minutes` / `warn_low` / `warn_mid` / `hover_details` / `show_tray` / `show_ds` / `show_zc`。**deepseek_key 不在此文件**（v2.2 起存 Windows 凭据管理器，目标 AgentHUD/DeepSeekKey，旧 ini 明文 key 启动时自动迁移并从文件移除）。'),
    ('4. **DeepSeek 余额**：`GET https://api.deepseek.com/user/balance`，Bearer key，响应 `balance_info.total_balance/granted_balance/currency`。key 放 hdr-sidebar.ini。未配置显示「未配置」，失败显示 tooltip。',
     '4. **DeepSeek 余额**：`GET https://api.deepseek.com/user/balance`，Bearer key，响应 `balance_info.total_balance/granted_balance/currency`。key 存 Windows 凭据管理器（目标 AgentHUD/DeepSeekKey）。未配置显示「未配置」，失败显示 tooltip。'),
    ('1. **DeepSeek key**：用户在安装目录 hdr-sidebar.ini 填 `deepseek_key=`（右键菜单「数据配置」直达）。填完无需重启（≤5 分钟自动刷新）。',
     '1. **DeepSeek key**：用户在设置页粘贴（存 Windows 凭据管理器）。填完无需重启（≤5 分钟自动刷新）。'),
]
for old, new in pairs:
    if old in s:
        s = s.replace(old, new)
        applied.append(old[:30])
    else:
        skipped.append(old[:40])
io.open(p, 'w', encoding='utf-8').write(s)
print('applied:', len(applied))
for sk in skipped:
    print('skip:', sk[:60])
