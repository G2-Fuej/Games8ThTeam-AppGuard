# 飞连屏蔽插件

Windows 软件限制工具的 C# 实现，包含用户态 WFP 回退、Windows 防火墙规则、组件 ACL 和进程守护；内核部分位于 `driver/`。

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

产物位于 `release\Games8thBlocker-portable\`。便携版默认使用用户态
WFP/Windows 防火墙；驱动未加载时会自动回退，不影响普通用户直接使用。
首次启动且没有配置文件时，GUI 会自动发现名称或路径包含“飞连/Feilian”
的进程、服务和常见安装目录；CLI 可直接运行：

```bat
Games8thBlocker.exe cli feilian
```

## 审计与验证

- `audit_g8tguard.ps1`：驱动静态审计，27 项断言。
- `audit_bsod.ps1`：驱动蓝屏风险静态审计，30 项断言。
- `audit_wfpengine.ps1`：WFP 结构与集成审计，21 项断言。
- `package_release.ps1`：生成无需开发环境的便携发布目录和 SHA-256 清单。
- `verification_2026-09-25.md`：本机三轮验证记录。

本次验证中三套审计均连续三轮通过；本机未安装或运行飞连，真实目标拦截结果保持未验证。

驱动测试加载使用 `\??\C:\...` 形式的 NT `ImagePath`。首次启用测试签名后需要重启，且应优先使用 `elevated_load_20260925.ps1` 获取可审计的服务状态和清理结果。
