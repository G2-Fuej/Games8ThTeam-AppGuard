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
