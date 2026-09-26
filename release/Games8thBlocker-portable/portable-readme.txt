Games8Th.Team 飞连专用 CLI 便携版（强制驱动模式）

1. 运行 start-Games8thBlocker.bat 或直接运行 Games8Th.Team-Feilian-CLI.exe。
2. 启动先显示 Games8Th.Team 标识 2 秒，再自动检测飞连路径、进程和服务。
3. 程序只对实际发现的飞连目标实施屏蔽，并使用 Games8thGuard.sys；驱动未通过服务、设备和
   QUERY_PATHS 三项校验时，结果为 UNVERIFIED，不会回退到 WFP/防火墙。
4. driver\Games8thGuard.sys 必须有当前 Windows 策略信任的有效签名。
   便携包不包含签名绕过，也不会修改 Secure Boot、DSE 或测试签名设置。
5. 需要清理时，运行 Games8Th.Team-Feilian-CLI.exe cli clear。
6. 跳过飞连检测直接测试指定软件：
   Games8Th.Team-Feilian-CLI.exe cli test "C:\Path\Target.exe"
   目标必须是当前存在的 EXE；只有驱动下发和 QUERY_PATHS 完整路径回读均成功才报告 OK。

命令行：
  Games8Th.Team-Feilian-CLI.exe
  Games8Th.Team-Feilian-CLI.exe cli auto
  Games8Th.Team-Feilian-CLI.exe cli driver-status
  Games8Th.Team-Feilian-CLI.exe cli list
  Games8Th.Team-Feilian-CLI.exe cli clear
  Games8Th.Team-Feilian-CLI.exe cli test "C:\Path\Target.exe"
