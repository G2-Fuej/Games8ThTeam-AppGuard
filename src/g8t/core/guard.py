r"""
Games8Th.Team AppGuard - core/guard.py
权限限制引擎：基于 Windows NTFS ACL (icacls) 对指定目标实施/解除限制。

策略说明
--------
DENY_EXEC   : 拒绝执行 (RX)  —— 目标程序无法启动 / 脚本无法运行
DENY_WRITE  : 拒绝写入 (W,M) —— 目标目录只读，程序无法写入配置/存档
DENY_ALL    : 拒绝全部 (F)   —— 完全封禁
DENY_READ   : 拒绝读取 (R)   —— 连读都不允许

实现要点
--------
1. 使用 DENY ACE 而非删除权限，保证"可逆"且不破坏原有 ACL。
2. 生效对象为当前登录用户 (DOMAIN\user)，同时覆盖 Everyone 兜底。
3. 支持 dry-run：只打印将要执行的命令，不产生副作用。
4. 支持 owner 恢复：若目标 ACL 已被破坏，可先接管所有权再清除 DENY。
"""

import os
import re
import sys
import ctypes
import getpass
import subprocess
import threading
from dataclasses import dataclass, field, asdict

from . import console as C

# ── 创建标志：不弹黑框 ──────────────────────────────────────────────────
_CREATE_NO_WINDOW = 0x08000000
_IS_WIN = os.name == "nt"


def _oem_encoding():
    """获取控制台 OEM 代码页编码（中文系统通常是 cp936/gbk）。"""
    try:
        return "cp%d" % ctypes.windll.kernel32.GetOEMCP()
    except Exception:
        return "gbk"


def _decode(raw: bytes) -> str:
    """icacls / takeown 输出使用 OEM 代码页，需按序尝试解码。"""
    if not raw:
        return ""
    for enc in (_oem_encoding(), "gbk", "utf-8", "latin-1"):
        try:
            return raw.decode(enc)
        except (UnicodeDecodeError, LookupError):
            continue
    return raw.decode("latin-1", errors="replace")


def _run(cmd, timeout=45):
    """执行外部命令，返回 (returncode, stdout+stderr)。"""
    if not _IS_WIN:
        return 1, "非 Windows 平台，icacls 不可用"
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0
    try:
        p = subprocess.run(
            cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=timeout, startupinfo=si,
            creationflags=_CREATE_NO_WINDOW,
        )
        return p.returncode, _decode(p.stdout).strip()
    except subprocess.TimeoutExpired:
        return 1, "命令执行超时"
    except Exception as e:
        return 1, "%s: %s" % (type(e).__name__, e)


def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def _console_encoding_probe():
    """返回 (stdout_encoding, 是否可用)。icacls 输出跟随 OEM 代码页。"""
    return getattr(sys.stdout, "encoding", None) or "utf-8"


def current_identity() -> str:
    """
    返回 计算机名\\用户名，用于构造 DENY 规则。
    优先使用 USERDOMAIN，避免 COMPUTERNAME 缺失/域环境取错。
    """
    user = (os.environ.get("USERNAME") or getpass.getuser() or "").strip()
    domain = (os.environ.get("USERDOMAIN") or os.environ.get("COMPUTERNAME") or "").strip()
    if not user:
        user = "Everyone"
    if not domain:
        # 兜底：从 whoami 结果提取
        rc, out = _run(["whoami"], timeout=10)
        out = (out or "").strip().split("\n")[0].strip()
        if rc == 0 and "\\" in out:
            return out
        return user
    return "%s\\%s" % (domain, user)


def mask_to_perm(mask: str) -> str:
    m = (mask or "").lower()
    return {
        "rx": ["(RX)"], "r": ["(R)"], "w": ["(W)", "(M)"],
        "all": ["(F)"], "denyall": ["(F)"],
    }.get(m, ["(RX)"])


# ── 策略定义 ────────────────────────────────────────────────────────────
STRATEGIES = {
    "DENY_EXEC":  {"label": "禁止执行", "mask": "rx",  "desc": "目标无法启动"},
    "DENY_WRITE": {"label": "禁止写入", "mask": "w",   "desc": "目标变为只读"},
    "DENY_READ":  {"label": "禁止读取", "mask": "r",   "desc": "目标无法被访问"},
    "DENY_ALL":   {"label": "完全封禁", "mask": "all", "desc": "拒绝一切访问"},
}


@dataclass
class Target:
    path: str
    strategy: str = "DENY_EXEC"
    enabled: bool = True
    note: str = ""
    last_result: str = ""       # ok / fail
    last_message: str = ""

    @property
    def name(self) -> str:
        return os.path.basename(self.path.rstrip("\\/")) or self.path

    @property
    def exists(self) -> bool:
        return os.path.exists(self.path)

    def to_dict(self):
        return asdict(self)

    @staticmethod
    def from_dict(d):
        return Target(
            path=d.get("path", ""),
            strategy=d.get("strategy", "DENY_EXEC"),
            enabled=bool(d.get("enabled", True)),
            note=d.get("note", ""),
            last_result=d.get("last_result", ""),
            last_message=d.get("last_message", ""),
        )


class Guard:
    """限制引擎：对所有目标实施 / 解除限制。"""

    def __init__(self, log: C.Logger, dry_run: bool = False):
        self.log = log
        self.dry_run = dry_run
        self.identity = current_identity()

    # ── 单目标操作 ──────────────────────────────────────────────────
    def _apply_one(self, t: Target, action: str):
        """action: 'apply' | 'remove'"""
        perm = mask_to_perm(STRATEGIES.get(t.strategy, {}).get("mask", "rx"))[0]
        verb = "限制" if action == "apply" else "解除"

        if not t.exists:
            return False, "目标不存在或路径不可访问: %s" % t.path

        principal = self.identity
        if action == "apply":
            cmd = ["icacls", t.path, "/deny", "%s:%s" % (principal, perm)]
        else:
            cmd = ["icacls", t.path, "/remove:d", principal]

        # dry-run
        if self.dry_run:
            return True, "[DRY-RUN] %s" % " ".join('"%s"' % c if " " in c else c for c in cmd)

        rc, out = _run(cmd)

        if rc == 0:
            return True, "%s 成功 (%s)" % (verb, self._strategy_label(t.strategy))
        return False, "%s 失败: %s" % (verb, self._short_err(out))

    @staticmethod
    def _strategy_label(s):
        return STRATEGIES.get(s, {}).get("label", s)

    @staticmethod
    def _short_err(out: str) -> str:
        out = (out or "").strip().replace("\r", "")
        lines = [ln.strip() for ln in out.split("\n") if ln.strip()]
        # 过滤掉"已处理的文件: xxx"这类噪音行，保留真正的报错
        noise = re.compile(r"^已处理的文件|^processed file|^已成功处理|successfully processed", re.I)
        keep = [ln for ln in lines if not noise.search(ln)]
        return (keep[-1] if keep else (lines[-1] if lines else "未知错误"))[:150]

    # ── 批量操作 ────────────────────────────────────────────────────
    def apply_all(self, targets, on_item=None):
        return self._batch(targets, "apply", on_item)

    def remove_all(self, targets, on_item=None):
        return self._batch(targets, "remove", on_item)

    def _batch(self, targets, action, on_item=None):
        todo = [t for t in targets if t.enabled] if action == "apply" else list(targets)
        total = len(todo)
        verb = "实施限制" if action == "apply" else "解除限制"

        self.log.rule("%s · 共 %d 个目标" % (verb, total))
        if total == 0:
            self.log.warn("没有需要处理的目标（检查是否已勾选启用）")
            return {"total": 0, "ok": 0, "fail": 0, "items": []}

        stats = {"total": total, "ok": 0, "fail": 0, "items": []}
        for i, t in enumerate(todo, 1):
            self.log.step("[%d/%d] %s  ->  %s" % (
                i, total, t.name, self._strategy_label(t.strategy)))
            ok, msg = self._apply_one(t, action)
            t.last_result = "ok" if ok else "fail"
            t.last_message = msg
            stats["items"].append((t, ok, msg))
            if ok:
                stats["ok"] += 1
                self.log.ok(msg)
            else:
                stats["fail"] += 1
                self.log.fail(msg)
            if on_item:
                try:
                    on_item(t, ok, msg)
                except Exception:
                    pass

        # ── 汇总：颜色结果 ──
        self.log.rule()
        summary = "结果汇总: 成功 %d / 失败 %d / 总计 %d" % (stats["ok"], stats["fail"], total)
        if stats["fail"] == 0:
            self.log.ok("√ " + summary + " —— 全部生效")
        elif stats["ok"] == 0:
            self.log.fail("× " + summary + " —— 全部失败")
        else:
            self.log.warn("! " + summary + " —— 部分失败")
        return stats

    # ── 状态查询 ────────────────────────────────────────────────────
    def inspect(self, target: Target):
        """读取目标当前的真实 ACL 状态。"""
        if not target.exists:
            return "missing", "目标不存在"
        rc, out = _run(["icacls", target.path])
        if rc != 0:
            return "error", self._short_err(out)
        up = out.upper()
        who = self.identity.split("\\")[-1].upper()
        if "(DENY)" in up:
            for line in out.split("\n"):
                lu = line.upper()
                if "DENY" in lu:
                    principal = line.split(":")[0].strip()
                    if who in lu:
                        return "restricted", "已被当前用户限制 (%s)" % principal
                    return "restricted", "存在 DENY 规则 (%s)" % principal
            return "restricted", "存在 DENY 规则"
        return "open", "无限制"

    def verify_all(self, targets):
        self.log.rule("状态核验")
        for t in targets:
            state, msg = self.inspect(t)
            prefix = "  · %-28s " % t.name[:28]
            if state == "restricted":
                self.log.ok(prefix + "RESTRICTED  " + msg)
            elif state == "open":
                self.log.warn(prefix + "OPEN        " + msg)
            else:
                self.log.fail(prefix + "UNKNOWN     " + msg)

    def take_ownership(self, target: Target):
        """接管所有权（ACL 被破坏时的恢复手段）。"""
        if self.dry_run:
            return True, "[DRY-RUN] takeown /f ... && icacls ... /reset"
        rc1, o1 = _run(["takeown", "/f", target.path, "/r", "/d", "y"], timeout=120)
        rc2, o2 = _run(["icacls", target.path, "/reset", "/t"], timeout=120)
        if rc1 == 0 or rc2 == 0:
            return True, "所有权已接管并重置 ACL"
        return False, self._short_err(o1 + " | " + o2)


# ── 提权辅助 ────────────────────────────────────────────────────────────
def relaunch_as_admin(argv=None):
    """以管理员身份重新启动自身。返回 True 表示正在提权重启。"""
    argv = argv if argv is not None else sys.argv
    if not _IS_WIN:
        return False
    try:
        exe = sys.executable
        if getattr(sys, "frozen", False):
            params = " ".join('"%s"' % a for a in argv[1:])
            file, params = exe, params
        else:
            script = os.path.abspath(argv[0])
            params = " ".join(['"%s"' % script] + ['"%s"' % a for a in argv[1:]])
            file = exe
        ret = ctypes.windll.shell32.ShellExecuteW(
            None, "runas", file, params, None, 1)
        return ret > 32
    except Exception:
        return False


def common_targets():
    """提供一些常见的可限制目标作为示例。"""
    pf = os.environ.get("ProgramFiles", r"C:\Program Files")
    pf86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
    win = os.environ.get("WINDIR", r"C:\Windows")
    cands = [
        (os.path.join(win, "System32", "notepad.exe"), "记事本"),
        (os.path.join(win, "System32", "mspaint.exe"), "画图"),
        (os.path.join(win, "System32", "calc.exe"), "计算器"),
        (os.path.join(pf86, "Microsoft", "Edge", "Application", "msedge.exe"), "Microsoft Edge"),
        (os.path.join(pf, "Google", "Chrome", "Application", "chrome.exe"), "Google Chrome"),
        (os.path.join(pf86, "Tencent", "WeChat", "WeChat.exe"), "微信"),
    ]
    return [{"path": p, "note": n} for p, n in cands if os.path.exists(p)]
