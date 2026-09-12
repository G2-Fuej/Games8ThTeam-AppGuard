"""
Games8Th.Team AppGuard - core/config.py
配置持久化（JSON），存放于 %APPDATA%\\Games8ThTeam\\AppGuard\\
"""

import os
import json
import shutil
from datetime import datetime

from .guard import Target

APP_DIR_NAME = "Games8ThTeam"
APP_SUB = "AppGuard"
CONFIG_FILE = "appguard.config.json"


def appdata_dir() -> str:
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    d = os.path.join(base, APP_DIR_NAME, APP_SUB)
    os.makedirs(d, exist_ok=True)
    return d


def config_path() -> str:
    return os.path.join(appdata_dir(), CONFIG_FILE)


def log_path() -> str:
    return os.path.join(appdata_dir(), "appguard.log")


def backup_dir() -> str:
    d = os.path.join(appdata_dir(), "backups")
    os.makedirs(d, exist_ok=True)
    return d


DEFAULT = {
    "version": 1,
    "brand": "Games8Th.Team",
    "confirm_before_apply": True,
    "dry_run_default": False,
    "targets": [],
}


def load() -> dict:
    p = config_path()
    if not os.path.exists(p):
        return json.loads(json.dumps(DEFAULT))
    try:
        with open(p, "r", encoding="utf-8") as f:
            data = json.load(f)
        for k, v in DEFAULT.items():
            data.setdefault(k, v)
        return data
    except Exception:
        return json.loads(json.dumps(DEFAULT))


def save(cfg: dict) -> bool:
    p = config_path()
    try:
        # 写入前自动备份一次
        if os.path.exists(p):
            stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            shutil.copy2(p, os.path.join(backup_dir(), "config_%s.json" % stamp))
        with open(p, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        return True
    except Exception:
        return False


def load_targets():
    cfg = load()
    out = []
    for d in cfg.get("targets", []):
        try:
            out.append(Target.from_dict(d))
        except Exception:
            continue
    return cfg, out


def save_targets(targets, cfg: dict = None):
    cfg = cfg or load()
    cfg["targets"] = [t.to_dict() for t in targets]
    return save(cfg)
