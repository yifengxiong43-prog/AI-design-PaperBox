from pathlib import Path
import runpy
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'C'
runpy.run_path(str(ROOT / 'make_structure_sketch_v11.py'))
img = Image.open(OUT / '结构草图_C-v11.png').convert('RGB')
open_img = Image.open(OUT / 'comparison-open-v13.png').convert('RGB')
open_img.thumbnail((1055, 630))
img.paste(open_img, (55, 206))
d = ImageDraw.Draw(img)
font_path = 'C:/Windows/Fonts/msyh.ttc'
title = ImageFont.truetype(font_path, 43, index=1)
h2 = ImageFont.truetype(font_path, 29, index=1)
small = ImageFont.truetype(font_path, 19)
green, dark = '#125c49', '#173f36'
d.rectangle((0, 0, 2000, 112), fill=green)
d.text((54, 24), 'C｜模块调色盘盒 · 结构计算草图 v13', font=title, fill='white')
d.rectangle((50, 150, 1010, 192), fill='#f5f7f5')
d.text((62, 160), '01 展开与取笔状态｜同一 C-master-v13-still.blend', font=h2, fill=green)
d.rectangle((50, 744, 1100, 850), fill='#f5f7f5')
d.text((62, 761), '先抽中间底盒 → 中间底盒两侧壁下翻 → 左右纸盘移出；小纸盘两边保持立起', font=small, fill=dark)
d.text((62, 790), '上方挡板固定；右侧多余独立板删除；下方带印刷挡板与底盒连体', font=small, fill=dark)
d.rounded_rectangle((38, 850, 1115, 1290), radius=12, fill='#edf5ef', outline='#c7dad0', width=2)
d.text((62, 872), '04 中间底盒侧壁下翻｜小纸盘两侧保持立起', font=h2, fill=green)
d.text((75, 930), '收纳 / 抽出前：中间底盒侧壁竖直', font=small, fill=green)
d.rectangle((115, 985, 145, 1145), fill='#69b39a', outline='#3f7464', width=2)
d.rectangle((145, 1125, 500, 1145), fill='#f8f5e8', outline='#55776c', width=2)
d.rectangle((170, 1040, 200, 1125), fill='#d7eee2', outline='#55776c', width=2)
d.text((220, 1060), '小纸盘侧壁保持立起', font=small, fill=dark)
d.text((620, 930), '取用：仅中间底盒侧壁向外下翻', font=small, fill=green)
d.rectangle((650, 1085, 1040, 1115), fill='#69b39a', outline='#3f7464', width=2)
d.rectangle((650, 1015, 680, 1085), fill='#d7eee2', outline='#3f7464', width=2)
d.rectangle((705, 1015, 735, 1085), fill='#d7eee2', outline='#55776c', width=2)
d.text((750, 1030), '左右小纸盘侧边不展开', font=small, fill=dark)
d.text((62, 1230), '中间底盒侧壁仅为抽盘让路；小纸盘两侧保持立起，从上方取笔；下方连体挡板不动', font=small, fill=dark)
d.text((1775, 1390), 'C-MODULAR-SKETCH-13', font=small, fill='#6c8b7d')
out = OUT / '结构草图_C-v13.png'
img.save(out, quality=95)
print(out)
