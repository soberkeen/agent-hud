# DSH 插件需求：PermissionRequest → Agent HUD 桥接

## 目标

写一个 DeepSeek Harness 原生 cordis 插件，把 agent 运行期间的权限确认请求桥接到本地 Agent HUD 桌面侧边栏，让用户通过 HUD 角色身上的确认弹窗远程放行或阻断工具执行。

## 参考实现

`@deepseek-ai/dsh-hooks-claude-code`（npm 包，源码在 `deepseek-ai/deepseek-harness` 仓库 `packages/experimental/hooks-claude-code/`）——它已经接入了 harness 的 PermissionRequest 拦截层。你要做的是同样的拦截方式，但不执行命令行 hook，而是走文件协议与 HUD 通信。

查看它的 `src/index.ts` 了解如何拿到 PermissionRequest 拦截点和决策返回格式。

## HUD 侧文件协议（已实现，不需要改）

### 通信目录

```
%LOCALAPPDATA%\AgentHUD\events\drop\    ← 插件写请求文件
%LOCALAPPDATA%\AgentHUD\events\resp\    ← HUD 写响应文件
```

### 请求文件（插件写入 drop 目录）

文件名：`<id>.json`（id 为 32 位十六进制 GUID-N 格式，如 `a1b2c3d4e5f6789012345678abcdef01`）

```json
{
  "id": "a1b2c3d4e5f6789012345678abcdef01",
  "app": "dsh",
  "kind": "confirm",
  "title": "权限请求",
  "body": "DSH 请求执行 Bash 命令：npm install",
  "buttons": ["允许", "拒绝"],
  "timeout": 110,
  "created": 1730000000
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| id | string | 32 位十六进制唯一标识 |
| app | string | `"dsh"` 固定 |
| kind | string | `"confirm"` 固定 |
| title | string | 气泡标题（加粗显示） |
| body | string | 气泡正文 |
| buttons | string[] | 按钮文案数组，固定 `["允许", "拒绝"]` |
| timeout | number | 秒；HUD 超时后自动拒绝 |
| created | number | Unix 时间戳秒 |

### 响应文件（HUD 写入 resp 目录，插件轮询读取）

文件名：`<id>.json`（与请求同 id）

```json
{
  "id": "a1b2c3d4e5f6789012345678abcdef01",
  "button": "允许",
  "timeout": false
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| id | string | 与请求相同的 id |
| button | string\|null | 用户点击的按钮文案；超时为 null |
| timeout | boolean | true = 超时自动拒绝 |

### 决策映射

| 响应 | harness 行为 |
|------|-------------|
| `button === "允许"` | 放行工具执行（return allow） |
| `button === "拒绝"` 或 `timeout === true` | 阻断工具执行（return deny + reason） |

## 插件行为要求

1. **阻塞语义**：`PermissionRequest` 到来时，插件**必须阻塞**工具执行，直到用户在 HUD 上做出选择或超时——不能 fire-and-forget
2. **写入方式**：先写临时文件再 rename（原子操作），避免 HUD 读到半写状态
3. **轮询间隔**：200ms
4. **超时**：110 秒（与 HUD 设置页的确认超时一致），超时后返回 deny
5. **容错**：HUD 未运行时（110 秒无响应），默认拒绝并给出理由 `HUD 未响应`，agent 不挂
6. **清理**：决策完成后删除请求和响应文件
7. **幂等**：插件重复加载不会产生副作用

## 参考来源

- 拦截层接入方式：看 `@deepseek-ai/dsh-hooks-claude-code` 的 `src/index.ts` 如何注册 `PermissionRequest` 拦截
- cordis 插件结构：看 `dsh-plugin-desktop/src/notifications.ts` 的 `name`/`inject`/安装函数模式
- 决策返回格式：`{ decision: 'allow'|'deny', reason: string }`（harness 内部枚举）

## HUD 侧（已实现，不需要动）

- `agent-hud.exe` 监听 `drop` 目录（FileSystemWatcher），读到 JSON → 弹漫画气泡（白底、指向角色、带按钮）
- 用户点按钮 → HUD 写响应文件到 `resp` 目录 → 气泡消失
- HUD 已运行时实时生效；HUD 未运行时 drop 文件会保留，HUD 下次启动时处理
