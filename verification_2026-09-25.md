# 飞连屏蔽器验证记录

更新时间：2026-09-26

## 最终状态

- 产品形态：飞连专用、CLI-only、x64、强制驱动模式。
- 网络后端：Games8thGuard.sys WDM/WFP callout 驱动；无运行时防火墙/WFP 用户态回退。
- VPN/代理测试端口：127.0.0.1:7890。
- 最终驱动：driver\build\Release\Games8thGuard.sys。
- 最终驱动 SHA-256：cfcb98ec34428375e8374721dbbf9b588cb22e93b652f01b9bcf23a531297c97。
- Authenticode：Valid；签名者 CN=科云（上海）信息技术有限公司。
- 交付形式：单 EXE；驱动资源名 Games8thTeamBlocker.Games8thGuard.sys。
- 最终外层 EXE Authenticode：Valid；签名者 CN=科云（上海）信息技术有限公司。
- 最终外层 EXE SHA-256：dcdabdde8ef813d3247a2f71025b071b5c82f8593fc9a9d0057fed1e2e288029。
- 启动 Logo：居中双层边框，金色 G8T 主标、白色 Games8Th.Team、青色产品名，
  并显示 DRIVER MODE / CLI ONLY / X64；展示时间保持 2 秒。
- 驱动释放目录：%ProgramData%\Games8Th.Team\FeilianBlocker。
- 本机真实加载与网络阻断：VERIFIED。
- 最终外层签名完成后的再次管理员加载：当前自动化会话不是管理员且无法启动
  高权限子进程，因此保持 UNVERIFIED；不得用签名前测试伪报为签名后复验。
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
7. 单 EXE 打包拒绝 Authenticode 非 Valid 的驱动，并校验内嵌资源 SHA-256。
8. 将签名驱动嵌入 CLI EXE；运行时执行固定 SHA-256、WinVerifyTrust、SCM
   服务启动及 QUERY_PATHS 四层验证，不再依赖外部 load.bat。

## 用户态构建

- 命令：cmd.exe /d /c "build.bat <nul"
- 结果：PASS，退出码 0。
- 产物：Games8thBlocker.exe。
- 架构：x64 (8664)。
- 子系统：Windows CUI。
- 清单：requireAdministrator。
- 编译输入：src\FeilianCli.cs、src\KernelDriver.cs、src\EmbeddedDriverInstaller.cs；不包含 WinForms GUI、
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

拆分包历史产品链日志：production_driver_e2e_20260926.log。

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

单 EXE 最终产品链日志：production_single_exe_e2e_20260926.log。

    ELEVATED=True
    LOAD_MODE=EMBEDDED_DRIVER_RESOURCE
    cli test C:\Windows\System32\notepad.exe=0
    服务状态=RUNNING
    设备句柄=可用
    QUERY_PATHS=响应正常
    回读路径=\device\harddiskvolume3\windows\system32\notepad.exe
    cli clear=0
    cli unload=0
    PRODUCTION_CHAIN_VERIFIED=True
    FINAL_EXIT=0

卸载后再次核验：服务不存在、服务注册表项不存在、ProgramData 中释放的 SYS
和父目录均不存在。因此本机单 EXE 自释放、加载、IOCTL 和清理链为 VERIFIED。

上述管理员产品链测试发生在外层 EXE 完成 Authenticode 签名之前。签名后复核
确认根目录 EXE 与发布副本哈希完全一致，二者 Authenticode 均为 `Valid`，内嵌
驱动大小为 20592 字节，SHA-256 仍为
`cfcb98ec34428375e8374721dbbf9b588cb22e93b652f01b9bcf23a531297c97`，且其
Authenticode 仍为 `Valid`。由于当前终端为 Medium Integrity，签名后的最终 EXE
尚未完成第二次管理员加载，故该单独检查项明确记为 `UNVERIFIED`。

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

最终有效审计日志：audit_3rounds_single_exe_20260926.log。任一轮失败均须修复并
从第 1 轮重新开始；最终有效的一组结果如下：

| 轮次 | WFP/集成 | 驱动静态 | 蓝屏风险 | 结果 |
|---|---:|---:|---:|---|
| 1 | 29/29 | 32/32 | 32/32 | CLEAN |
| 2 | 29/29 | 32/32 | 32/32 | CLEAN |
| 3 | 29/29 | 32/32 | 32/32 | CLEAN |

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

## 单 EXE 发布

正式发布先构建并手动签名最终 EXE，然后运行
`package_release.ps1 -SkipBuild`。脚本会：

1. 拒绝未签名或签名无效的最终外层 EXE；
2. 拒绝未签名/签名无效驱动；
3. 检查 EXE 包含 Games8thTeamBlocker.Games8thGuard.sys；
4. 解出资源并复核 SHA-256 和 Authenticode；
5. 生成 release\Games8Th.Team-Feilian-CLI.exe 及其 SHA-256 文本。

Windows 内核驱动必须以文件路径交给 SCM，因此运行时仍会将内嵌 SYS 安全释放
到 ProgramData；这不要求用户另外携带驱动文件。`cli unload` 会停止并删除服务，
随后删除释放文件。当前外层 EXE 和内嵌 SYS 的本机 Authenticode 状态均为 Valid。

## 2026-09-26 CorpLink 专属发现修订

用户确认飞连固定安装在 `C:\Program Files\CorpLink`。CLI 发现逻辑已收紧为：

1. 仅以 `C:\Program Files\CorpLink` 为安装根目录；
2. 递归收集该目录下真实存在的 `.exe`，不再把目录本身下发给驱动；
3. 进程、服务和卸载注册表发现的路径必须位于该根目录内；
4. 使用目录分隔边界判断，`CorpLink2`、Program Files (x86) 或其他目录不会被接受；
5. 拒绝目录和 EXE 重解析点，避免链接越过固定根目录；
6. 服务路径解析兼容未加引号的 `C:\Program Files\CorpLink\...\x.exe -arg`。

本机环境检查：

    C:\Program Files\CorpLink=False
    DISCOVERED_TARGET_COUNT=0
    TERMINAL_ADMIN=False
    REAL_FEILIAN_RUNTIME=UNVERIFIED

因此本次不能声称真实飞连发现、驱动加载或网络阻断已在新候选上完成。历史签名
版本的管理员加载与 7890 端到端证据仍保留，但它们不包含本次 CorpLink 专属发现
修改，不能冒充本次运行验证。

临时 x64 单 EXE 编译已通过，使用与 `build.bat` 相同的三个源码输入和内嵌签名
驱动资源，未覆盖仓库中的上一版已签名 EXE。路径边界反射测试结果：

    C:\Program Files\CorpLink => True
    C:\Program Files\CorpLink\client.exe => True
    C:\Program Files\CorpLink2\client.exe => False
    C:\Program Files (x86)\CorpLink\client.exe => False
    C:\Program Files\Other\client.exe => False

本次修改后重新从第 1 轮开始连续执行三轮审计：

| 轮次 | WFP/集成 | 驱动静态 | 蓝屏风险 | 结果 |
|---|---:|---:|---:|---|
| 1 | 34/34 | 32/32 | 32/32 | CLEAN |
| 2 | 34/34 | 32/32 | 32/32 | CLEAN |
| 3 | 34/34 | 32/32 | 32/32 | CLEAN |

新增 WFP/集成断言覆盖 CorpLink 固定根目录、路径边界、EXE-only 目标、进程/服务/
注册表统一约束以及重解析点防护。CorpLink 根目录在本机不存在，因此真实飞连
实例发现及屏蔽仍为 `UNVERIFIED`。

## 2026-09-26 签名后管理员驱动链验证

用户完成外层 EXE 签名后，本机 `Get-AuthenticodeSignature` 对正式候选、发布 EXE
和根目录 EXE 均返回 `Valid`，签名者为科云（上海）信息技术有限公司。三者
SHA-256 一致：

    12624E33EE2E47B3578717F920E4532734EAA03B5D85A8B4265DC94678628267

`package_release.ps1 -SkipBuild` 已生成正式 EXE 和 SHA-256 文件。
`production_corplink_signed_e2e_20260926.log` 记录了管理员上下文测试：

- `ELEVATED=True`，加载模式为内嵌驱动资源；
- 服务进入 `RUNNING`，设备句柄可用，`QUERY_PATHS` 正常；
- `C:\Windows\System32\notepad.exe` 路径写入后通过查询回读；
- `cli list` 显示路径，随后 `cli clear` 回读为空，`cli unload` 清理服务及释放文件；
- 所有命令退出码为 0，日志记录 `PRODUCTION_CHAIN_VERIFIED=True`。

因此已真实验证本机管理员驱动加载、设备通信和路径规则生命周期；Notepad 是测试
目标，不代表真实飞连网络阻断。`C:\Program Files\CorpLink` 不存在，真实飞连
目标结果继续标记为 `UNVERIFIED`。未执行证书绕过、DSE 修改或 Secure Boot 修改。
