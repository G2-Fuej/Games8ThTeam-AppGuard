"""
生成应用图标 assets/app.ico
与 GUI 徽标保持一致：橙色圆角底 + 官方 G8 徽标。

运行: python make_icon.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
os.makedirs(ASSETS, exist_ok=True)

BRAND = (224, 120, 32, 255)      # #e07820
BRAND_DARK = (184, 95, 18, 255)  # #b85f12

MARK_SRC = os.path.join(ASSETS, "logo_mark.png")

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 圆角底 + 内描边
pad = int(S * 0.055)
radius = int(S * 0.225)
d.rounded_rectangle([pad, pad, S - pad, S - pad], radius=radius, fill=BRAND)
inset = int(S * 0.115)
d.rounded_rectangle([inset, inset, S - inset, S - inset],
                    radius=int(radius * 0.78), outline=BRAND_DARK, width=int(S * 0.015))

# 叠加官方徽标（透明底白色版）
if os.path.exists(MARK_SRC):
    mark = Image.open(MARK_SRC).convert("RGBA")
    target = int(S * 0.60)
    mark = mark.resize((target, target), Image.LANCZOS)
    img.alpha_composite(mark, ((S - target) // 2, (S - target) // 2))
else:
    # 兜底：无 LOGO 资源时用文字
    from PIL import ImageFont
    font = ImageFont.truetype("seguibl.ttf", int(S * 0.42))
    bbox = d.textbbox((0, 0), "G8", font=font)
    d.text(((S - bbox[2] + bbox[0]) / 2, (S - bbox[3] + bbox[1]) / 2),
           "G8", font=font, fill=(255, 255, 255, 255))

ico_path = os.path.join(ASSETS, "app.ico")
img.save(ico_path, format="ICO",
         sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
img.resize((256, 256), Image.LANCZOS).save(os.path.join(ASSETS, "app.png"))
print("OK:", ico_path)
print("OK:", os.path.join(ASSETS, "app.png"))
