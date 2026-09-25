"""
Games8Th.Team AppGuard - gui/theme.py
亮色主题配色（浅色 IDE 主题），品牌橙 + 日志绿/红/黄。
"""

# ── 基础色板 ────────────────────────────────────────────────────────────
BG          = "#f5f7fa"   # 窗口底色
PANEL       = "#ffffff"   # 面板
PANEL_ALT   = "#eef1f6"   # 次级面板
BORDER      = "#d7dce5"   # 描边
BORDER_SOFT = "#e6eaf1"

TEXT        = "#1f2530"   # 主文字（深色，亮主题可读）
TEXT_DIM    = "#6b7383"   # 次要文字
TEXT_MUTE   = "#9aa2b1"   # 弱化文字

BRAND       = "#e07820"   # 品牌橙 Games8Th.Team
BRAND_DARK  = "#b85f12"
BRAND_SOFT  = "#fdf1e4"

# ── 日志三色（用户核心要求）────────────────────────────────────────────
LOG_OK      = "#1f9d3d"   # 绿 = 成功
LOG_FAIL    = "#d63b30"   # 红 = 失败
LOG_WARN    = "#c98a00"   # 黄 = 警告 / 进行中
LOG_INFO    = "#0f7f9c"   # 青 = 信息
LOG_STEP    = "#8a4fbf"   # 紫 = 阶段
LOG_DIM     = "#8b93a2"   # 灰

LOG_BG      = "#0f1420"   # 日志控制台深底（终端感）
LOG_BG_ALT  = "#141a28"

# ── 控件 ────────────────────────────────────────────────────────────────
BTN_BG      = "#ffffff"
BTN_HOVER   = "#eef2f7"
BTN_ACTIVE  = "#e2e8f0"
BTN_BORDER  = "#ccd3de"

ACCENT_OK_BG   = "#e6f6ea"
ACCENT_FAIL_BG = "#fdeaea"
ACCENT_WARN_BG = "#fff7e0"

SEL_BG      = BRAND_SOFT
SEL_FG      = "#8a4a06"

# ── 字体 ────────────────────────────────────────────────────────────────
FONT_UI    = "Microsoft YaHei UI"
FONT_MONO  = "Consolas"
FONT_EMOJI = "Segoe UI Emoji"

F_TITLE   = (FONT_UI, 15, "bold")
F_HEAD    = (FONT_UI, 11, "bold")
F_BODY    = (FONT_UI, 10)
F_SMALL   = (FONT_UI, 9)
F_TINY    = (FONT_UI, 8)
F_LOGO    = (FONT_MONO, 11, "bold")
F_LOG     = (FONT_MONO, 9)
F_BRAND   = (FONT_UI, 12, "bold")

# 日志等级 -> 前景色
LEVEL_COLORS = {
    "OK": LOG_OK, "FAIL": LOG_FAIL, "WARN": LOG_WARN,
    "INFO": LOG_INFO, "STEP": LOG_STEP, "DIM": LOG_DIM,
    "BRAND": BRAND,
}
