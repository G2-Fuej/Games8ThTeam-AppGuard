# 飞连屏蔽器验证记录

更新时间：2026-09-26

## 最终状态

- 产品形态：飞连专用、CLI-only、x64、强制驱动模式。
- 网络后端：Games8thGuard.sys WDM/WFP callout 驱动；无运行时防火墙/WFP 用户态回退。
- VPN/代理测试端口：127.0.0.1:7890。
- 最终驱动：driver\build\Release\Games8thGuard.sys。
- 最终驱动 SHA-256：cfcb98ec34428375e8374721dbbf9b588cb22e93b652f01b9bcf23a531297c97。
- Authenticode：Valid；签名者 CN=科云（上海）信息技术有限公司。
- 本机真实加载与网络阻断：VERIFIED。
- 真实飞连目标：本机未发现，因此飞连目标实测保持 UNVERIFIED。

## 修复内容

1. FWPS_CALLOUT1.notifyFn 不再为 NULL，修复实际加载返回
   STATUS_FWP_NULL_POINTER (0xC022001C)。
2. 新增专用 G8T_SUBLAYER，并用 WFP transaction 原子添加 sublayer、
   callout 和 filter；失败路径执行 FwpmTransactionAbort0。
3. 用户态调用 FwpmGetAppIdFromFileName0，把 DOS EXE 路径转换成 WFP
   ALE_APP_ID。此前 IOCTL 回读虽成功，但 classify 元数据是设备路径，导致
   真实连接未命中；修复后 7890 连接已真实阻断。
4. CLI 明确构建为 x64，启用警告即错误。
5. 驱动构建启用 /GS、/Qspectre、CFG、ASLR、NX、/W4 /WX，并链接
   bufferoverflowfastfailk.lib。
6. 正式驱动构建只输出 Games8thGuard-unsigned.sys，不会覆盖已签名发布件。
7. 便携打包拒绝 Authenticode 非 Valid 的驱动，并校验复制前后 SHA-256。

## 用户态构建

- 命令：cmd.exe /d /c "build.bat <nul"
- 结果：PASS，退出码 0。
- 产物：Games8thBlocker.exe。
- 架构：x64 (8664)。
- 子系统：Windows CUI。
- 清单：requireAdministrator。
- 编译输入：src\FeilianCli.cs、src\KernelDriver.cs；不包含 WinForms GUI、
  src\Program.cs 或 src\WfpEngine.cs。

## 驱动真实加载与 IOCTL 验证

管理员原生加载日志：native_driver_final_hardened_elevated_20260926.log。

    SeLoadDriverPrivilege=True
    NtLoadDriver=0x00000000
    CreateFile(\\.\G8TGuard)=成功
    QUERY_PATHS=成功
    路径写入=成功
    精确路径回读=成功
    路径移除=成功
    最终清空=成功
    NtUnloadDriver=0x00000000
    临时注册表项已删除=True

正式产品链日志：production_driver_e2e_20260926.log。

    driver\load.bat=0
    Games8thGuard 服务=RUNNING
    cli driver-status=VERIFIED
    cli clear=0，QUERY_PATHS 回读为空
    driver\unload.bat=0
    最终服务/注册表残留=无

跳过飞连检测的正式测试入口日志：production_driver_direct_test_20260926.log。

    cli test C:\Windows\System32\notepad.exe=0
    cli list=0
    回读路径=\device\harddiskvolume3\windows\system32\notepad.exe
    cli clear=0
    driver\unload.bat=0
    最终服务/注册表残留=无

## 7890 端到端网络阻断

日志：native_driver_network_block_appid_elevated_20260926.log。

    BASELINE_CONNECT=True
    PATH_BIND=True
    BLOCKED_CONNECT=False
    RESTORED_CONNECT=True
    NETWORK_BLOCK_VERIFIED=True

测试使用独立 x64 客户端连接 127.0.0.1:7890：写入客户端的 WFP
ALE_APP_ID 前连接成功；写入后新连接收到套接字访问拒绝；清空驱动路径后连接恢复。
这证明最终结果不只是“规则/服务/路径名称存在”，而是 ALE V4 连接实际被阻断。

## 三轮连续审计

最终有效审计日志：audit_3rounds_release_20260926.log。任一轮失败均须修复并
从第 1 轮重新开始；最终有效的一组结果如下：

| 轮次 | WFP/集成 | 驱动静态 | 蓝屏风险 | 结果 |
|---|---:|---:|---:|---|
| 1 | 28/28 | 32/32 | 32/32 | CLEAN |
| 2 | 28/28 | 32/32 | 32/32 | CLEAN |
| 3 | 28/28 | 32/32 | 32/32 | CLEAN |

新增覆盖包括：非空 notify callback、自定义 sublayer、WFP transaction/abort、
ALE_APP_ID 转换、CFG/ASLR/NX、签名件防覆盖及 x64 CLI-only 构建。

## 飞连目标

2026-09-26 复核本机进程、服务及常见安装目录，结果为：

    FEILIAN_TARGETS=0
    FEILIAN_RUNTIME_RESULT=UNVERIFIED

因此不能声称已对真实飞连实例完成拦截。当前已真实验证的是驱动加载、IOCTL、
正式服务链和独立测试客户端的 7890 网络阻断。

## 兼容性边界

- 当前签名件已在本机当前 Windows 代码完整性策略下真实加载成功。
- 其他机器能否加载仍取决于其 Secure Boot、WDAC/HVCI、证书信任和撤销检查策略；
  未在目标机器实测时应标记为 UNVERIFIED。
- 工具不会修改测试签名、Secure Boot、DSE、WDAC 或 g_CiOptions。

## 便携包

package_release.ps1 会：

1. 重新构建 x64 CLI；
2. 拒绝未签名/签名无效驱动；
3. 校验 EXE 和驱动复制前后 SHA-256；
4. 生成 release\Games8thBlocker-portable\SHA256SUMS.txt；
5. 对清单执行逐文件复核，要求 HASH_FAILURES=0。
