"""Copy the six supplied Ohuhu references losslessly and build a labelled board."""
from pathlib import Path
import hashlib
import json
import shutil

from PIL import Image, ImageDraw, ImageFont, ImageOps


SOURCE = Path(r"C:\Users\Lenovo\Desktop\纸类产品AI辅助\ohuhu素材")
DESTINATION = Path(__file__).resolve().parent / "resources"
REFERENCES = [
    ("3d3b5a1c905f8ebea928f891c11e9e6abdfdbd481940036db697d4afbe4a0bbd.png", "01: Bright tropical shapes / black", "黑底、高饱和扁平热带图形：植物、仙人掌、鸟、花、月亮与艺术元素。"),
    ("9f35f467c2c0c0a80540b326433c618fd2da6ffa65c1541840ee566b944cb3d7.png", "02: Colorful sparse line art / white", "白底、疏朗彩色线稿：棕榈树、花、艺术与音乐、云太阳、纸飞机。"),
    ("85d085729d8b4d6c334383e8577833e8fb41a5f6b3d59f72f6804ccb8fab270e.png", "03: Supplied Ohuhu logo", "用户提供的绿色 Ohuhu 品牌标识，O 内有叶片。保留原标识的形状与比例。"),
    ("8147e1c2c0de835cc20c712f7a2f7e0614bc3729b40f743a6ad5d751b1e0c661.png", "04: Skateboarding chameleon / playful doodles", "带透明通道的黑色线稿密集涂鸦，本联系表使用白底展示；用户指定的滑板变色龙（卷尾、橙帽、绿色身体与滑板）是优先保留的品牌图案，另有音乐运动游戏与艺术元素、彩色点缀。图中 Hilo Acrylic Markers 和 12 Colors 仅属于原案例。"),
    ("24479d4e0e819a9f052415409ef6c2057d324b02faf97d90433d6d72a646f058.png", "05: Outdoor frog doodles / white", "白底户外探索青蛙涂鸦、露营、植物、自行车与画具；黑色线稿与彩色点缀，含 draw feel empower 示例文案。"),
    ("bce58858a80b1f0fd57df2b190bbd4f4420cbf558e62b7c6ea233637bd35e8a1.png", "06: Botanical line art / white", "白底热带植物黑色轮廓、稀疏柔和彩色、木槿与叶片、橙色圆角版面分区。图中 Kalalau Acrylic Markers 和 60 Colors 仅属于原案例。"),
]


def main():
    DESTINATION.mkdir(parents=True, exist_ok=True)
    font = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 23)
    heading = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 27)
    board = Image.new("RGB", (1536, 1860), "white")
    draw = ImageDraw.Draw(board)
    draw.text((24, 12), "OHUHU - BRAND / STYLE REFERENCES (01-06)", font=heading, fill="#165b3e")
    draw.text((24, 48), "Example wording and color counts are not current product requirements.", font=font, fill="#414141")
    records = []
    for index, (filename, label, description) in enumerate(REFERENCES, 1):
        original = SOURCE / filename
        resource = "ohuhu-{:02d}.png".format(index)
        shutil.copyfile(original, DESTINATION / resource)
        with Image.open(original) as source_image:
            source_image.load()
            width, height = source_image.size
            x = ((index - 1) % 2) * 768
            y = 96 + ((index - 1) // 2) * 588
            draw.rectangle((x + 12, y, x + 755, y + 573), outline="#d5ded8", width=2)
            draw.text((x + 26, y + 12), label, font=font, fill="#21352a")
            fitted = ImageOps.contain(source_image.convert("RGBA"), (720, 500), Image.Resampling.LANCZOS)
            board.paste(fitted, (x + (768 - fitted.width) // 2, y + 58 + (500 - fitted.height) // 2), fitted)
            records.append({"id": "ohuhu-{:02d}".format(index), "resource": resource,
                            "original_filename": filename,
                            "sha256": hashlib.sha256(original.read_bytes()).hexdigest(),
                            "width": width, "height": height, "mode": source_image.mode,
                            "role": "brand_logo" if index == 3 else "brand_style",
                            "description": description})
    shutil.copyfile(SOURCE / REFERENCES[2][0], DESTINATION / "ohuhu-logo.png")
    mascot_crop = (458, 202, 615, 362)
    with Image.open(SOURCE / REFERENCES[3][0]) as mascot_source:
        mascot_source.crop(mascot_crop).save(DESTINATION / "ohuhu-skateboard-mascot.png")
    board.save(DESTINATION / "ohuhu-style-board.png")
    metadata = {
        "brand": "Ohuhu", "purpose": "品牌标识与视觉风格参考，不是产品结构或尺寸依据。",
        "usage": "根据用户要求选择合适的视觉方向，不要把六种风格机械叠加。用户指定主题、品类、数量、尺寸优先；不得自动套用原图的系列名、12/60色、示例文案。",
        "board_resource": "ohuhu-style-board.png", "logo_resource": "ohuhu-logo.png",
        "mascot_resource": "ohuhu-skateboard-mascot.png",
        "mascot": {"description": "按用户称呼的滑板变色龙；保留原图角色形象和滑板，不重新杜撰角色。", "source_resource": "ohuhu-04.png", "crop_box": list(mascot_crop), "preserves_source_pixels_and_alpha": True},
        "references": records,
    }
    (DESTINATION / "ohuhu-references.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Imported six byte-identical PNGs, original logo, 1536 x 1860 style board, and metadata.")


if __name__ == "__main__":
    main()
