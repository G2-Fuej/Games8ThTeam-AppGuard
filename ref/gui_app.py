"""
Games8Th.Team AppGuard - gui/app.py
Tkinter 图形界面：文字 LOGO、目标管理、彩色日志面板（绿/红/黄）。
"""

import os
import sys
import queue
import threading
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

from ..core import console as C
from ..core import config as CFG
from ..core.guard import (
    Guard, Target, STRATEGIES, is_admin, relaunch_as_admin,
    common_targets, current_identity,
)
from . import theme as T


# 预渲染 LOGO 尺寸（与 make_logo.py 保持一致）
MARK_SIZES = [20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 64, 72, 80, 96, 112, 128, 160, 192, 256]


def assets_dir() -> str:
    """定位 assets 目录，兼容源码运行与 PyInstaller 打包。"""
    if getattr(sys, "frozen", False):
        base = getattr(sys, "_MEIPASS", os.path.dirname(sys.executable))
        return os.path.join(base, "assets")
    return os.path.normpath(
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets"))


class AppGuardGUI:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("%s %s  ·  应用权限限制工具" % (C.BRAND_NAME, C.BRAND_SUB))
        self.root.configure(bg=T.BG)
        self._dpi_fix()
        self._fit_screen()
        self.root.minsize(860, 560)

        self.cfg, self.targets = CFG.load_targets()
        self.dry_run = bool(self.cfg.get("dry_run_default", False))
        self.admin = is_admin()

        self.log = C.configure(log_file=CFG.log_path(), quiet_console=True)
        self.log.subscribe(self._on_log)          # 日志总线 -> GUI
        self.guard = Guard(self.log, dry_run=self.dry_run)

        self._q = queue.Queue()
        self._busy = False
        self._log_lines = 0

        self._build_fonts()
        self._build_style()
        self._build_ui()
        self._refresh_table()
        self._pump()

        self._banner()

        if not self.targets:
            self.log.info("未配置目标，已注入常见示例（默认未启用）")
            for c in common_targets():
                self.targets.append(Target(path=c["path"], note=c["note"], enabled=False))
            self._save()
            self._refresh_table()

        if not self.admin:
            self.log.warn("未以管理员身份运行：对系统目录下的目标可能限制失败")
            self.log.warn("可点击右上角「以管理员重启」获取完整权限")

    # ════════════════════════════════════════════════════════════════
    #  高 DPI 适配
    # ════════════════════════════════════════════════════════════════
    def _dpi_fix(self):
        """声明 DPI 感知，让 Tk 用真实像素渲染（文字清晰、不模糊）。"""
        try:
            import ctypes
            ctypes.windll.shcore.SetProcessDpiAwareness(1)  # SYSTEM_DPI_AWARE
        except Exception:
            try:
                ctypes.windll.user32.SetProcessDPIAware()
            except Exception:
                pass
        # 不手工改 tk scaling：交由系统 DPI 处理，布局用字体度量自适应

    def _fit_screen(self):
        """按屏幕可用区域计算窗口尺寸并居中，避免底部被截断。"""
        sw = self.root.winfo_screenwidth()
        sh = self.root.winfo_screenheight()
        # 预留任务栏与窗口边框
        avail_w = max(860, sw - 120)
        avail_h = max(560, sh - 140)
        w = min(1180, avail_w)
        h = min(760, avail_h)
        x = max(0, (sw - w) // 2)
        y = max(0, (sh - h) // 2 - 30)
        self.root.geometry("%dx%d+%d+%d" % (w, h, x, y))

        if os.environ.get("G8T_DEBUG"):
            try:
                import tempfile
                with open(os.path.join(tempfile.gettempdir(), "g8t_fit.log"),
                          "w", encoding="utf-8") as f:
                    f.write("screen=%dx%d\n" % (sw, sh))
                    f.write("avail=%dx%d\n" % (avail_w, avail_h))
                    f.write("geometry=%dx%d+%d+%d\n" % (w, h, x, y))
                    f.write("fpixels(1i)=%s\n" % self.root.winfo_fpixels("1i"))
                    f.write("tk_scaling=%s\n" % self.root.tk.call("tk", "scaling"))
                    f.write("frozen=%s\n" % getattr(sys, "frozen", False))
            except Exception:
                pass

    # ════════════════════════════════════════════════════════════════
    #  样式
    # ════════════════════════════════════════════════════════════════
    def _build_fonts(self):
        pass

    def _build_style(self):
        st = ttk.Style()
        try:
            st.theme_use("clam")
        except Exception:
            pass

        st.configure(".", background=T.BG, foreground=T.TEXT, font=T.F_BODY)
        st.configure("TFrame", background=T.BG)
        st.configure("Panel.TFrame", background=T.PANEL)
        st.configure("Alt.TFrame", background=T.PANEL_ALT)

        st.configure("TLabel", background=T.BG, foreground=T.TEXT, font=T.F_BODY)
        st.configure("Panel.TLabel", background=T.PANEL, foreground=T.TEXT)
        st.configure("Head.TLabel", background=T.PANEL, foreground=T.TEXT, font=T.F_HEAD)
        st.configure("Dim.TLabel", background=T.BG, foreground=T.TEXT_DIM, font=T.F_SMALL)
        st.configure("Brand.TLabel", background=T.PANEL, foreground=T.BRAND, font=T.F_BRAND)

        # 按钮
        st.configure("TButton", background=T.BTN_BG, foreground=T.TEXT,
                     bordercolor=T.BTN_BORDER, focusthickness=0,
                     padding=(12, 6), font=T.F_BODY, relief="flat")
        st.map("TButton",
               background=[("pressed", T.BTN_ACTIVE), ("active", T.BTN_HOVER)],
               foreground=[("disabled", T.TEXT_MUTE)])

        st.configure("Accent.TButton", background=T.BRAND, foreground="#ffffff",
                     padding=(14, 7), font=(T.FONT_UI, 10, "bold"), relief="flat")
        st.map("Accent.TButton",
               background=[("pressed", T.BRAND_DARK), ("active", "#f08a2e"),
                           ("disabled", "#e5cdb4")],
               foreground=[("disabled", "#ffffff")])

        st.configure("Danger.TButton", background=T.ACCENT_FAIL_BG, foreground=T.LOG_FAIL,
                     padding=(14, 7), font=(T.FONT_UI, 10, "bold"), relief="flat")
        st.map("Danger.TButton",
               background=[("pressed", "#f8d5d5"), ("active", "#fbe0e0")])

        # 表格
        st.configure("Treeview",
                     background=T.PANEL, fieldbackground=T.PANEL,
                     foreground=T.TEXT, rowheight=27, font=T.F_BODY,
                     bordercolor=T.BORDER, borderwidth=1)
        st.configure("Treeview.Heading",
                     background=T.PANEL_ALT, foreground=T.TEXT,
                     font=(T.FONT_UI, 9, "bold"), relief="flat", padding=(6, 6))
        st.map("Treeview.Heading", background=[("active", "#e3e8f0")])
        st.map("Treeview",
               background=[("selected", T.SEL_BG)],
               foreground=[("selected", T.SEL_FG)])

        st.configure("TCheckbutton", background=T.PANEL, foreground=T.TEXT, font=T.F_SMALL)
        st.map("TCheckbutton", background=[("active", T.PANEL)])

        st.configure("TCombobox", fieldbackground=T.PANEL, background=T.PANEL)
        st.configure("Horizontal.TProgressbar", background=T.BRAND,
                     troughcolor=T.PANEL_ALT, bordercolor=T.PANEL_ALT, lightcolor=T.BRAND)

    # ════════════════════════════════════════════════════════════════
    #  界面构建
    # ════════════════════════════════════════════════════════════════
    def _build_ui(self):
        self._build_header()

        body = ttk.Frame(self.root, style="TFrame")
        body.pack(fill="both", expand=True, padx=14, pady=(0, 12))

        left = ttk.Frame(body, style="Panel.TFrame")
        left.pack(side="left", fill="both", expand=True, padx=(0, 10))
        self._build_target_panel(left)

        right = ttk.Frame(body, style="Panel.TFrame")
        right.pack(side="left", fill="both", expand=True)
        self._build_log_panel(right)

    # ── 顶部品牌区 + 文字 LOGO ──────────────────────────────────────
    def _build_header(self):
        h = tk.Frame(self.root, bg=T.PANEL, highlightthickness=0)
        h.pack(fill="x", padx=14, pady=(12, 10))

        inner = tk.Frame(h, bg=T.PANEL)
        inner.pack(fill="x", padx=16, pady=12)

        # ── 左侧：ASCII 文字 LOGO（Games8Th.Team）──
        logo_box = tk.Frame(inner, bg=T.PANEL)
        logo_box.pack(side="left", anchor="n")

        self.logo_canvas = tk.Canvas(logo_box, bg=T.PANEL, highlightthickness=0,
                                     width=545, height=118)
        self.logo_canvas.pack(anchor="w")
        self._draw_logo()

        # ── 右侧：状态与操作 ──
        rt = tk.Frame(inner, bg=T.PANEL)
        rt.pack(side="right", anchor="ne")

        mode_txt = "DRY-RUN 演示" if self.dry_run else "LIVE 真实生效"
        mode_fg = T.LOG_WARN if self.dry_run else T.LOG_OK
        self.lbl_mode = tk.Label(rt, text="●  " + mode_txt, bg=T.PANEL, fg=mode_fg,
                                 font=(T.FONT_UI, 9, "bold"))
        self.lbl_mode.pack(anchor="e")

        adm_txt = "管理员权限" if self.admin else "普通用户"
        adm_fg = T.LOG_OK if self.admin else T.LOG_FAIL
        tk.Label(rt, text="●  " + adm_txt, bg=T.PANEL, fg=adm_fg,
                 font=(T.FONT_UI, 9, "bold")).pack(anchor="e", pady=(2, 0))

        tk.Label(rt, text="身份: " + current_identity(), bg=T.PANEL, fg=T.TEXT_DIM,
                 font=T.F_TINY).pack(anchor="e", pady=(2, 6))

        btns = tk.Frame(rt, bg=T.PANEL)
        btns.pack(anchor="e")
        if not self.admin:
            ttk.Button(btns, text="以管理员重启", style="TButton",
                       command=self._elevate).pack(side="left", padx=(0, 6))
        ttk.Button(btns, text="切换模式", style="TButton",
                   command=self._toggle_mode).pack(side="left")

        # 底部分隔线
        tk.Frame(self.root, bg=T.BORDER, height=1).pack(fill="x", padx=14)

    @staticmethod
    def _round_rect(cv, x1, y1, x2, y2, r, **kw):
        """在 Canvas 上画圆角矩形。"""
        pts = [
            x1 + r, y1, x2 - r, y1, x2, y1,
            x2, y1 + r, x2, y2 - r, x2, y2,
            x2 - r, y2, x1 + r, y2, x1, y2,
            x1, y2 - r, x1, y1 + r, x1, y1,
        ]
        return cv.create_polygon(pts, smooth=True, **kw)

    def _load_mark(self, target_px):
        """
        加载官方 G8 徽标（透明底白色版）。

        直接取用最接近目标像素的预渲染尺寸，避免运行时缩放造成的锯齿；
        必要时用 subsample 整数降采样。返回的 PhotoImage 会被缓存，
        否则会被 GC 回收导致画布上图像消失。
        """
        if not hasattr(self, "_mark_cache"):
            self._mark_cache = {}

        pick = min(MARK_SIZES, key=lambda s: abs(s - target_px))
        key = (pick, target_px)
        if key in self._mark_cache:
            return self._mark_cache[key]

        path = os.path.join(assets_dir(), "sized", "mark_%d.png" % pick)
        img = None
        if os.path.exists(path):
            try:
                img = tk.PhotoImage(file=path)
                # 明显偏大时做一次整数降采样，贴合目标尺寸
                if pick >= target_px * 1.9:
                    img = img.subsample(2)
                elif pick >= target_px * 2.9:
                    img = img.subsample(3)
            except Exception:
                img = None

        self._mark_cache[key] = img
        return img

    def _draw_logo(self):
        """
        绘制 Games8Th.Team 文字 LOGO。

        采用「品牌徽标 + 字标」的专业排印方案：左侧为品牌色圆角徽标，
        右侧为 Games8Th.Team 字标与产品副标题。全部按字体真实度量
        (linespace / measure) 定位，高 DPI 下也不会重叠或裁切。
        """
        import tkinter.font as tkfont

        cv = self.logo_canvas
        cv.delete("all")

        f_brand = tkfont.Font(family=T.FONT_UI, size=16, weight="bold")
        f_prod = tkfont.Font(family=T.FONT_UI, size=10, weight="bold")
        f_note = tkfont.Font(family=T.FONT_UI, size=8)
        f_ver = tkfont.Font(family=T.FONT_UI, size=8)

        # ── 左侧品牌徽标：橙色圆角底 + 官方 G8 徽标 ──
        box = 56
        bx, by = 2, 4
        self._round_rect(cv, bx, by, bx + box, by + box, 12,
                         fill=T.BRAND, outline="")
        self._round_rect(cv, bx + 3, by + 3, bx + box - 3, by + box - 3, 10,
                         fill="", outline=T.BRAND_DARK, width=1)

        mark = self._load_mark(int(box * 0.66))
        if mark is not None:
            cv.create_image(bx + box / 2, by + box / 2 + 1, image=mark, anchor="center")

        # ── 右侧字标 ──
        tx = bx + box + 14
        ls_brand = f_brand.metrics("linespace")
        y = by + 2

        cv.create_text(tx, y, text="Games8Th.Team", anchor="nw",
                       fill=T.BRAND_DARK, font=f_brand)
        w_brand = f_brand.measure("Games8Th.Team")

        # 版本号跟随字标右侧
        ls_ver = f_ver.metrics("linespace")
        cv.create_text(tx + w_brand + 10, y + ls_brand - ls_ver - 2,
                       text="v" + C.VERSION, anchor="nw",
                       fill=T.TEXT_MUTE, font=f_ver)

        # 产品名 + 说明
        y += ls_brand + 5
        cv.create_text(tx, y, text="AppGuard", anchor="nw",
                       fill=T.BRAND, font=f_prod)
        w_prod = f_prod.measure("AppGuard")
        cv.create_text(tx + w_prod + 8, y + 2, text="应用权限限制工具", anchor="nw",
                       fill=T.TEXT_DIM, font=f_note)

        y += f_prod.metrics("linespace") + 2
        cv.create_text(tx, y,
                       text="日志颜色：绿=成功   红=失败   黄=警告",
                       anchor="nw", fill=T.TEXT_MUTE, font=f_note)

        # ── 按内容自动定尺 ──
        bb = cv.bbox("all")
        if bb:
            cv.configure(width=max(360, bb[2] + 10), height=max(70, bb[3] + 8))

    # ── 左：目标管理 ────────────────────────────────────────────────
    def _build_target_panel(self, parent):
        pad = tk.Frame(parent, bg=T.PANEL)
        pad.pack(fill="both", expand=True, padx=14, pady=14)

        head = tk.Frame(pad, bg=T.PANEL)
        head.pack(fill="x")
        tk.Label(head, text="限制目标", bg=T.PANEL, fg=T.TEXT,
                 font=(T.FONT_UI, 12, "bold")).pack(side="left")
        self.lbl_count = tk.Label(head, text="", bg=T.PANEL, fg=T.TEXT_DIM, font=T.F_SMALL)
        self.lbl_count.pack(side="left", padx=(8, 0))

        tk.Label(pad, text="勾选 = 参与限制；双击可改策略", bg=T.PANEL,
                 fg=T.TEXT_MUTE, font=T.F_TINY).pack(anchor="w", pady=(2, 8))

        # 表格
        wrap = tk.Frame(pad, bg=T.BORDER)
        wrap.pack(fill="both", expand=True)
        cols = ("on", "name", "strategy", "state", "path")
        self.tree = ttk.Treeview(wrap, columns=cols, show="headings",
                                 selectmode="browse", height=14)
        for c, txt, w, anchor in (
            ("on", "启用", 48, "center"),
            ("name", "目标名称", 130, "w"),
            ("strategy", "限制策略", 80, "center"),
            ("state", "当前状态", 84, "center"),
            ("path", "路径", 240, "w"),
        ):
            self.tree.heading(c, text=txt)
            self.tree.column(c, width=w, anchor=anchor, stretch=(c == "path"))

        vs = ttk.Scrollbar(wrap, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=vs.set)
        vs.pack(side="right", fill="y")
        self.tree.pack(side="left", fill="both", expand=True, padx=1, pady=1)

        self.tree.bind("<Double-1>", self._on_double)
        self.tree.bind("<Button-1>", self._on_click)
        self.tree.tag_configure("off", foreground=T.TEXT_MUTE)
        self.tree.tag_configure("on", foreground=T.TEXT)

        # 操作按钮区
        ops = tk.Frame(pad, bg=T.PANEL)
        ops.pack(fill="x", pady=(10, 0))

        r1 = tk.Frame(ops, bg=T.PANEL)
        r1.pack(fill="x")
        for txt, cb in (("添加目标", self._add_target),
                        ("选择程序", self._add_file),
                        ("选择目录", self._add_dir),
                        ("删除", self._del_target)):
            ttk.Button(r1, text=txt, style="TButton", command=cb).pack(side="left", padx=(0, 6))

        r2 = tk.Frame(ops, bg=T.PANEL)
        r2.pack(fill="x", pady=(8, 0))
        self.btn_apply = ttk.Button(r2, text="▶  实施限制", style="Accent.TButton",
                                    command=self._apply)
        self.btn_apply.pack(side="left", padx=(0, 8))
        self.btn_clear = ttk.Button(r2, text="■  解除全部限制", style="Danger.TButton",
                                    command=self._clear)
        self.btn_clear.pack(side="left", padx=(0, 8))
        ttk.Button(r2, text="核验状态", style="TButton",
                   command=self._verify).pack(side="left", padx=(0, 6))
        ttk.Button(r2, text="接管所有权", style="TButton",
                   command=self._takeown).pack(side="left")

        # 进度条
        self.pb = ttk.Progressbar(pad, mode="determinate",
                                  style="Horizontal.TProgressbar")
        self.pb.pack(fill="x", pady=(10, 0))

    # ── 右：日志面板 ────────────────────────────────────────────────
    def _build_log_panel(self, parent):
        pad = tk.Frame(parent, bg=T.PANEL)
        pad.pack(fill="both", expand=True, padx=14, pady=14)

        head = tk.Frame(pad, bg=T.PANEL)
        head.pack(fill="x")
        tk.Label(head, text="运行日志", bg=T.PANEL, fg=T.TEXT,
                 font=(T.FONT_UI, 12, "bold")).pack(side="left")

        # 颜色图例（体现绿红黄要求）
        leg = tk.Frame(head, bg=T.PANEL)
        leg.pack(side="right")
        for txt, fg in (("成功", T.LOG_OK), ("失败", T.LOG_FAIL), ("警告", T.LOG_WARN)):
            tk.Label(leg, text="● " + txt, bg=T.PANEL, fg=fg,
                     font=(T.FONT_UI, 8, "bold")).pack(side="left", padx=(0, 8))

        tk.Label(pad, text="绿=限制成功 · 红=限制失败 · 黄=警告/进行中",
                 bg=T.PANEL, fg=T.TEXT_MUTE, font=T.F_TINY).pack(anchor="w", pady=(2, 8))

        box = tk.Frame(pad, bg=T.LOG_BG)
        box.pack(fill="both", expand=True)

        self.txt = tk.Text(box, bg=T.LOG_BG, fg="#d5dae4", insertbackground="#d5dae4",
                           font=T.F_LOG, wrap="none", relief="flat",
                           padx=10, pady=8, spacing1=1, spacing3=2,
                           selectbackground="#2c3848", borderwidth=0,
                           highlightthickness=0)
        vs = ttk.Scrollbar(box, orient="vertical", command=self.txt.yview)
        hs = ttk.Scrollbar(box, orient="horizontal", command=self.txt.xview)
        self.txt.configure(yscrollcommand=vs.set, xscrollcommand=hs.set)
        vs.pack(side="right", fill="y")
        hs.pack(side="bottom", fill="x")
        self.txt.pack(side="left", fill="both", expand=True)

        for lvl, col in T.LEVEL_COLORS.items():
            self.txt.tag_configure("L_" + lvl, foreground=col)
        self.txt.tag_configure("L_HDR", foreground=T.BRAND, font=(T.FONT_MONO, 9, "bold"))
        self.txt.configure(state="disabled")

        # 日志工具栏
        bar = tk.Frame(pad, bg=T.PANEL)
        bar.pack(fill="x", pady=(8, 0))
        ttk.Button(bar, text="清空日志", style="TButton",
                   command=self._clear_log).pack(side="left", padx=(0, 6))
        ttk.Button(bar, text="打开日志文件", style="TButton",
                   command=self._open_logfile).pack(side="left", padx=(0, 6))
        ttk.Button(bar, text="运行安全演示", style="TButton",
                   command=self._demo).pack(side="left", padx=(0, 6))
        self.lbl_status = tk.Label(bar, text="就绪", bg=T.PANEL, fg=T.TEXT_DIM, font=T.F_SMALL)
        self.lbl_status.pack(side="right")

    # ════════════════════════════════════════════════════════════════
    #  日志 -> GUI
    # ════════════════════════════════════════════════════════════════
    def _on_log(self, level: str, colored: str, plain: str):
        """日志总线回调（可能来自任意线程）→ 投递到队列。"""
        self._q.put((level, plain))

    def _pump(self):
        try:
            while True:
                level, plain = self._q.get_nowait()
                self._append(level, plain)
        except queue.Empty:
            pass
        self.root.after(60, self._pump)

    def _append(self, level, plain):
        self.txt.configure(state="normal")
        # 等级列着色，正文跟随
        tag = "L_" + level if level in T.LEVEL_COLORS else "L_DIM"
        stamp = ""
        if plain.startswith("[") and "]" in plain:
            idx = plain.index("]")
            stamp, plain = plain[:idx + 1], plain[idx + 1:]
        self.txt.insert("end", stamp, tag)
        self.txt.insert("end", plain + "\n", tag)
        self._log_lines += 1
        # 限制最大行数，防止内存膨胀
        if self._log_lines > 4000:
            self.txt.delete("1.0", "600.0")
            self._log_lines -= 600
        self.txt.see("end")
        self.txt.configure(state="disabled")
        # 状态栏显示最近一条
        fg = T.LEVEL_COLORS.get(level, T.TEXT_DIM)
        self.lbl_status.configure(text=plain.strip()[:60], fg=fg)

    def _banner(self):
        self._q.put(("HDR", ""))
        self._append("HDR", "═" * 62)
        self._append("HDR", "  %s  ·  %s   v%s" % (C.BRAND_NAME, C.BRAND_SUB, C.VERSION))
        self._append("HDR", "  Windows 应用权限限制工具  |  日志三色: 绿=成功 红=失败 黄=警告")
        self._append("HDR", "═" * 62)
        self.log.info("配置文件: %s" % CFG.config_path())
        self.log.info("日志文件: %s" % CFG.log_path())
        self.log.ok("%s 就绪，共 %d 个目标" % (C.BRAND_NAME, len(self.targets)))

    def _clear_log(self):
        self.txt.configure(state="normal")
        self.txt.delete("1.0", "end")
        self.txt.configure(state="disabled")
        self._log_lines = 0

    def _open_logfile(self):
        p = CFG.log_path()
        try:
            os.startfile(p)
        except Exception as e:
            messagebox.showerror("无法打开", "%s\n%s" % (p, e))

    # ════════════════════════════════════════════════════════════════
    #  表格
    # ════════════════════════════════════════════════════════════════
    def _refresh_table(self):
        sel = self.tree.selection()
        selpath = None
        if sel:
            vals = self.tree.item(sel[0], "values")
            if len(vals) > 4:
                selpath = vals[4]

        for i in self.tree.get_children():
            self.tree.delete(i)

        for t in self.targets:
            st = STRATEGIES.get(t.strategy, {})
            state, _ = self.guard.inspect(t)
            state_txt = {"restricted": "● 已限制", "open": "○ 未限制",
                         "missing": "✕ 不存在", "error": "? 异常"}.get(state, "?")
            self.tree.insert("", "end", values=(
                "☑" if t.enabled else "☐",
                t.name,
                st.get("label", t.strategy),
                state_txt,
                t.path,
            ), tags=("on" if t.enabled else "off",))

        n_on = sum(1 for t in self.targets if t.enabled)
        self.lbl_count.configure(text="共 %d 项 · 启用 %d 项" % (len(self.targets), n_on))

        if selpath:
            for i in self.tree.get_children():
                if self.tree.item(i, "values")[4] == selpath:
                    self.tree.selection_set(i)
                    break

    def _selected(self):
        sel = self.tree.selection()
        if not sel:
            return None
        path = self.tree.item(sel[0], "values")[4]
        for t in self.targets:
            if t.path == path:
                return t
        return None

    def _on_click(self, event):
        """点击「启用」列切换勾选状态。"""
        if self.tree.identify_region(event.x, event.y) != "cell":
            return
        if self.tree.identify_column(event.x) != "#1":
            return
        row = self.tree.identify_row(event.y)
        if not row:
            return
        path = self.tree.item(row, "values")[4]
        for t in self.targets:
            if t.path == path:
                t.enabled = not t.enabled
                self._save()
                self._refresh_table()
                (self.log.ok if t.enabled else self.log.warn)(
                    "%s 已%s" % (t.name, "加入限制" if t.enabled else "移出限制"))
                break
        return "break"

    def _on_double(self, event):
        t = self._selected()
        if t:
            self._change_strategy(t)

    # ════════════════════════════════════════════════════════════════
    #  目标操作
    # ════════════════════════════════════════════════════════════════
    def _add_path(self, path):
        if not path:
            return
        path = os.path.abspath(path)
        if not os.path.exists(path):
            self.log.fail("路径不存在: %s" % path)
            return
        if any(os.path.normcase(t.path) == os.path.normcase(path) for t in self.targets):
            self.log.warn("该目标已在列表中: %s" % os.path.basename(path))
            return
        t = Target(path=path, strategy="DENY_EXEC", enabled=True)
        self.targets.append(t)
        self._save()
        self._refresh_table()
        self.log.ok("已添加目标: %s" % t.name)
        self._change_strategy(t, first=True)

    def _add_target(self):
        dlg = tk.Toplevel(self.root)
        dlg.title("添加目标")
        dlg.configure(bg=T.PANEL)
        dlg.geometry("560x180")
        dlg.transient(self.root)
        dlg.grab_set()
        tk.Label(dlg, text="输入要限制的目标完整路径（程序或文件夹）",
                 bg=T.PANEL, fg=T.TEXT, font=T.F_HEAD).pack(anchor="w", padx=16, pady=(14, 6))
        var = tk.StringVar()
        e = tk.Entry(dlg, textvariable=var, font=T.F_BODY, relief="solid", bd=1)
        e.pack(fill="x", padx=16)
        e.focus_set()
        row = tk.Frame(dlg, bg=T.PANEL)
        row.pack(fill="x", padx=16, pady=10)

        def browse():
            p = filedialog.askopenfilename(title="选择程序",
                                           filetypes=[("可执行文件", "*.exe"), ("所有文件", "*.*")])
            if p:
                var.set(p)

        ttk.Button(row, text="浏览...", style="TButton", command=browse).pack(side="left")

        def confirm():
            self._add_path(var.get().strip().strip('"'))
            dlg.destroy()

        ttk.Button(row, text="确认添加", style="Accent.TButton",
                   command=confirm).pack(side="right")
        ttk.Button(row, text="取消", style="TButton",
                   command=dlg.destroy).pack(side="right", padx=(0, 8))
        dlg.bind("<Return>", lambda e: confirm())

    def _add_file(self):
        p = filedialog.askopenfilename(title="选择要限制的程序",
                                       filetypes=[("可执行文件", "*.exe *.bat *.cmd *.ps1"),
                                                  ("所有文件", "*.*")])
        self._add_path(p)

    def _add_dir(self):
        p = filedialog.askdirectory(title="选择要限制的文件夹")
        self._add_path(p)

    def _del_target(self):
        t = self._selected()
        if not t:
            self.log.warn("请先在列表中选择一个目标")
            return
        if not messagebox.askyesno(
                "确认删除",
                "从列表移除「%s」？\n\n注意：只从配置移除，不会修改系统权限。\n如需解除限制请先点击「解除全部限制」。" % t.name):
            return
        self.targets.remove(t)
        self._save()
        self._refresh_table()
        self.log.warn("已从列表移除: %s" % t.name)

    def _change_strategy(self, t, first=False):
        dlg = tk.Toplevel(self.root)
        dlg.title("限制策略")
        dlg.configure(bg=T.PANEL)
        dlg.geometry("440x260")
        dlg.transient(self.root)
        dlg.grab_set()

        tk.Label(dlg, text="为「%s」选择限制策略" % t.name, bg=T.PANEL, fg=T.TEXT,
                 font=T.F_HEAD).pack(anchor="w", padx=16, pady=(14, 8))
        tk.Label(dlg, text=t.path, bg=T.PANEL, fg=T.TEXT_MUTE,
                 font=T.F_TINY, wraplength=400, justify="left").pack(anchor="w", padx=16)

        var = tk.StringVar(value=t.strategy)
        for k, v in STRATEGIES.items():
            rb = tk.Radiobutton(dlg, text="%-11s %s   (%s)" % (k, v["label"], v["desc"]),
                                variable=var, value=k, bg=T.PANEL, fg=T.TEXT,
                                activebackground=T.PANEL, selectcolor=T.PANEL,
                                font=T.F_BODY, anchor="w")
            rb.pack(fill="x", padx=20, pady=1)

        def confirm():
            t.strategy = var.get()
            self._save()
            self._refresh_table()
            self.log.ok("%s 策略已设为 %s，需重新「实施限制」生效" % (t.name, var.get()))
            dlg.destroy()

        row = tk.Frame(dlg, bg=T.PANEL)
        row.pack(fill="x", padx=16, pady=12)
        ttk.Button(row, text="确定", style="Accent.TButton",
                   command=confirm).pack(side="right")
        if not first:
            ttk.Button(row, text="取消", style="TButton",
                       command=dlg.destroy).pack(side="right", padx=(0, 8))

    # ════════════════════════════════════════════════════════════════
    #  执行动作（后台线程，避免界面卡死）
    # ════════════════════════════════════════════════════════════════
    def _run_async(self, fn, label):
        if self._busy:
            self.log.warn("已有任务在执行中，请等待完成")
            return
        self._busy = True
        self.btn_apply.configure(state="disabled")
        self.btn_clear.configure(state="disabled")
        self.pb.configure(mode="indeterminate")
        self.pb.start(12)
        self.log.step("开始任务: %s" % label)

        def worker():
            try:
                fn()
            except Exception as e:
                self.log.fail("任务异常: %s: %s" % (type(e).__name__, e))
            finally:
                self.root.after(0, done)

        def done():
            self.pb.stop()
            self.pb.configure(mode="determinate", value=0)
            self.btn_apply.configure(state="normal")
            self.btn_clear.configure(state="normal")
            self._busy = False
            self._refresh_table()

        threading.Thread(target=worker, daemon=True).start()

    def _apply(self):
        n = sum(1 for t in self.targets if t.enabled)
        if n == 0:
            self.log.warn("没有启用中的目标，请先勾选")
            return
        if self.cfg.get("confirm_before_apply", True):
            if not messagebox.askyesno(
                    "确认实施限制",
                    "将对 %d 个目标实施限制。\n\n"
                    "限制后这些程序/目录将无法按策略访问，\n"
                    "需管理员权限才能设置，且可通过「解除全部限制」还原。\n\n确认继续？" % n):
                return
        if self.dry_run:
            self.log.warn("当前为 DRY-RUN 模式，仅演示不写入系统")

        def job():
            stats = self.guard.apply_all(self.targets)
            self._save()
            if stats["fail"] == 0:
                self.log.ok("★ 限制完成：全部成功（%d/%d）" % (stats["ok"], stats["total"]))
            else:
                self.log.fail("★ 限制完成：成功 %d / 失败 %d" % (stats["ok"], stats["fail"]))

        self._run_async(job, "实施限制")

    def _clear(self):
        if not self.targets:
            self.log.warn("列表为空")
            return
        if not messagebox.askyesno(
                "确认解除限制",
                "将解除列表中全部 %d 个目标的 DENY 规则。\n\n确认继续？" % len(self.targets)):
            return

        def job():
            stats = self.guard.remove_all(self.targets)
            self._save()
            if stats["fail"] == 0:
                self.log.ok("★ 解除完成：全部成功（%d/%d）" % (stats["ok"], stats["total"]))
            else:
                self.log.fail("★ 解除完成：成功 %d / 失败 %d" % (stats["ok"], stats["fail"]))

        self._run_async(job, "解除限制")

    def _verify(self):
        def job():
            self.guard.verify_all(self.targets)
        self._run_async(job, "核验状态")

    def _takeown(self):
        t = self._selected()
        if not t:
            self.log.warn("请先选择一个目标")
            return
        if not messagebox.askyesno(
                "接管所有权",
                "将对以下目标执行 takeown / icacls reset：\n\n%s\n\n"
                "该操作会重置其 ACL 为继承状态，用于修复被破坏的权限。\n确认继续？" % t.path):
            return

        def job():
            ok, msg = self.guard.take_ownership(t)
            (self.log.ok if ok else self.log.fail)(msg)

        self._run_async(job, "接管所有权")

    def _demo(self):
        from ..cli import run_demo

        def job():
            run_demo(self.guard)

        self._run_async(job, "安全演示")

    def _toggle_mode(self):
        self.dry_run = not self.dry_run
        self.guard.dry_run = self.dry_run
        self.cfg["dry_run_default"] = self.dry_run
        CFG.save(self.cfg)
        txt = "DRY-RUN 演示" if self.dry_run else "LIVE 真实生效"
        fg = T.LOG_WARN if self.dry_run else T.LOG_OK
        self.lbl_mode.configure(text="●  " + txt, fg=fg)
        (self.log.warn if self.dry_run else self.log.ok)("已切换为 %s 模式" % txt)

    def _elevate(self):
        if self.admin:
            self.log.warn("当前已是管理员权限")
            return
        if messagebox.askyesno("提权", "将以管理员身份重新启动程序，确认继续？"):
            if relaunch_as_admin([sys.argv[0], "gui"]):
                self.log.ok("正在以管理员身份重启 ...")
                self.root.after(600, self.root.destroy)
            else:
                self.log.fail("提权被取消或失败")

    def _save(self):
        self.cfg["targets"] = [t.to_dict() for t in self.targets]
        if not CFG.save(self.cfg):
            self.log.fail("配置保存失败: %s" % CFG.config_path())


def launch():
    root = tk.Tk()
    try:
        root.iconbitmap(default="")
    except Exception:
        pass
    AppGuardGUI(root)
    root.mainloop()
    return 0


if __name__ == "__main__":
    launch()
