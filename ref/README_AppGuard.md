# Games8Th.Team AppGuard

> Windows 应用权限限制工具 · 带 GUI 与 CMD 双界面 · 彩色日志输出
>
> **Games8Th.Team**

---

## 这是什么

一个可以**限制指定软件权限**的 Windows 工具。选择一个程序（或文件夹），
AppGuard 会通过 **NTFS ACL（DENY 规则）** 真正剥夺它的访问权限 —— 不是隐藏窗口，
不是杀进程，而是让系统本身拒绝目标。

- **GUI**：图形界面，勾选即用，实时日志面板
- **CMD**：交互式控制台 + 子命令，适合批处理与自动化
- **彩色日志**：🟢 绿=成功 · 🔴 红=失败 · 🟡 黄=警告/进行中
- **可逆**：一键解除，完整还原原有权限

---

## 快速开始

### 方式一：免安装 EXE（推荐）

`dist\` 目录下已打包好两个独立可执行文件，**无需安装 Python**：

| 文件 | 说明 |
|------|------|
| `Games8Th.Team-AppGuard.exe` | 图形界面版（约 8.9 MB） |
| `Games8Th.Team-AppGuard-CLI.exe` | 命令行版（约 6.0 MB） |

双击 `Games8Th.Team-AppGuard.exe` → 在左侧勾选目标 → 点「▶ 实施限制」，
右侧日志面板实时显示结果（绿=成功 / 红=失败 / 黄=警告）。

### 方式二：源码运行

1. 把整个 `Games8ThTeam-AppGuard` 文件夹放到任意位置
2. **右键 `启动-GUI.bat` → 以管理员身份运行**（脚本会自动请求提权）
3. 命令行版本：右键 `启动-CLI.bat` → 以管理员身份运行

> ⚠️ **建议以管理员身份运行**，否则对 `C:\Windows\System32`、
> `C:\Program Files` 下的目标会因权限不足而失败（日志会以红色标出）。
> 界面右上角有「以管理员重启」按钮可直接提权。

---

## 限制策略

| 策略 | 说明 | 效果 |
|------|------|------|
| `DENY_EXEC` | 禁止执行 | 程序无法启动、脚本无法运行（默认） |
| `DENY_WRITE` | 禁止写入 | 目标目录变为只读，程序无法写配置/存档 |
| `DENY_READ` | 禁止读取 | 目标无法被访问读取 |
| `DENY_ALL` | 完全封禁 | 拒绝一切访问 |

双击表格中的目标即可切换策略。

---

## 命令行用法

```
python src/launcher.py cli              # 进入交互式控制台（推荐）
python src/launcher.py cli list         # 列出目标及实时状态
python src/launcher.py cli add <路径> -s DENY_EXEC
python src/launcher.py cli remove <序号|名称>
python src/launcher.py cli toggle <序号|名称>
python src/launcher.py cli apply        # 对启用目标实施限制
python src/launcher.py cli clear        # 解除全部限制
python src/launcher.py cli verify       # 核验当前状态
python src/launcher.py cli demo         # 安全演示（用临时文件，不碰系统）
python src/launcher.py cli targets      # 列出常见可限制目标
```

交互式控制台内命令：`add` `apply` `clear` `list` `verify` `toggle`
`strategy` `del` `own` `demo` `live/dry` `elevate` `menu` `quit`

退出码：`0` 全部成功 · `1` 存在失败项 · `3` 参数错误

---

## 日志颜色规范

| 颜色 | 等级 | 含义 |
|------|------|------|
| 🟢 绿 | `[ OK ]` | 限制 / 解除 **成功** |
| 🔴 红 | `[ FAIL ]` | 限制 / 解除 **失败** |
| 🟡 黄 | `[ WARN ]` | 警告、跳过、**进行中**、部分失败 |
| 🔵 青 | `[ INFO ]` | 一般信息 |
| 🟣 紫 | `[ >>> ]` | 阶段推进 |
| ⚪ 灰 | | 分隔线等辅助信息 |

GUI 日志面板与 CMD 输出使用**同一套颜色规则**，两处完全一致。

---

## 工作原理

```
用户选择目标
     ↓
构造 icacls DENY 规则    (域\用户:(RX|W|R|F))
     ↓
写入 NTFS ACL            ← 系统级生效，非应用层拦截
     ↓
验证：读取目标 → 系统返回 "拒绝访问"
```

1. **不删除原有权限**，而是叠加一条 `DENY` ACE —— 因此可逆、可审计
2. 同时支持对当前用户与 `Everyone` 设置，避免绕过
3. 解除时用 `/remove:d` 精确移除自己写入的 DENY 记录
4. ACL 被破坏时，可用「接管所有权」(`takeown` + `icacls /reset`) 修复

工程细节：

- `icacls` 输出是 **OEM 代码页**（中文系统为 GBK），代码里按
  `GetOEMCP()` 主动解码，避免中文错误信息乱码
- 判定成功以 **进程退出码** 为准，而非匹配输出文本
- 所有外部调用使用 `CREATE_NO_WINDOW`，不弹黑框
- 日志同时写入 `%APPDATA%\Games8ThTeam\AppGuard\appguard.log`
- **控制台编码兼容**：日志符号只用 GBK 可编码字符（如 `√`/`×` 而非
  `✔`/`✘`），并给 stdout 设置 `errors="replace"` —— 否则中文 Windows
  下输出会抛 `UnicodeEncodeError` 直接崩溃
- 界面 LOGO 按字体真实度量（`linespace`/`measure`）排版并自动定尺，
  高 DPI 缩放下不会重叠或裁切

---

## 文件位置

| 内容 | 路径 |
|------|------|
| 配置 | `%APPDATA%\Games8ThTeam\AppGuard\appguard.config.json` |
| 日志 | `%APPDATA%\Games8ThTeam\AppGuard\appguard.log` |
| 配置备份 | `%APPDATA%\Games8ThTeam\AppGuard\backups\` |

---

## 项目结构

```
Games8ThTeam-AppGuard/
├─ dist/                        # 打包好的 EXE（免安装直接运行）
│  ├─ Games8Th.Team-AppGuard.exe
│  └─ Games8Th.Team-AppGuard-CLI.exe
├─ docs/界面预览.png
├─ 启动-GUI.bat                 # 源码方式启动 GUI（自动提权）
├─ 启动-CLI.bat                 # 源码方式启动 CLI（自动提权）
├─ build_exe.bat                # 一键重新打包 EXE
├─ README.md
└─ src/
   ├─ launcher.py               # 源码统一入口
   ├─ app_main.py               # 打包入口（GUI）
   ├─ cli_main.py               # 打包入口（CLI）
   ├─ make_logo.py              # 处理官方 LOGO（去黑底→透明、多尺寸）
   ├─ make_icon.py              # 生成应用图标 .ico
   ├─ assets/
   │  ├─ logo_src.png           # 官方 LOGO 原图
   │  ├─ logo_mark.png          # 透明底白色徽标（用于橙色徽标位）
   │  ├─ logo_dark.png          # 透明底深色徽标（用于浅色背景）
   │  ├─ app.ico / app.png      # 应用图标
   │  └─ sized/                 # 预渲染的多尺寸徽标
   └─ g8t/
      ├─ cli.py                # 命令行界面
      ├─ core/
      │  ├─ console.py         # 彩色日志总线 + 文字 LOGO
      │  ├─ guard.py           # ACL 限制引擎
      │  └─ config.py          # 配置持久化
      └─ gui/
         ├─ app.py             # 图形界面
         └─ theme.py           # 亮色主题配色
```

---

## 重新打包 EXE

双击 `build_exe.bat` 即可。它会依次：处理 LOGO 资源 → 生成图标 →
打包 GUI 版 → 打包 CLI 版，产物输出到 `dist\`。

手工打包（等价命令）：

```bat
python -m PyInstaller --noconfirm --onefile --windowed ^
    --name "Games8Th.Team-AppGuard" ^
    --icon "src\assets\app.ico" ^
    --paths "src" --add-data "src\assets;assets" ^
    --collect-submodules g8t ^
    "src\app_main.py"
```

> 需要 `pip install pyinstaller pillow`。

---

## 品牌资源

界面使用 Games8Th.Team 官方 G8 徽标：橙色圆角底 + 透明底白色徽标，
与 `app.ico` 应用图标保持一致的视觉语言。

`make_logo.py` 负责把黑底原图转成透明 PNG —— 按像素亮度提取 alpha 通道，
再裁掉留白、居中到正方形画布，并预渲染 19 种尺寸供界面直接取用
（避免运行时依赖图像库，同时保证任意 DPI 下都清晰）。

---

## 注意事项

- 需要 **Windows 10/11**，Python 3.8+（含 tkinter）
- 对**正在运行**的程序设置 DENY 不会立即踢掉已打开的进程，需重启该程序
- 部分带内核级反作弊的游戏可能抵抗此方式（与 ACL 机制无关）
- UWP / Microsoft Store 应用进程结构特殊，可能不适用
- **请仅在自有或已获授权的设备上使用**

---

## 开发者自检

```bash
python src/launcher.py cli demo     # 沙箱演示，验证 限制→核验→解除 全流程
```

演示模式全程在 `%TEMP%` 下使用临时文件，**不会触碰任何真实系统文件**。

---

## 许可

[MIT License](LICENSE) © 2026 Games8Th.Team

---

*Games8Th.Team · AppGuard v1.0.0*
