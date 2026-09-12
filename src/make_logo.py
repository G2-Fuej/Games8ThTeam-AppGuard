"""
处理 Games8Th.Team 官方 LOGO：
  1. 把黑色背景转成透明（按亮度提取 alpha）
  2. 裁掉多余留白，居中到正方形画布
  3. 生成多种尺寸，供 GUI 与图标使用（避免运行时依赖 PIL）

输入 : assets/logo_src.png   （原始黑底白字 LOGO）
输出 : assets/logo_mark.png  （透明底白色徽标，256px 主图）
       assets/logo_dark.png  （透明底深色徽标，用于浅色背景）
       assets/sized/mark_{n}.png
"""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
SIZED = os.path.join(ASSETS, "sized")
os.makedirs(SIZED, exist_ok=True)

SRC = os.path.join(ASSETS, "logo_src.png")
if not os.path.exists(SRC):
    raise SystemExit("缺少源文件: %s" % SRC)

im = Image.open(SRC).convert("RGBA")
W, H = im.size
px = im.load()

# ── 1. 黑底 -> alpha ────────────────────────────────────────────────────
# 徽标本身是白色（含浅蓝描边），背景接近纯黑。
# 用最大通道值作为不透明度：黑(0)->全透明，白(255)->不透明。
out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
op = out.load()
dark = Image.new("RGBA", (W, H), (0, 0, 0, 0))
dp = dark.load()

DARK_RGB = (31, 37, 48)      # #1f2530 与 GUI 主文字色一致

for y in range(H):
    for x in range(W):
        r, g, b, _ = px[x, y]
        a = max(r, g, b)
        if a <= 8:
            continue
        # 轻微提升对比，让边缘更干净
        if a < 40:
            a = 0
        op[x, y] = (255, 255, 255, a)                  # 白色版
        dp[x, y] = (DARK_RGB[0], DARK_RGB[1], DARK_RGB[2], a)  # 深色版

# ── 2. 裁到内容边界并居中 ───────────────────────────────────────────────
bbox = out.getbbox()
if bbox:
    out = out.crop(bbox)
    dark = dark.crop(bbox)

w, h = out.size
side = max(w, h)
pad = int(side * 0.06)
side += pad * 2


def center(img, side):
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(img, ((side - img.width) // 2, (side - img.height) // 2), img)
    return canvas


out = center(out, side)
dark = center(dark, side)

MASTER = 512
out = out.resize((MASTER, MASTER), Image.LANCZOS)
dark = dark.resize((MASTER, MASTER), Image.LANCZOS)

out.save(os.path.join(ASSETS, "logo_mark.png"))
dark.save(os.path.join(ASSETS, "logo_dark.png"))
print("OK: logo_mark.png / logo_dark.png", out.size)

# ── 3. 生成离散尺寸（运行时直接取用，不做缩放，保证清晰）──────────────
SIZES = [20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 64, 72, 80, 96, 112, 128, 160, 192, 256]
for n in SIZES:
    out.resize((n, n), Image.LANCZOS).save(os.path.join(SIZED, "mark_%d.png" % n))
    dark.resize((n, n), Image.LANCZOS).save(os.path.join(SIZED, "markdark_%d.png" % n))
print("OK: 生成 %d 种尺寸 -> %s" % (len(SIZES), SIZED))
