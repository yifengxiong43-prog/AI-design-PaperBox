"""Preserve supplied transparent sketches and make a readable white-backed board."""
from pathlib import Path
import hashlib
import json
import shutil

from PIL import Image, ImageDraw, ImageFont, ImageOps


SOURCE = Path(r"C:\Users\Lenovo\Desktop\纸类产品AI辅助\草图素材")
DESTINATION = Path(__file__).resolve().parent / "resources"
REFERENCES = [
    ("5ea3c3052ba05f79662dca8fa44dded6fe5c0fc89018a6cb9ef5f3e46e205afc.png", "01: Rounded carry case / removable modules", "圆角胶囊形提箱、翻盖与提手一体视觉，内部可抽取分盒。草图结合彩色外观、正侧视图、开启透视和分盒示意，表达圆润轮廓与模块收纳。"),
    ("32cd5bab287c0c625e2a7566e4981332e4402c6d1da63d98592a22f98e431ff3.png", "02: Upright carry case / drawers and dividers", "竖式圆肩提箱，前翻盖展开；提供多层抽屉与可取出隔板分盒两种结构探索。草图用外观上色、前后侧视与抽屉开启透视说明携带、取用和内部布局。"),
    ("404ed9319b5ef94b48425958337a88f82f45519ecb0e3833fc62afc5e24be7f4.png", "03: Twin-column layered trays / flat storage", "扁平矩形盒的双列多层托盘，抽出时完整露出平放笔身；中部品牌腰封结合植物纹样。草图用封面、封闭透视、托盘抽离分解视图表达分层与数量组织。"),
    ("801c26efd36a33bba0f558e400b227c7c4252a9cf82a52b8ee8ed2b2c424e360.png", "04: Linked expanding trays / transformation", "连杆连接多层托盘，从紧凑叠合盒变成阶梯展开或左右完全摊开；蓝色箭头串联闭合、开启、展开状态，标注转轴与分仓。借鉴结构变形和动作叙事，不能直接照搬原例载量、尺寸或默认横放。"),
    ("b8c65319c156fbcfbd057f1c0823f5c3c3df74d245e1a6f672522da5a0d8475c.png", "05: Leaf-shaped opening wings / layered drawers", "龟背叶轮廓成为左右异形开启侧翼，与顶翻盖、提取缺口和多层抽屉组合；主题图案参与结构。草图把彩色闭合外观、顶视叶片、打开透视与内托独立展示。"),
]


def main():
    DESTINATION.mkdir(parents=True, exist_ok=True)
    font = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 25)
    heading = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 31)
    board = Image.new("RGB", (1920, 2310), "white")
    draw = ImageDraw.Draw(board)
    draw.text((26, 12), "OHUHU - STRUCTURE / DESIGN SKETCH REFERENCES (01-05)", font=heading, fill="#165b3e")
    draw.text((26, 55), "Recalculate all counts, dimensions and pen orientation for the current product.", font=font, fill="#414141")
    records = []
    for index, (filename, label, description) in enumerate(REFERENCES, 1):
        original = SOURCE / filename
        resource = "sketch-{:02d}.png".format(index)
        shutil.copyfile(original, DESTINATION / resource)
        with Image.open(original) as source_image:
            source_image.load()
            x = ((index - 1) % 2) * 960
            y = 108 + ((index - 1) // 2) * 728
            draw.rectangle((x + 12, y, x + 947, y + 710), outline="#d5ded8", width=2)
            draw.text((x + 25, y + 14), label, font=font, fill="#21352a")
            fitted = ImageOps.contain(source_image.convert("RGBA"), (920, 650), Image.Resampling.LANCZOS)
            board.paste(fitted, (x + (960 - fitted.width) // 2, y + 55 + (650 - fitted.height) // 2), fitted)
            records.append({"id": "sketch-{:02d}".format(index), "resource": resource,
                            "original_filename": filename, "sha256": hashlib.sha256(original.read_bytes()).hexdigest(),
                            "width": source_image.width, "height": source_image.height, "mode": source_image.mode,
                            "role": "structure_and_sketch_reference", "description": description})
    board.save(DESTINATION / "sketch-style-board.png")
    metadata = {
        "purpose": "用户提供的结构创意与设计草图表达参考；透明线稿须在白底上阅读。",
        "usage": "借鉴结构创新、开启动作、部件分解、多视图与少量品牌上色，不能只换颜色或图案。原图300支、尺寸、分仓数均为历史案例；必须按当前笔种、数量、单支长度、包装上限和放置方向重新设计。丙烯笔只允许竖放；不从草图估计或沿用笔长。",
        "board_resource": "sketch-style-board.png", "references": records,
    }
    (DESTINATION / "sketch-references.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Imported five byte-identical sketches and a white-backed 1920 x 2310 board.")


if __name__ == "__main__":
    main()
