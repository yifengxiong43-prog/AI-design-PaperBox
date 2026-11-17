"""Build the embedded, source-linked retrieval catalogue without an API call.

Facts are verbatim source fields; summaries are selected extracts, not claimed
LLM analysis. Rules and cautions are curated design guidance, explicitly marked.
Run with --original-dir to also verify the user's five files and eleven PNGs.
--check verifies all input hashes and expected output without writing any file.
"""
import argparse
import hashlib
import json
import zipfile
from copy import deepcopy
from pathlib import Path
from stable_source_ids import assign_stable_ids, stamp_source_metadata, validate_source_metadata


ROOT = Path(__file__).resolve().parent / "resources"
MATERIALS = "纸质产品包装盒材料工艺配件汇总表/"
CASES = "包装案例与问题收集/"
TERMS = "白卡 FBB 灰板 瓦楞 牛卡 黑卡 铜版纸 特种纸 纸浆 模塑 插格 插舌 抽屉 翻盖 书本 手提 提手 提带 磁铁 磁吸 卡扣 铰链 连杆 托盘 分仓 分组 模块 展开 折叠 旋转 层叠 竖放 横放 平铺 透明 线稿 草图 颜色 插画 线描 涂鸦 热带 植物 变色龙 滑板 扶桑花 鸡蛋花 椰树 丙烯 酒精 彩铅 马克笔 笔帽 笔尖 色码 色系 Ohuhu Hilo Honolulu Hanauma Kailua Arrtx Grabie Shuttle FIZZ mideer 儿童 礼盒 圣诞 环保 禁塑 塑料 EPE EVA PET PP OPP 亚克力 泡沫 玻璃 防潮 防震 缓冲 划痕 挤压 跌落 振动 防滑 固定 定位 刮擦 承重 提拉 撕裂 拉力 耐磨 防爆线 压痕 模切 糊盒 胶水 覆膜 上光 烫金 UV 印刷 白墨 专色 四色 印墨 厚度 克重 纤维 楞向 成本 打样 运输 测试 安规 FSC ISTA FBA 合规 认证".split()
PRIORITY = ["材料/类型", "材料/配件", "保护材料", "工艺", "适用场景", "适配材料", "主要特点", "主要限制/风险", "限制/风险", "主要限制", "借鉴点说明", "问题描述", "解决办法", "内部结构描述", "盒型", "固定/连接方式", "选型建议", "验收项目", "打样/验收关注", "需要确认的内容", "注意事项", "防护方式", "具体材质", "包装结构", "作用（例：防护、礼盒、展示）"]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8"))


def tags(text):
    return [word for word in TERMS if word.lower() in text.lower()]


def facts(text):
    result = []
    for paragraph in text.split("\n\n"):
        if not paragraph.strip() or paragraph.startswith("原表字段"):
            continue
        # Keeping the entire paragraph preserves contiguous, auditable quotes.
        label = paragraph.split("：", 1)[0] if "：" in paragraph else "原文"
        if len(label) > 35:
            label = "原文"
        result.append({"Label": label, "Quote": paragraph})
    return result


def entry(record):
    raw = record["Text"]
    extracted = facts(raw)
    ordered = sorted(enumerate(extracted), key=lambda x: (PRIORITY.index(x[1]["Label"]) if x[1]["Label"] in PRIORITY else len(PRIORITY), x[0]))
    selected, used = [], 0
    for _, field in ordered:
        quote = field["Quote"]
        if used >= 180:
            break
        room = 200 - used
        if len(quote) > room:
            # Never end an extract halfway through a negative or a dimension.
            boundary = max(quote.rfind(char, 0, room) for char in "。；\n")
            if boundary < 0:
                continue
            excerpt = quote[:boundary + 1].strip()
        else:
            excerpt = quote
        if not excerpt:
            continue
        selected.append(excerpt)
        used += len(excerpt) + 1
    cautions = []
    group = record["Group"]
    if group in ("历史包装", "产品案例", "市场参考", "网络案例", "案例分析"):
        cautions.append("历史容量、尺寸、价格、供应商和打样期仅属于原案例；当前参数与报价需另行计算或确认。")
    if group == "材料与工艺":
        cautions.append("资料列出的常见规格与选型建议不是当前实测值；厚度、工艺兼容与验收限值须通过供应商和打样确认。")
    if group in ("市场参考", "品牌研究", "网络案例"):
        cautions.append("可借鉴视觉或使用动作，不代表其材料已被用户材料表允许；不得将原图数量或笔尖宽度作为当前笔长/笔径。")
    if group == "历史问题":
        cautions.append("先判断失效机制是否适用；原案例的处理材料不得绕过当前禁用项或直接认定已验证有效。")
    if group == "案例分析":
        cautions.append("这是对用户原资料的二次分析；图像观察与设计推导分开标注，不是原文件原句，也不是已验证的制造方案。")
    if "磁铁" in raw or "磁吸" in raw:
        cautions.append("磁吸非必选；用户禁用磁铁时不能推荐，儿童产品须复核小零件与脱落风险。")
    if record["Id"] == CASES + "现有产品包装/2":
        cautions.append("此圣诞组合产品的重量栏存在类似支数的数值，不作为克重依据；历史成本有其原报价条件。")
    if record["Id"] == "合规与测试要求":
        cautions.append("250转/分、3小时是企业调整条件；跌落高度取决于实际毛重，边界归属和标准现行版本须与实验室核对。")
    if record["Id"] == CASES + "包装问题记录/3":
        cautions.append("此例是袋口热封失效；不是要求每种纸盒真空包装。")
    if record["Id"].endswith("/extra"):
        cautions.append("附加图片尚未自动对应到具体行，必须核对原件后才能据此引用产品事实。")
    return {
        "SourceId": record["Id"], "Title": record["Title"], "Kind": group,
        "Summary": "；".join(selected), "Tags": list(dict.fromkeys([group] + tags(record["Title"] + " " + raw))),
        "Cautions": cautions, "Facts": extracted,
        "ImageResources": record.get("Images", []),
        "Provenance": "原文字段摘录；摘要和关键词由规则生成；注意事项为人工整理的方法建议。",
    }


def rule(rule_id, text, source_ids, words, provenance="根据资料整理的设计建议；不等同实测、认证或源文原句"):
    return {"Id": rule_id, "Text": text, "SourceIds": source_ids, "Tags": words.split(), "Provenance": provenance}


def rules():
    return [
        rule("scope", "AI提供可供打样讨论的方案；人工确认尺寸、材料工艺、成本、合规与生产。项目分工与日期不进入包装画面。", ["AI辅助纸类包装设计项目方案"], "范围 打样 人工"),
        rule("source-whitelist", "物料及工艺名称须对应材料表的实际条目；市场案例和官网出现的材料不自动加入许可范围。", [MATERIALS + "材料汇总/说明"], "材料 来源 白名单"),
        rule("paper-thickness", "克重不等于厚度。计算需要选定壁厚并标注为设计假设，待供应商实测；纸纹、压痕和折线爆裂须验证。", [MATERIALS + "材料汇总/5", MATERIALS + "材料汇总/6"], "厚度 克重 白卡 FBB 压痕"),
        rule("printing", "底材底色影响插画还原；按底材选择四色或专色/白墨，确认印刷、表面处理、模切压痕的顺序及附着。", [MATERIALS + "工艺适配/5", MATERIALS + "工艺适配/6"], "印刷 四色 白墨 专色 色差"),
        rule("handles", "提手须写固定方式、孔位和局部补强，验证静态承重、动态和重复提拉、孔位撕裂；未知产品重量不宣称承重合格。", [MATERIALS + "提手与配件/说明"], "提手 提拉 承重 孔位"),
        rule("transport-crush", "挤压刮伤风险需落到外箱抗压、受力路径和接触面保护；使用原案例泡沫方案前先核对用户是否禁塑。", [CASES + "包装问题记录/2"], "运输 挤压 划痕 外箱 防护"),
        rule("transport-clearance", "空隙过大可造成跌落破裂；按笔径、笔长设计定位和缓冲，测试拿取与运输两种状态。纸内托不未经测试宣称等效EPE。", [CASES + "包装问题记录/4", MATERIALS + "保护材料/说明"], "内托 间隙 缓冲 跌落 定位"),
        rule("moisture", "受潮案例的根因是袋口热封失效；仅在采用相同封装时检查热封工艺。纸盒方案应按实际运输湿度确认保护，不能机械加真空袋。", [CASES + "包装问题记录/3"], "受潮 防潮 热封 真空"),
        rule("test-conditions", "参照资料ISTA 1A-2014的毛重区间确定跌落高度，顺序1角、3棱、6面共10次；未知毛重不可默认76cm。250转/分、3小时为企业调整的振动条件。", ["合规与测试要求"], "ISTA 跌落 振动 毛重 测试"),
        rule("verification", "验收应有检查对象、方法、待协商通过条件；白样验证装配、开合和稳定性。几何包围盒不证明铰链运动、承重或运输通过。", [MATERIALS + "选型与验收/说明", MATERIALS + "选型与验收/6"], "验收 结构 白样 装配"),
        rule("cost-history", "历史包装表中的2023年规格、成本、供应商和打样时间只用于比较，不能作为当前产品的尺寸、报价或交期。", ["马克笔包装分类汇总表 20230628/千岸/14"], "成本 历史 供应商 报价"),
        rule("christmas-weight", "组合礼盒重量栏出现类似清单数量的值，不转换为克重；其混合产品尺寸不能给全部笔种套用。", [CASES + "现有产品包装/2"], "圣诞 重量 组合"),
        rule("visibility", "可旋转或透明不等于一次全部可见。全展开应把每组实际笔身摆出，避免前后遮挡；承托边挡遮住一段笔身时如实说明。", ["web-bamboo", "web-acrylic-organizer"], "展开 全部 可见 旋转 透明"),
        rule("caps-not-full-body", "原36支丙烯盒只露出多排笔帽，不是完整笔身可见模板；连杆草图的阶梯中间态也不能作为展开终态。借鉴案例必须先列不满足当前需求之处。", ["local-analysis/acrylic36-caps-counterexample", "local-analysis/linkage-full-spread"], "36 丙烯 展开 遮挡 草图"),
        rule("computed-open-repair", "逐组计算实际宽度和展开投影。选择并排展开时可声明OpenLayout=side-by-side，由程序计算单元X位置和4mm单元间距；这不是4mm笔间隙。必须同步核对连接、Parts、Motion、支撑和开合路径，不允许只改坐标通过复核；一体连接要求不能偷换为独立移出。", ["local-analysis/removable-upright-modules", "web-v088-tombow-accordion-case"], "并排 坐标 回修 连接 展开", "基于案例提出的本客户端计算方法，不是原案例尺寸或机构验证"),
        rule("ohuhu-pen", "Ohuhu不同系列笔帽、笔尖、笔身不同；Hilo的1–6mm是笔迹宽不是笔径。按用户提供的笔长和笔径构型，官网色数不覆盖当前支数。", ["web-hilo", "web-honolulu", "web-hanauma"], "Ohuhu Hilo Honolulu Hanauma 笔径 笔长 笔帽"),
        rule("paper-adaptation", "可借鉴多页单排、色码与分组取笔；将软包转为纸结构时须重新计算页宽、叠层深度、展开位置、连接和支撑。", ["web-hanauma", "web-hilo"], "展开 分组 单排 色码 纸结构"),
        rule("requirements", "完整保留当前用户每条要求、禁止项、保留项和修改项；否定句不能只取关键词。新旧要求冲突要显式解释，当前笔类、数量、长度、直径不被历史案例覆盖。", [], "需求 否定 修改 保留", "用户明确要求与客户端设计方法；不是附件原文"),
        rule("net-width", "净排宽=支数×直径+整排总余量+(支数−1)×(单间隙+隔板厚度)。12×15+4=184mm仅在4mm为整排总余量且无单间隙/隔板时成立；外壁另计。", [], "尺寸 直径 长度 支数 间隙", "用户明确要求与程序几何协议；4mm是可调整设计选值，不是所有产品通用实测值"),
        rule("orientation", "丙烯笔只允许竖直放置；酒精笔和彩铅可横或竖。中盒/黄盒等外包装尺寸的各边预留30–50mm，再检验产品盒能否装入。", [], "丙烯 酒精 彩铅 方向 外包装 中盒 黄盒", "用户明确要求；不是行业所有笔种的通用规则"),
        rule("same-assembly", "闭合、展开、草图必须是同一组部件、数量、尺寸和连接关系。每个盒子、内托、连接件分别标注尺寸并保持笔长比例，不能靠数量标签代替被遮挡的笔。", [], "草图 展开 部件 尺寸 一致", "用户明确要求与程序几何协议"),
        rule("sketch-transparency", "草图用细线多视图、少量局部上色、运动箭头与逐部件尺寸；透明指PNG背景透明，不是纸板透明，也不画棋盘格背景。", [], "草图 透明 PNG 线稿 多视图", "用户草图参考与明确要求；图片出处见Images"),
        rule("brand-use", "Ohuhu风格按主题选择，不把所有参考机械拼贴；指定滑板变色龙时保留原角色特征，历史12/60/300等数量及系列名不直接搬到当前包装。", [], "Ohuhu 变色龙 滑板 风格 品牌", "用户品牌参考与明确要求；图片出处见Images"),
    ]


def verify_files(original_dir):
    manifest = read("source-manifest.json")
    verified = []
    with zipfile.ZipFile(ROOT / "original-sources.zip") as archive:
        for source in manifest["sources"]:
            data = archive.read(source["file"])
            assert sha(data) == source["sha256"], "Embedded original changed: " + source["file"]
            assert len(data) == source["bytes"]
            if original_dir:
                assert sha((original_dir / source["file"]).read_bytes()) == sha(data), "User original differs: " + source["file"]
            verified.append({"File": source["file"], "Sha256": sha(data), "Bytes": len(data)})
        # Original embedded media remain present inside the five retained documents.
        for source in manifest["sources"]:
            with zipfile.ZipFile(__import__("io").BytesIO(archive.read(source["file"]))) as doc:
                for media in source["media"]:
                    assert sha(doc.read(media["part"])) == media["sha256"]
                    assert (ROOT / (media["resource"] + ".jpg")).exists()
    return verified


def image_entries(original_dir):
    result = []
    for filename, subdir in [("sketch-references.json", "草图素材"), ("ohuhu-references.json", "ohuhu素材")]:
        metadata = read(filename)
        for reference in metadata["references"]:
            data = (ROOT / reference["resource"]).read_bytes()
            digest = sha(data)
            if "sha256" in reference:
                assert digest == reference["sha256"], "Image changed: " + reference["resource"]
                if original_dir:
                    assert sha((original_dir / subdir / reference["original_filename"]).read_bytes()) == digest
            result.append({
                "Id": reference["id"], "Resource": reference["resource"],
                "SourceFile": reference["original_filename"], "Sha256": digest,
                "Summary": reference["description"], "Tags": tags(reference["description"]) + [subdir],
                "Cautions": [metadata["usage"]],
                "Provenance": "已保存的用户图片人工观察摘要，来自" + filename + "；不从图片推定精确尺寸或材料。",
            })
    return result


def build(original_dir=None, check=False):
    source_files = read("source-manifest.json")["sources"]
    for name in ("knowledge.json", "web-knowledge.json"):
        existing = read(name)
        if check:
            validate_source_metadata(existing, source_files)
        else:
            migrated = stamp_source_metadata(assign_stable_ids(deepcopy(existing), existing), source_files)
            if migrated != existing:
                indent = None if name == "knowledge.json" else 2
                suffix = "" if indent is None else "\n"
                (ROOT / name).write_text(json.dumps(migrated, ensure_ascii=False, indent=indent) + suffix, encoding="utf-8")
    records = read("knowledge.json") + read("web-knowledge.json")
    validate_source_metadata(records, source_files)
    source_ids = {record["Id"] for record in records}
    assert len(source_ids) == len(records), "Duplicate source IDs"
    entries = [entry(record) for record in records]
    guidance = rules()
    for item in guidance:
        assert set(item["SourceIds"]) <= source_ids, "Invalid source in rule " + item["Id"]
    for record, item in zip(records, entries):
        assert all(fact["Quote"] in record["Text"] for fact in item["Facts"])
        assert all((ROOT / (name + ".jpg")).exists() for name in item["ImageResources"])
    inputs = ["knowledge.json", "web-knowledge.json", "packaging-design-method.md", "sketch-references.json", "ohuhu-references.json", "source-manifest.json"]
    result = {
        "Version": "1.0", "CorpusHash": sha((ROOT / "knowledge.json").read_bytes() + (ROOT / "web-knowledge.json").read_bytes()),
        "Method": "逐来源字段摘录、检索标签与人工整理的设计规则；不是模型训练或对每次用户需求的预先理解。",
        "InputHashes": {name: sha((ROOT / name).read_bytes()) for name in inputs},
        "SourceFiles": verify_files(original_dir), "Entries": entries, "Rules": guidance,
        "Images": image_entries(original_dir),
    }
    target = ROOT / "knowledge-analysis.json"
    expected = (json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    if check:
        if not target.exists() or target.read_bytes() != expected:
            raise SystemExit("Embedded knowledge analysis is stale or missing; run desktop/build_knowledge_analysis.py to rebuild it.")
    else:
        target.write_bytes(expected)
    print(json.dumps({"entries": len(entries), "rules": len(guidance), "images": len(result["Images"]), "original_files": len(result["SourceFiles"]), "bytes": target.stat().st_size, "corpus_hash": result["CorpusHash"], "originals_compared": original_dir is not None, "check_only": check}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-dir", type=Path)
    parser.add_argument("--check", action="store_true", help="Fail on stale analysis without rewriting it")
    args = parser.parse_args()
    build(args.original_dir, args.check)
