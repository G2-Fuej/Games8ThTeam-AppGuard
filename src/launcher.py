"""
Games8Th.Team AppGuard - launcher.py
统一入口：把 src 目录加入 sys.path 后分发给 GUI 或 CLI。
"""

import os
import sys

# 保证以任意工作目录运行都能 import g8t
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def main():
    argv = sys.argv[1:]
    target = "gui"
    if argv and argv[0] in ("gui", "cli"):
        target = argv[0]
        argv = argv[1:]

    if target == "gui":
        try:
            from g8t.gui.app import launch
            return launch()
        except ImportError as e:
            print("[WARN] GUI 启动失败 (%s)，回退到命令行模式" % e)
            from g8t.cli import interactive
            interactive()
            return 0
    else:
        from g8t.cli import main as cli_main
        return cli_main(argv)


if __name__ == "__main__":
    sys.exit(main())
