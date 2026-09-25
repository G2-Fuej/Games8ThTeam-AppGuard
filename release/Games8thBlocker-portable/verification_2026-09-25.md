# 飞连屏蔽器验证记录

- 系统：Windows 11 专业版，Build 22631
- 当前 PowerShell：非管理员令牌；内核驱动/WFP 运行时装载未执行
- 构建：`build.bat` 成功生成 `Games8thBlocker.exe`
- 驱动重编译：未执行，机器上缺少 `vcvarsall.bat`/VS Build Tools；已有 `driver/build/Release/Games8thGuard.sys` 作为历史产物

## 三轮目标发现
### 第 1 轮
- 进程：0 个
- 服务：0 个
- 安装文件：0 个
- 结果：未发现飞连目标，运行时限制效果 UNVERIFIED（环境缺少目标）
### 第 2 轮
- 进程：0 个
- 服务：0 个
- 安装文件：0 个
- 结果：未发现飞连目标，运行时限制效果 UNVERIFIED（环境缺少目标）
### 第 3 轮
- 进程：0 个
- 服务：0 个
- 安装文件：0 个
- 结果：未发现飞连目标，运行时限制效果 UNVERIFIED（环境缺少目标）

## 审计结果
- `audit_g8tguard.ps1`：27/27 PASS × 3 轮。
- `audit_wfpengine.ps1`：21/21 PASS × 3 轮。
- 第一次 WFP 审计发现并修复了断言脚本中的 `FWP_BYTE_BLOB_TYPE` 错值（14 → 12），随后从第 1 轮重新开始。

## 结论
代码审计和构建检查已完成三轮且 CLEAN；由于本机未安装/运行飞连，且当前会话不是管理员，无法声称已完成真实目标拦截验证。

## 2026-09-25 追加运行时检查
- 已通过 UAC 启动提权脚本；脚本现在使用仓库内绝对驱动路径，并会校正 `Games8thGuard` 服务的 `ImagePath`。
- KDU 默认 provider 0：驱动资源 ID 不存在，未执行 DSE 修改。
- KDU provider 4（MsIo64）：数据库和 `MsIo` 设备打开成功，但读取 DSE 状态返回 `GetLastError=483`，未完成 DSE 修改。
- KDU provider 22（AsIO3）：现有驱动拒绝打开，返回 `NTSTATUS 0xC0000022`，未完成 DSE 修改。
- 测试签名探测：`bcdedit /set testsigning on` 与清理用的 `off` 均返回 0；本轮不重启系统，因此测试签名没有在当前启动会话生效。
- 标准服务加载测试：服务路径已指向仓库内 `driver\build\Release\Games8thGuard.sys`，启动返回 `0x7B / 123`，随后已删除服务。事件日志记录为 SCM 7000；未得到驱动已加载证据。
- 驱动产物静态检查：PE64、Machine `0x8664`、Native 子系统、MSVC 14.36；Authenticode 状态为 `NotSigned`。
- 目标发现复核：仍未发现飞连进程、服务、安装目录或目标二进制，因此飞连权限限制效果仍为 `UNVERIFIED`。
- 修复服务路径后，使用 `\??\C:\Temp\Games8thGuard.sys` 进行加载探测，错误从路径语法 `0x7B / 123` 变为明确的签名拒绝 `0x241 / 577`；服务随后自动删除，未发生驱动加载或蓝屏。
- WFP 回退实测（管理员上下文，目标为 `C:\Windows\System32\notepad.exe`）：`AVAILABLE=True`、`BLOCK=True`、过滤器存在、`UNBLOCK=True`、过滤器移除成功。
- 蓝屏风险审计 `audit_bsod.ps1`：30/30 PASS × 3；驱动审计 27/27 PASS × 3；WFP 审计 21/21 PASS × 3。三轮均从第 1 轮重新计数且无失败。

## 2026-09-25 便携发布复核
- 用户态源码重新编译成功：`Games8thBlocker.exe`。
- GUI 在无配置文件时自动搜索飞连/Feilian 进程、服务和常见安装目录；CLI 新增 `cli feilian`。
- CLI 网络封锁优先使用用户态 WFP，WFP 不可用时回退 Windows 防火墙；GUI 核验和清理覆盖已配置目标的 WFP 过滤器。
- `package_release.ps1 -SkipBuild` 成功生成 `release\Games8thBlocker-portable`，包含 EXE、资源、可选驱动、启动脚本、说明和 `SHA256SUMS.txt`。
- 便携包 SHA-256 校验：`HASH_FAILURES=0`；发布文件名 ASCII 检查通过。
- 本机仍未发现飞连目标，且没有 WDK/MSVC 工具链；驱动重新编译、签名绕过和真实飞连拦截保持 `UNVERIFIED`。普通用户路径不依赖驱动。
