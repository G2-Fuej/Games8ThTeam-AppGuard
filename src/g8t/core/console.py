"""
Games8Th.Team AppGuard - core/console.py
彩色日志输出（绿=成功 / 红=失败 / 黄=警告与进行中），中央日志总线。
"""

import os
import sys
import time
import threading
from datetime import datetime

# ── Windows ANSI 支持 ────────────────────────────────────────────────────
try:
    from ctypes import windll
    _K = windll.kernel32
    _K.SetConsoleMode(_K.GetStdHandle(-11), 7)   # ENABLE_VIRTUAL_TERMINAL_PROCESSING
    _ANSI_OK = True
except Exception:
    _ANSI_OK = False


def _make_stream_safe():
    """
    让 stdout/stderr 遇到无法编码的字符时用 '?' 代替，而不是抛异常。

    中文 Windows 控制台默认是 GBK(cp936)，像 '√' 这类符号不在 GBK 字符集里，
    直接 print 会抛 UnicodeEncodeError 并导致整个程序崩溃。
    """
    for name in ("stdout", "stderr"):
        st = getattr(sys, name, None)
        if st is None:
            continue
        try:
            st.reconfigure(errors="replace")
        except Exception:
            pass


_make_stream_safe()


def _safe_print(text: str):
    """兜底输出：任何编码异常都不得中断程序。"""
    try:
        print(text, flush=True)
    except UnicodeEncodeError:
        try:
            enc = getattr(sys.stdout, "encoding", None) or "utf-8"
            sys.stdout.write(text.encode(enc, "replace").decode(enc, "replace") + "\n")
            sys.stdout.flush()
        except Exception:
            try:
                sys.stdout.write(text.encode("ascii", "replace").decode("ascii") + "\n")
                sys.stdout.flush()
            except Exception:
                pass
    except Exception:
        pass


# ── 等级定义 ────────────────────────────────────────────────────────────
OK, FAIL, WARN, INFO, STEP, DIM, BRAND, RESET = (
    "OK", "FAIL", "WARN", "INFO", "STEP", "DIM", "BRAND", "RESET"
)

_ANSI = {
    OK:    "\033[92m",   # 亮绿
    FAIL:  "\033[91m",   # 亮红
    WARN:  "\033[93m",   # 亮黄
    INFO:  "\033[96m",   # 青
    STEP:  "\033[95m",   # 品红
    DIM:   "\033[90m",   # 灰
    BRAND: "\033[38;5;208m",  # 橙（品牌色）
    RESET: "\033[0m",
}
if not _ANSI_OK:
    _ANSI = {k: "" for k in _ANSI}

_TAG = {
    OK:    "  OK  ",
    FAIL:  " FAIL ",
    WARN:  " WARN ",
    INFO:  " INFO ",
    STEP:  " >>>  ",
    DIM:   "      ",
    BRAND: " G8T  ",
}

BRAND_NAME = "Games8Th.Team"
BRAND_SUB = "AppGuard"
VERSION = "1.0.0"

_LOGO = r"""
   ______                       _____ _   _     _______ _         _______                  _
  / ____/___ _____ ___  ___  __/__  /| | | |   |__   __| |       |__   __|                | |
 / / __/ __ `/ __ `__ \/ _ \/ ___/ / | |_| |__   | |  | |__   ___   | | ___  __ _ _ __ ___ | |
/ /_/ / /_/ / / / / / /  __(__  ) /| |  __   |  | |  | '_ \ / _ \  | |/ _ \/ _` | '_ ` _ \| |
\____/\__,_/_/ /_/ /_/\___/____/_/ |_|\__,_|  |_|  |_.__/\___/  |_|\___/\__,_|_| |_| |_|_|
"""


def banner(admin: bool = False, dry_run: bool = False):
    """打印启动横幅与文字 LOGO。"""
    out = []
    for line in _LOGO.strip("\n").split("\n"):
        out.append(_ANSI[BRAND] + line + _ANSI[RESET])
    out.append("")
    title = "  %s  ·  %s   v%s" % (BRAND_NAME, BRAND_SUB, VERSION)
    white = "\033[97m" if _ANSI_OK else ""
    out.append(white + title + _ANSI[RESET])
    out.append("  " + "-" * 70)
    mode = "DRY-RUN (仅演示，不写入系统)" if dry_run else "LIVE (真实生效)"
    out.append("  权限模式 : %s%s%s" % (_ANSI[WARN] if dry_run else _ANSI[OK], mode, _ANSI[RESET]))
    out.append("  管理员   : %s%s%s" % (_ANSI[OK] if admin else _ANSI[FAIL],
                                      "是 (完整拦截能力)" if admin else "否 (建议以管理员运行)",
                                      _ANSI[RESET]))
    out.append("  日志颜色 : %s绿=成功%s  %s红=失败%s  %s黄=警告/进行中%s" % (
        _ANSI[OK], _ANSI[RESET], _ANSI[FAIL], _ANSI[RESET], _ANSI[WARN], _ANSI[RESET]))
    out.append("  " + "-" * 70)
    _safe_print("\n".join(out))


class Logger:
    """
    中央日志总线：同时输出到终端与 GUI 日志面板。

    颜色约定（用户要求）：
        绿 OK    -> 限制/解除 成功
        红 FAIL  -> 限制/解除 失败
        黄 WARN  -> 警告、跳过、进行中
    """

    LEVEL_MAP = {"OK": OK, "SUCCESS": OK, "FAIL": FAIL, "ERROR": FAIL,
                 "WARN": WARN, "WARNING": WARN, "INFO": INFO}

    def __init__(self, log_file: str = None, quiet_console: bool = False):
        self.log_file = log_file
        self.quiet_console = quiet_console
        self._subs = []
        self._lock = threading.Lock()
        if log_file:
            os.makedirs(os.path.dirname(log_file), exist_ok=True)
            with open(log_file, "a", encoding="utf-8") as f:
                f.write("\n" + "=" * 78 + "\n")
                f.write("%s  %s %s  session start\n" % (ts(), BRAND_NAME, BRAND_SUB))

    # ── 订阅接口：GUI 用 ────────────────────────────────────────────
    def subscribe(self, cb):
        """注册回调 cb(level, text, plain_text)。"""
        self._subs.append(cb)

    def unsubscribe(self, cb):
        if cb in self._subs:
            self._subs.remove(cb)

    # ── 核心输出 ────────────────────────────────────────────────────
    def emit(self, level: str, text: str, echo: bool = True):
        lvl = level.upper()
        if lvl not in _TAG:
            lvl = self.LEVEL_MAP.get(lvl, INFO)
        stamp = datetime.now().strftime("%H:%M:%S")
        tag = _TAG[lvl]
        colored = "%s%s%s[%s]%s %s%s" % (_ANSI[DIM], _ANSI[RESET], _ANSI[lvl],
                                        tag, _ANSI[RESET], _ANSI[lvl], text)
        plain = "[%s] %s" % (tag.strip() or "----", text)

        if not self.quiet_console and echo:
            with self._lock:
                _safe_print(colored + _ANSI[RESET])

        if self.log_file:
            with self._lock:
                with open(self.log_file, "a", encoding="utf-8") as f:
                    f.write("%s  %-6s %s\n" % (stamp, lvl, text))

        for cb in list(self._subs):
            try:
                cb(lvl, colored, plain)
            except Exception:
                pass

    # ── 便捷方法 ────────────────────────────────────────────────────
    def ok(self, t):    self.emit(OK, t)
    def fail(self, t):  self.emit(FAIL, t)
    def warn(self, t):  self.emit(WARN, t)
    def info(self, t):  self.emit(INFO, t)
    def step(self, t):  self.emit(STEP, t)
    def raw(self, t, echo=True): self.emit(DIM, t, echo=echo)

    def rule(self, label=""):
        line = "─" * 70
        if label:
            line = "── %s %s" % (label, "─" * max(0, 64 - len(label)))
        self.emit(DIM, line)


def ts() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


# ── 全局单例 ────────────────────────────────────────────────────────────
_default = Logger()


def get_logger() -> Logger:
    return _default


def configure(log_file=None, quiet_console=False) -> Logger:
    global _default
    _default = Logger(log_file=log_file, quiet_console=quiet_console)
    return _default
