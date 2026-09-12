"""
Games8Th.Team AppGuard - cli_main.py
PyInstaller 打包入口（控制台版）。
"""

import os
import sys


def _bootstrap():
    if getattr(sys, "frozen", False):
        base = getattr(sys, "_MEIPASS", os.path.dirname(sys.executable))
        if base not in sys.path:
            sys.path.insert(0, base)
    else:
        here = os.path.dirname(os.path.abspath(__file__))
        if here not in sys.path:
            sys.path.insert(0, here)


def main():
    _bootstrap()
    from g8t.cli import main as cli_main
    return cli_main()


if __name__ == "__main__":
    sys.exit(main())
