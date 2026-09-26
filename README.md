# 飞连屏蔽插件

飞连专用 Windows CLI 工具。启动后显示居中的 Games8Th.Team / G8T
双色终端标识 2 秒，自动
发现飞连进程、服务、安装目录和组件，并针对真实路径实施屏蔽。网络限制强制使用 `Games8thGuard.sys`
内核驱动；用户态 WFP 和 Windows 防火墙代码仅用于结构/兼容性审计，
不会编入 CLI 产品，也不会作为运行时回退。

## 构建

```bat
build.bat
```

驱动需要 WDK + MSVC。`driver/build.bat` 会从项目根目录调用工具链。

## 单 EXE 发布

用户无需安装 Python、WDK、MSVC 或其他开发工具。正式发布顺序为：先运行
`build.bat`，对最终 `Games8thBlocker.exe` 完成 Authenticode 签名，再运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\package_release.ps1 -SkipBuild
```

产物为 `release\Games8Th.Team-Feilian-CLI.exe`。签名驱动作为程序集资源
直接嵌入 EXE，不再依赖外部 `driver` 目录、`load.bat` 或其他运行文件。
程序启动后校验内嵌驱动固定 SHA-256，并通过 `WinVerifyTrust` 验证签名，
再释放到 `%ProgramData%\Games8Th.Team\FeilianBlocker\Games8thGuard.sys`。
程序在驱动服务、设备句柄和 `QUERY_PATHS`
三项均验证成功前不会写入网络限制，也不会回退到 WFP/Windows 防火墙。
驱动和外层 EXE 都必须具有本机验证为 `Valid` 的 Authenticode 签名。打包脚本会
拒绝签名无效的 EXE/驱动，并复核 EXE 内资源 SHA-256。发布包
不能绕过 Secure Boot、DSE、WDAC/HVCI 或签名策略；其他机器未实际加载前
仍应标记为 `UNVERIFIED`。
程序仅保留 CLI 入口；无参数、`cli` 或 `cli auto` 都执行自动飞连检测和针对性屏蔽：

```bat
Games8Th.Team-Feilian-CLI.exe
Games8Th.Team-Feilian-CLI.exe cli
Games8Th.Team-Feilian-CLI.exe cli auto
```

跳过飞连发现、直接测试指定软件的强制驱动屏蔽链：

```bat
Games8Th.Team-Feilian-CLI.exe cli test "C:\Path\Target.exe"
```

测试目标必须是当前存在的 `.exe`，且不能是屏蔽器自身。只有驱动服务、
设备句柄、IOCTL、路径下发和 `QUERY_PATHS` 完整路径回读全部成功时才输出
`[OK]`；驱动未签名或无法加载时仍输出 `UNVERIFIED`。测试写入的路径可用
`cli clear` 清理。

卸载驱动服务并删除释放的 SYS：

```bat
Games8Th.Team-Feilian-CLI.exe cli unload
```

## 审计与验证

- `audit_g8tguard.ps1`：驱动静态审计，32 项断言。
- `audit_bsod.ps1`：驱动蓝屏风险静态审计，32 项断言。
- `audit_wfpengine.ps1`：WFP 结构、ALE_APP_ID、单 EXE 内嵌驱动、CLI-only x64 构建及“禁止运行时回退”集成审计，29 项断言。
- `package_release.ps1`：生成单 EXE 和对应 SHA-256 文本。
- `verification_2026-09-25.md`：本机三轮验证记录。

本次验证中三套审计已连续三轮通过。最终签名驱动已在本机完成真实加载、
设备/IOCTL、正式服务链和 `127.0.0.1:7890` 端到端阻断验证；本机未安装或
运行飞连，因此真实飞连目标结果仍为 `UNVERIFIED`。

EXE 清单使用 `requireAdministrator`。内置加载器验证资源哈希和 Authenticode、
创建并启动服务，再通过设备句柄和 `QUERY_PATHS` 验证实际可用性。
程序不会修改测试签名、Secure Boot 或其他代码完整性设置。当前最终外层 EXE
Authenticode 状态为 `Valid`，签名者为科云（上海）信息技术有限公司；最终
SHA-256 为 `DCDABDDE8EF813D3247A2F71025B071B5C82F8593FC9A9D0057FED1E2E288029`。
内嵌 SYS 的 Authenticode 状态同样为 `Valid`。这些结果只证明本机签名校验；
其他机器的代码完整性、证书信任和撤销策略仍须实际加载验证。
