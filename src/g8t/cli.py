"""
Games8Th.Team AppGuard - cli.py
命令行界面：交互式控制台 + 子命令，带彩色日志与文字 LOGO。

用法
----
  python -m g8t.cli                 # 进入交互式控制台（推荐）
  python -m g8t.cli list            # 列出目标
  python -m g8t.cli add <路径>       # 添加目标
  python -m g8t.cli remove <序号|名称>
  python -m g8t.cli apply           # 实施限制
  python -m g8t.cli clear           # 解除限制
  python -m g8t.cli verify          # 核验当前状态
  python -m g8t.cli demo            # 运行演示（安全，使用临时文件）
"""

import os
import sys
import argparse

from .core import console as C
from .core import config as CFG
from .core.guard import (
    Guard, Target, STRATEGIES, is_admin, relaunch_as_admin,
    common_targets, current_identity,
)


# ────────────────────────────────────────────────────────────────────────
#  表格 / 列表渲染
# ────────────────────────────────────────────────────────────────────────
def render_list(targets, guard: Guard, check_state=False):
    log = guard.log
    if not targets:
        log.warn("目标列表为空，使用 add 命令添加，或执行 demo 体验流程")
        return
    log.rule("目标列表 · 共 %d 项" % len(targets))
    head = "  %-4s %-9s %-11s %-26s %s" % ("#", "启用", "策略", "目标名称", "路径")
    log.emit(C.DIM, head)
    for i, t in enumerate(targets, 1):
        st = STRATEGIES.get(t.strategy, {})
        flag = "[x]" if t.enabled else "[ ]"
        line = "  %-4d %-9s %-11s %-26s %s" % (
            i, flag, st.get("label", t.strategy), t.name[:26], t.path)
        log.emit(C.INFO, line)
        if check_state:
            state, msg = guard.inspect(t)
            lvl = {"restricted": C.OK, "open": C.WARN}.get(state, C.FAIL)
            tagm = {"restricted": "已限制", "open": "未限制", "missing": "不存在"}.get(state, "异常")
            log.emit(lvl, "         └─ 当前状态: %s · %s" % (tagm, msg))


# ────────────────────────────────────────────────────────────────────────
#  交互式控制台
# ────────────────────────────────────────────────────────────────────────
MENU = """
  ┌─ 操作菜单 ───────────────────────────────────────────────┐
  │  1 / add     添加目标（文件或文件夹）                     │
  │  2 / apply   实施限制      ← 绿=成功 红=失败              │
  │  3 / clear   解除限制                                    │
  │  4 / list    查看目标列表                                 │
  │  5 / verify  核验当前限制状态                             │
  │  6 / toggle  启用/停用某目标                              │
  │  7 / strategy 修改某目标的限制策略                        │
  │  8 / del     删除某目标（仅从列表移除，不动系统权限）     │
  │  9 / own     接管所有权（ACL 损坏时恢复用）               │
  │  d / demo    运行演示（使用临时文件，绝对安全）           │
  │  m / menu    重新显示菜单                                 │
  │  q / quit    退出                                         │
  └──────────────────────────────────────────────────────────┘"""


def interactive():
    log = C.configure(log_file=CFG.log_path())
    admin = is_admin()

    cfg, targets = CFG.load_targets()
    dry = bool(cfg.get("dry_run_default", False))

    C.banner(admin=admin, dry_run=dry)
    log.info("配置文件: %s" % CFG.config_path())
    log.info("日志文件: %s" % CFG.log_path())
    log.info("当前身份: %s" % current_identity())

    if not admin:
        log.warn("未以管理员身份运行 —— 对系统目录/Program Files 下的目标可能限制失败")
        log.warn("提示: 可用菜单 9 或启动参数 --elevate 以管理员身份重启")

    if not cfg.get("targets"):
        log.info("首次运行，注入常见示例目标（可自行删除，均为未启用状态）")
        for c in common_targets():
            targets.append(Target(path=c["path"], note=c["note"], strategy="DENY_EXEC", enabled=False))
        CFG.save_targets(targets, cfg)

    log.ok("%s 就绪，共加载 %d 个目标" % (C.BRAND_NAME, len(targets)))
    print(MENU)

    guard = Guard(log, dry_run=dry)

    def pick(prompt="请输入序号: "):
        try:
            raw = input(prompt).strip()
        except (EOFError, KeyboardInterrupt):
            return None, None
        if not raw:
            return None, None
        if raw.isdigit():
            i = int(raw)
            if 1 <= i <= len(targets):
                return i, targets[i - 1]
            log.fail("序号超出范围 (1-%d)" % len(targets))
            return None, None
        low = raw.lower()
        hits = [t for t in targets if low in t.name.lower() or low in t.path.lower()]
        if len(hits) == 1:
            return targets.index(hits[0]) + 1, hits[0]
        if not hits:
            log.fail("未找到匹配的目标: %s" % raw)
        else:
            log.warn("匹配到 %d 个目标，请输入序号" % len(hits))
        return None, None

    def sync():
        cfg["targets"] = [t.to_dict() for t in targets]
        if CFG.save(cfg):
            return True
        log.fail("配置保存失败")
        return False

    while True:
        try:
            raw = input("\n%s > " % C.BRAND_NAME).strip()
        except (EOFError, KeyboardInterrupt):
            print()
            log.info("已退出")
            break
        if not raw:
            continue
        cmd, _, arg = raw.partition(" ")
        cmd = cmd.lower()
        arg = arg.strip()

        # ── 1 添加 ──
        if cmd in ("1", "add"):
            path = arg or input("请输入目标完整路径（文件或文件夹）: ").strip().strip('"')
            if not path:
                log.fail("路径为空，已取消")
                continue
            path = os.path.abspath(path)
            if not os.path.exists(path):
                log.fail("路径不存在: %s" % path)
                continue
            if any(os.path.normcase(t.path) == os.path.normcase(path) for t in targets):
                log.warn("该目标已在列表中")
                continue
            log.info("选择限制策略:")
            for k, v in STRATEGIES.items():
                log.emit(C.INFO, "    %-11s %s  (%s)" % (k, v["label"], v["desc"]))
            s = (input("策略 [默认 DENY_EXEC]: ").strip() or "DENY_EXEC").upper()
            if s not in STRATEGIES:
                log.warn("未知策略 %s，回退为 DENY_EXEC" % s)
                s = "DENY_EXEC"
            targets.append(Target(path=path, strategy=s, enabled=True))
            if sync():
                log.ok("已添加目标: %s  ·  策略 %s" % (os.path.basename(path), s))

        # ── 2 实施 ──
        elif cmd in ("2", "apply"):
            if guard.dry_run:
                log.warn("当前为 DRY-RUN 模式，只演示不写入系统。可用 'live' 切换到真实模式")
            log.step("开始对启用中的目标实施限制 ...")
            stats = guard.apply_all(targets)
            sync()
            if stats["fail"] == 0 and stats["total"] > 0:
                log.ok("★ 限制任务完成：全部成功")
            elif stats["total"] > 0:
                log.fail("★ 限制任务完成：存在失败项，请检查上方红色日志")

        # ── 3 解除 ──
        elif cmd in ("3", "clear", "release"):
            log.step("开始解除所有 DENY 规则 ...")
            stats = guard.remove_all(targets)
            sync()
            if stats["fail"] == 0:
                log.ok("★ 解除任务完成：全部成功")
            else:
                log.fail("★ 解除任务完成：存在失败项")

        # ── 4 列表 ──
        elif cmd in ("4", "list", "ls"):
            render_list(targets, guard, check_state=True)

        # ── 5 核验 ──
        elif cmd in ("5", "verify"):
            guard.verify_all(targets)

        # ── 6 启用/停用 ──
        elif cmd in ("6", "toggle"):
            render_list(targets, guard)
            if arg:
                idx = int(arg) if arg.isdigit() else None
                t = targets[idx - 1] if idx and 1 <= idx <= len(targets) else None
            else:
                _, t = pick()
            if t:
                t.enabled = not t.enabled
                sync()
                (log.ok if t.enabled else log.warn)(
                    "%s 已%s" % (t.name, "启用" if t.enabled else "停用"))

        # ── 7 策略 ──
        elif cmd in ("7", "strategy"):
            render_list(targets, guard)
            _, t = pick()
            if t:
                for k, v in STRATEGIES.items():
                    log.emit(C.INFO, "    %-11s %s  (%s)" % (k, v["label"], v["desc"]))
                s = (input("新策略 [当前 %s]: " % t.strategy).strip() or t.strategy).upper()
                if s in STRATEGIES:
                    t.strategy = s
                    sync()
                    log.ok("%s 策略已改为 %s，需重新 apply 生效" % (t.name, s))
                else:
                    log.fail("未知策略: %s" % s)

        # ── 8 删除 ──
        elif cmd in ("8", "del", "delete"):
            render_list(targets, guard)
            idx, t = pick()
            if t:
                targets.remove(t)
                sync()
                log.warn("已从列表移除: %s （系统权限未被修改，如需解除请先执行 clear）" % t.name)

        # ── 9 接管所有权 ──
        elif cmd in ("9", "own"):
            render_list(targets, guard)
            _, t = pick()
            if t:
                ok, msg = guard.take_ownership(t)
                (log.ok if ok else log.fail)(msg)

        # ── live / dry ──
        elif cmd in ("live", "dry", "dryrun", "dry-run"):
            guard.dry_run = (cmd != "live")
            cfg["dry_run_default"] = guard.dry_run
            CFG.save(cfg)
            (log.warn if guard.dry_run else log.ok)(
                "已切换为 %s 模式" % ("DRY-RUN (不写入系统)" if guard.dry_run else "LIVE (真实生效)"))

        # ── 演示 ──
        elif cmd in ("d", "demo"):
            run_demo(guard)

        # ── 提权 ──
        elif cmd in ("elevate", "admin"):
            if admin:
                log.warn("当前已是管理员")
            elif relaunch_as_admin():
                log.ok("正在以管理员身份重启 ...")
                return
            else:
                log.fail("提权被取消或失败")

        # ── 帮助 / 退出 ──
        elif cmd in ("m", "menu", "help", "h", "?"):
            print(MENU)
        elif cmd in ("q", "quit", "exit"):
            log.info("已退出 %s %s" % (C.BRAND_NAME, C.BRAND_SUB))
            break
        else:
            log.fail("未知命令: %s  （输入 m 查看菜单）" % cmd)


# ────────────────────────────────────────────────────────────────────────
#  演示模式：使用沙箱临时文件，绝不触碰真实系统文件
# ────────────────────────────────────────────────────────────────────────
def run_demo(guard: Guard):
    import tempfile
    import shutil

    log = guard.log
    log.rule("DEMO 演示模式")
    log.info("将在临时目录创建沙箱文件并演示 限制 -> 核验 -> 解除 全流程")
    log.warn("不会触碰任何真实系统文件")

    sandbox = tempfile.mkdtemp(prefix="g8t_demo_")
    files = []
    try:
        for i in range(1, 5):
            p = os.path.join(sandbox, "demo_target_%d.txt" % i)
            with open(p, "w", encoding="utf-8") as f:
                f.write("Games8Th.Team demo file %d\n" % i)
            files.append(Target(path=p, strategy="DENY_EXEC",
                                note="demo", enabled=True))
        log.ok("已创建 %d 个沙箱目标: %s" % (len(files), sandbox))

        log.step("阶段 1/3 · 实施限制")
        stats = guard.apply_all(files)

        log.step("阶段 2/3 · 核验状态")
        for t in files:
            state, msg = guard.inspect(t)
            (log.ok if state == "restricted" else log.warn)("  %s -> %s" % (t.name, msg))

        log.step("阶段 3/3 · 解除限制")
        stats2 = guard.remove_all(files)

        log.rule()
        if stats["fail"] == 0 and stats2["fail"] == 0:
            log.ok("√ DEMO 完成：限制与解除全部成功")
        elif stats["ok"] == 0 and stats2["ok"] == 0:
            log.fail("× DEMO 完成：全部失败，请查看上方红色日志")
        else:
            log.warn("! DEMO 完成：部分步骤失败，请查看上方红色日志")
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)
        log.raw("沙箱已清理: %s" % sandbox)


# ────────────────────────────────────────────────────────────────────────
#  命令行入口
# ────────────────────────────────────────────────────────────────────────
def build_parser():
    p = argparse.ArgumentParser(
        prog="g8t-appguard",
        description="%s %s - Windows 应用权限限制工具" % (C.BRAND_NAME, C.BRAND_SUB),
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("--elevate", action="store_true", help="以管理员身份重新启动")
    p.add_argument("--dry-run", action="store_true", help="只演示，不写入系统")
    p.add_argument("--no-color", action="store_true", help="禁用彩色输出")
    sub = p.add_subparsers(dest="cmd")

    sub.add_parser("list", help="列出所有目标及实时状态")
    a = sub.add_parser("add", help="添加目标"); a.add_argument("path"); a.add_argument("-s", "--strategy", default="DENY_EXEC")
    r = sub.add_parser("remove", help="删除目标"); r.add_argument("key")
    t = sub.add_parser("toggle", help="启用/停用"); t.add_argument("key")
    sub.add_parser("apply", help="对启用目标实施限制")
    sub.add_parser("clear", help="解除全部限制")
    sub.add_parser("verify", help="核验限制状态")
    sub.add_parser("demo", help="运行安全演示")
    sub.add_parser("gui", help="启动图形界面")
    sub.add_parser("targets", help="列出常见可限制目标")
    return p


def resolve(targets, key):
    if key.isdigit():
        i = int(key)
        if 1 <= i <= len(targets):
            return targets[i - 1]
        return None
    hits = [t for t in targets if key.lower() in t.name.lower() or key.lower() in t.path.lower()]
    return hits[0] if len(hits) == 1 else None


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    args = build_parser().parse_args(argv)

    if args.no_color:
        C._ANSI = {k: "" for k in C._ANSI}

    if args.elevate and not is_admin():
        if relaunch_as_admin([sys.argv[0]] + argv):
            print("正在以管理员身份重启 ...")
            return 0

    # 无子命令 -> 交互式控制台
    if not args.cmd:
        interactive()
        return 0

    log = C.configure(log_file=CFG.log_path(), quiet_console=False)
    cfg, targets = CFG.load_targets()
    dry = bool(args.dry_run or cfg.get("dry_run_default", False))
    C.banner(admin=is_admin(), dry_run=dry)
    guard = Guard(log, dry_run=dry)

    cmd = args.cmd

    if cmd == "gui":
        from ..gui.app import launch
        return launch()

    if cmd == "targets":
        log.rule("常见可限制目标")
        for i, c in enumerate(common_targets(), 1):
            log.emit(C.INFO, "  %2d) %-18s %s" % (i, c["note"], c["path"]))
        return 0

    if cmd == "demo":
        run_demo(guard)
        return 0

    if cmd == "list":
        render_list(targets, guard, check_state=True); return 0

    if cmd == "add":
        path = os.path.abspath(args.path)
        if not os.path.exists(path):
            log.fail("路径不存在: %s" % path); return 3
        s = args.strategy.upper()
        if s not in STRATEGIES:
            log.fail("未知策略: %s" % s); return 3
        if any(os.path.normcase(t.path) == os.path.normcase(path) for t in targets):
            log.warn("该目标已存在"); return 0
        targets.append(Target(path=path, strategy=s))
        CFG.save_targets(targets, cfg)
        log.ok("已添加: %s [%s]" % (path, s))
        return 0

    if cmd in ("remove", "toggle"):
        t = resolve(targets, args.key)
        if not t:
            log.fail("未找到匹配目标: %s" % args.key); return 3
        if cmd == "remove":
            targets.remove(t); CFG.save_targets(targets, cfg)
            log.warn("已移除: %s" % t.name)
        else:
            t.enabled = not t.enabled; CFG.save_targets(targets, cfg)
            (log.ok if t.enabled else log.warn)("%s 已%s" % (t.name, "启用" if t.enabled else "停用"))
        return 0

    if cmd == "apply":
        stats = guard.apply_all(targets); CFG.save_targets(targets, cfg)
        return 0 if stats["fail"] == 0 else 1

    if cmd == "clear":
        stats = guard.remove_all(targets); CFG.save_targets(targets, cfg)
        return 0 if stats["fail"] == 0 else 1

    if cmd == "verify":
        guard.verify_all(targets); return 0

    return 0


if __name__ == "__main__":
    sys.exit(main())
