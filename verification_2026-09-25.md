# 飞连屏蔽器验证记录

更新时间：2026-09-26

## 运行模式

- 网络限制运行时后端：强制 `Games8thGuard.sys` 驱动。
- 用户态 WFP/Windows 防火墙：仅保留为源码结构与集成审计对象，不是运行时回退。
- VPN 端口记录：`7890`。
- 工具目录检查：`C:\Program Files\GJ` 已检查；未发现可用的 VS/WDK、`signtool.exe` 或 `inf2cat.exe` 完整驱动构建/签名链。

## 用户态构建

- 命令：`cmd.exe /d /c "build.bat <nul"`
- 结果：成功生成 `Games8thBlocker.exe`，退出码 0。
- CLI 提权路径：非管理员启动会请求 UAC，等待提权子进程结束并传回真实退出码；取消或失败返回非零并标记 `UNVERIFIED`。

## 三轮连续审计

每轮同时执行 `audit_wfpengine.ps1`、`audit_g8tguard.ps1` 和 `audit_bsod.ps1`。三套审计任一失败都应修复并从第 1 轮重新开始；本次没有失败，因此以下为连续一组有效轮次。

| 轮次 | WFP/集成 | 驱动静态 | 蓝屏风险 | 结果 |
|---|---:|---:|---:|---|
| 1 | 22/22 | 27/27 | 30/30 | CLEAN |
| 2 | 22/22 | 27/27 | 30/30 | CLEAN |
| 3 | 22/22 | 27/27 | 30/30 | CLEAN |

审计覆盖：WFP 结构、运行时禁止回退、驱动服务生命周期、ALE V4/V6 classify、输入长度、非分页池、锁与 IRP 完成、卸载顺序和可疑蓝屏风险模式。

## 驱动真实加载

- 驱动文件：`driver\build\Release\Games8thGuard.sys`
- SHA-256：`9d1411d75bbc21fd0b332e1d80287ec0c300304301567a0111ff3aa119b87c61`
- Authenticode：`NotSigned`。
- 结论：驱动真实加载为 `UNVERIFIED`。当前 Windows 代码完整性策略不信任该产物，不能声称服务已运行、设备句柄可用或 `QUERY_PATHS` 已响应。
- `driver\load.bat` 会先检查管理员权限和签名，再验证服务 `RUNNING`、设备句柄及 `QUERY_PATHS`。它不会修改 Secure Boot、DSE、测试签名或实施签名绕过。

## 飞连目标

- 当前复核未发现飞连/Feilian 进程、服务、安装目录或目标 EXE。
- 目标限制结果：`UNVERIFIED`。没有真实目标和已验证驱动，不能声称已完成拦截。

## 规则与清理校验

- Windows 防火墙规则代码现在要求目标 EXE 真实存在，并读取应用过滤器确认规则绑定到完整 EXE 路径；仅规则名称存在不会判定成功。
- 强制驱动路径下发后通过 `QUERY_PATHS` 回读确认；清理后也必须回读为空。
- 无法连接驱动、目标不存在、权限不足或 IOCTL 失败均返回非零并标记 `UNVERIFIED`。

## 便携包

- `package_release.ps1` 要求驱动产物存在，并生成 `release\Games8thBlocker-portable`。
- 便携包包含 EXE、驱动、加载/卸载脚本、说明和 `SHA256SUMS.txt`。
- SHA-256 清单在重新打包后逐文件核验，必须以 `HASH_FAILURES=0` 为通过条件。
