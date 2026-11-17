"""Import reference data with original bytes and auditable text/media coverage."""
import hashlib
import io
import json
import posixpath
import re
import sys
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image, ImageOps
from openpyxl import load_workbook
from stable_source_ids import assign_stable_ids, validate_stable_ids

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tmp/archive_review_20261028/纸类产品AI辅助'
OUT = ROOT / 'desktop/resources'
OUT.mkdir(exist_ok=True)
previous_records = json.loads((OUT / 'knowledge.json').read_text(encoding='utf-8')) if (OUT / 'knowledge.json').exists() else []
records, images, manifest, extraction = [], {}, [], []
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
R = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
XDR = '{http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def image_resource(data):
    key = 'img_' + sha(data)[:16]
    if key not in images:
        im = ImageOps.exif_transpose(Image.open(io.BytesIO(data)))
        if im.mode in ('RGBA', 'LA'):
            bg = Image.new('RGB', im.size, 'white')
            bg.paste(im, mask=im.getchannel('A'))
            im = bg
        im = im.convert('RGB')
        im.thumbnail((1400, 1400))
        im.save(OUT / (key + '.jpg'), quality=87)
        images[key] = True
    return key


def merged_values(sheet):
    values = {(c.row, c.column): c.value for row in sheet for c in row if c.value is not None}
    for area in sheet.merged_cells.ranges:
        value = sheet.cell(area.min_row, area.min_col).value
        if value is not None:
            for row in range(area.min_row, area.max_row + 1):
                for col in range(area.min_col, area.max_col + 1):
                    values.setdefault((row, col), value)
    return values


def is_picture_formula(value):
    return isinstance(value, str) and 'DISPIMG(' in value


def cell_image_ids(archive):
    if 'xl/cellimages.xml' not in archive.namelist():
        return {}
    rels = {r.attrib['Id']: posixpath.normpath(posixpath.join('xl', r.attrib['Target']))
            for r in ET.fromstring(archive.read('xl/_rels/cellimages.xml.rels'))}
    result = {}
    for picture in ET.fromstring(archive.read('xl/cellimages.xml')).iter(XDR + 'pic'):
        name, blip = picture.find('.//' + XDR + 'cNvPr'), picture.find('.//' + A + 'blip')
        result[name.attrib['name']] = image_resource(archive.read(rels[blip.attrib[R + 'embed']]))
    return result


source_files = sorted(f for f in SOURCE.iterdir() if f.suffix in ('.docx', '.xlsx'))
assert len(source_files) == 5, 'Expected the five supplied original documents'
with ZipFile(OUT / 'original-sources.zip', 'w', compression=ZIP_DEFLATED) as originals:
    for f in source_files:
        originals.write(f, f.name)

for f in source_files:
    first_record = len(records)
    entry = dict(file=f.name, sha256=sha(f.read_bytes()), bytes=f.stat().st_size,
                 media=[], sheets=[], text_cells=0, image_formula_cells=0)
    full = dict(file=f.name, sha256=entry['sha256'])
    with ZipFile(f) as z:
        for name in z.namelist():
            if '/media/' in name and not name.endswith('/'):
                data = z.read(name)
                entry['media'].append(dict(part=name, sha256=sha(data), resource=image_resource(data)))
        if f.suffix == '.docx':
            parts = []
            for name in z.namelist():
                if name.startswith('word/') and name.endswith('.xml'):
                    root = ET.fromstring(z.read(name))
                    paragraphs = [''.join(t.text or '' for t in p.iter(W + 't')) for p in root.iter(W + 'p')]
                    paragraphs = [p for p in paragraphs if p.strip()]
                    if paragraphs:
                        parts.append(dict(part=name, paragraphs=paragraphs))
            full['parts'] = parts
            text = '\n\n'.join(p for part in parts for p in part['paragraphs'])
            records.append(dict(Id=f.stem, Title=f.stem, Group='测试与项目', Source=f.name,
                                Text=text, Images=[m['resource'] for m in entry['media']]))
            entry['paragraphs'] = sum(len(p['paragraphs']) for p in parts)
        else:
            w = load_workbook(f, data_only=False)
            cached = load_workbook(f, data_only=True)
            cell_images = cell_image_ids(z)
            full['sheets'] = []
            assigned = set()
            for s in w:
                material = '材料工艺' in f.name
                start = 5 if material else (3 if s.title == 'new' else 2)
                header_row = 4 if material else 1
                values = merged_values(s)
                cells = [dict(cell=c.coordinate, value=str(c.value), type=c.data_type,
                              cached=str(cached[s.title][c.coordinate].value) if c.data_type == 'f' else None,
                              comment=c.comment.text if c.comment else None)
                         for row in s for c in row if c.value is not None]
                full['sheets'].append(dict(name=s.title, cells=cells,
                                          merged=[str(a) for a in s.merged_cells.ranges]))
                entry['text_cells'] += len(cells)
                entry['sheets'].append(dict(name=s.title, cells=len(cells), hidden=s.sheet_state))
                headers = []
                for col in range(1, s.max_column + 1):
                    parts = [str(values.get((row, col), '')) for row in range(header_row, start)]
                    headers.append(' / '.join(dict.fromkeys(p for p in parts if p)) or '补充信息')
                if material:
                    preface = '\n'.join(str(c.value) for row in s.iter_rows(max_row=3) for c in row if c.value is not None)
                    records.append(dict(Id=f'{f.stem}/{s.title}/说明', Title=s.title+' · 使用说明', Group='材料与工艺',
                                        Source=f'{f.name} · {s.title} · 第1–3行', Text=preface, Images=[]))
                rows = {}
                for n in range(start, s.max_row + 1):
                    row_values = {col: str(value) for (row, col), value in values.items()
                                  if row == n and not is_picture_formula(value) and str(value).strip()}
                    if not row_values:
                        continue
                    title_col = 2 if material and s.title in ('总览', '材料汇总', '提手与配件') else 1 if material or '案例' in f.name else 2 if s.title == 'new' else 3
                    title = row_values.get(title_col) or next(iter(row_values.values()))
                    if s.title == '市场参考包装':
                        title = row_values.get(1, '未标品牌') + ' · ' + row_values.get(2, '包装')
                    group = '产品案例' if s.title == '现有产品包装' else '市场参考' if s.title == '市场参考包装' else '历史问题' if s.title == '包装问题记录' else '材料与工艺' if material else '历史包装'
                    row = dict(Id=f'{f.stem}/{s.title}/{n}', Title=title.split('\n')[0][:65], Group=group,
                               Source=f'{f.name} · {s.title} · 第{n}行',
                               Text='\n\n'.join(f'{headers[col-1]}：{value}' for col, value in row_values.items()), Images=[])
                    if group == '产品案例':
                        row['Brief'] = dict(Name=row_values.get(1,''), Size=row_values.get(4,''), Quantity=row_values.get(6,''),
                                            Cost=row_values.get(17,''), Market=row_values.get(15,'')+' / '+row_values.get(16,''), Style='简洁自然',
                                            Needs='参考现有包装，改善取用与收纳体验。\n现有内部结构：'+row_values.get(11,''))
                    rows[n] = row
                    records.append(row)
                if rows:
                    first = rows[min(rows)]
                    first['Text'] = ('原表字段（仅表示列名，空白字段并非已提供数据）：' + '；'.join(headers)
                                     + '\n\n' + first['Text'])
                for im in s._images:
                    rownum = im.anchor._from.row + 1
                    previous = [n for n in rows if n <= rownum]
                    if previous:
                        key = image_resource(im._data())
                        assigned.add(key)
                        rec = rows[max(previous)]
                        if key not in rec['Images']:
                            rec['Images'].append(key)
                for cells_row in s:
                    for cell in cells_row:
                        if not is_picture_formula(cell.value):
                            continue
                        entry['image_formula_cells'] += 1
                        match = re.search(r'DISPIMG\("([^"]+)"', cell.value)
                        key = cell_images[match.group(1)]
                        rec = rows[cell.row]
                        if key not in rec['Images']:
                            rec['Images'].append(key)
                        assigned.add(key)
                for rec in rows.values():
                    rec['Text'] = '\n\n'.join(dict.fromkeys(rec['Text'].split('\n\n')))
            extra = list(dict.fromkeys(m['resource'] for m in entry['media'] if m['resource'] not in assigned))
            if extra:
                records.append(dict(Id=f.stem+'/extra', Title='补充图片', Group='历史包装', Source=f.name,
                                    Text='原文件中的图片，未自动关联到具体记录，请结合原表核对。', Images=extra))
            entry['unassociated_media'] = len(extra)
        imported_images = {key for rec in records[first_record:] for key in rec['Images']}
        assert all(m['resource'] in imported_images for m in entry['media']), f.name
        entry['records'] = len(records) - first_record
        entry['media_covered'] = len(entry['media'])
        knowledge_text = '\n'.join(rec['Text'] for rec in records[first_record:])
        source_texts = ([cell['value'] for sheet in full.get('sheets', []) for cell in sheet['cells']]
                        if f.suffix == '.xlsx' else [p for part in full['parts'] for p in part['paragraphs']])
        missing_texts = [value for value in source_texts if value.strip() and not is_picture_formula(value)
                         and value not in knowledge_text]
        assert not missing_texts, (f.name, missing_texts)
        entry['nonempty_text_values_missing_from_knowledge'] = len(missing_texts)
        manifest.append(entry)
        extraction.append(full)

assign_stable_ids(records, previous_records)
validate_stable_ids(records)
(OUT / 'knowledge.json').write_text(json.dumps(records, ensure_ascii=False), encoding='utf-8')
(OUT / 'full-reference-extract.json').write_text(json.dumps(extraction, ensure_ascii=False, indent=2), encoding='utf-8')
summary = dict(files=len(manifest), records=len(records), unique_image_resources=len(images),
               media_parts=sum(len(f['media']) for f in manifest),
               text_cells=sum(f['text_cells'] for f in manifest), sources=manifest)
(OUT / 'source-manifest.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
report = ['# 原始五份资料导入核验', '',
          '已保存五份原件的完整字节副本（original-sources.zip），逐文件 SHA-256 见 source-manifest.json。',
          '完整文本备份见 full-reference-extract.json：Word按XML段落顺序提取正文和表格文本；Excel保留全部非空单元格、公式、合并范围与批注。', '',
          f'共 {len(records)} 条知识记录，{summary["text_cells"]} 个原始非空Excel单元格，{summary["media_parts"]} 个原始媒体文件，{len(images)} 个去重预览图片资源。', '',
          '| 原件 | 知识记录 | 媒体覆盖 |', '|---|---:|---:|']
report += [f'| {m["file"]} | {m["records"]} | {m["media_covered"]}/{len(m["media"])} |' for m in manifest]
report += ['', '修复：补入材料表8个工作表的标题及使用说明；继承实际合并单元格的品牌/类别/材质；保留历史表二级表头；将2个DISPIMG图片关联回原单元格所在记录。',
           '', '知识正文核对：五份文件的全部非空可读文字值（包括表头）均可在knowledge.json中找到；2条DISPIMG公式作为图片关联处理，公式原串保存在完整提取备份。包装案例表有1张图片锚点未能归入具体数据行，已保存在“补充图片”记录，未丢弃。',
           '', '范围说明：V0.2.4文字方案请求包含全部知识正文；图片请求包含相关资料摘要和选中的案例图。Ohuhu风格图另行导入并自动发送。图片预览为缩小JPEG，原始分辨率/格式保留在原件中。图片中的文字未做OCR；没有自行补写未给出的参数、标准限值或供应商信息。历史图按原表锚点关联，跨行浮动图仍需以原件为准。']
(ROOT / 'desktop' / '资料导入核验.md').write_text('\n'.join(report)+'\n', encoding='utf-8')
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k != 'sources'}, ensure_ascii=False))
