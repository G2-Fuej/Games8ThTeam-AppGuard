# 飞连屏蔽插件

飞连专用 Windows CLI 工具。启动后显示 Games8Th.Team 标识 2 秒，自动
发现飞连进程、服务、安装目录和组件，并针对真实路径实施屏蔽。网络限制强制使用 `Games8thGuard.sys`
内核驱动；用户态 WFP 和 Windows 防火墙代码仅用于结构/兼容性审计，
不会编入 CLI 产品，也不会作为运行时回退。

## 构建

```bat
build.bat
```

驱动需要 WDK + MSVC。`driver/build.bat` 会从项目根目录调用工具链。

## 便携发布

用户无需安装 Python、WDK、MSVC 或其他开发工具。管理员运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\package_release.ps1
```

产物位于 `release\Games8thBlocker-portable\`。便携版主程序名为
`Games8Th.Team-Feilian-CLI.exe`，并包含驱动、
提权/加载脚本和校验清单。程序在驱动服务、设备句柄和 `QUERY_PATHS`
三项均验证成功前不会写入网络限制，也不会回退到 WFP/Windows 防火墙。
驱动必须具有被当前 Windows 代码完整性策略信任的有效签名；当前仓库中的
历史 `Games8thGuard.sys` 若显示 `NotSigned`，加载结果必须标记为
`UNVERIFIED`。发布包本身不能绕过 Secure Boot、DSE 或签名策略。
程序仅保留 CLI 入口；无参数、`cli` 或 `cli auto` 都执行自动飞连检测和针对性屏蔽：

```bat
Games8Th.Team-Feilian-CLI.exe
Games8Th.Team-Feilian-CLI.exe cli
Games8Th.Team-Feilian-CLI.exe cli auto
```

## 审计与验证

- `audit_g8tguard.ps1`：驱动静态审计，27 项断言。
- `audit_bsod.ps1`：驱动蓝屏风险静态审计，30 项断言。
- `audit_wfpengine.ps1`：WFP 结构、CLI-only 构建、自身排除及“禁止运行时回退”集成审计，26 项断言。
- `package_release.ps1`：生成无需开发环境的便携发布目录和 SHA-256 清单。
- `verification_2026-09-25.md`：本机三轮验证记录。

本次验证中三套审计需要连续三轮通过；本机未安装或运行飞连时，
真实目标拦截结果保持 `UNVERIFIED`，不能用静态审计代替运行时证据。

`driver\load.bat` 会自动请求管理员权限、验证 Authenticode、创建服务、
启动服务，并调用 `cli driver-status` 验证设备句柄和 `QUERY_PATHS`。
脚本不会修改测试签名、Secure Boot 或其他代码完整性设置。
