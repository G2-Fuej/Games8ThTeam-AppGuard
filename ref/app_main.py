"""
Games8Th.Team AppGuard - app_main.py
PyInstaller 打包入口：默认启动图形界面。

打包命令见 build_exe.bat
"""

import os
import sys


def _bootstrap():
    """兼容 PyInstaller 单文件模式的资源路径。"""
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

    # 打包后支持 `AppGuard.exe cli ...` 走命令行（需控制台版）
    argv = sys.argv[1:]
    if argv and argv[0] == "cli":
        from g8t.cli import main as cli_main
        return cli_main(argv[1:])

    from g8t.gui.app import launch
    return launch()


if __name__ == "__main__":
    sys.exit(main())
