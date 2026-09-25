# 飞连屏蔽插件

Windows 软件限制工具的 C# 实现，包含用户态 WFP 回退、Windows 防火墙规则、组件 ACL 和进程守护；内核部分位于 `driver/`。

## 构建

```bat
build.bat
```

驱动需要 WDK + MSVC。`driver/build.bat` 会从项目根目录调用工具链。

## 审计与验证

- `audit_g8tguard.ps1`：驱动静态审计，27 项断言。
- `audit_wfpengine.ps1`：WFP 结构与集成审计，21 项断言。
- `verification_2026-09-25.md`：本机三轮验证记录。

本次验证中两套审计均连续三轮通过；本机未安装或运行飞连，真实目标拦截结果保持未验证。
