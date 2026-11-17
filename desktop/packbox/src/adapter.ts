import { BOX_STYLES } from './geometry/boxBuilders';
import { computeFlatLayout } from './geometry/layout';
import { generateDielineSVG } from './geometry/svgExport';
import { emptyArtwork } from './artwork/types';
import { serializeProject } from './project/projectFile';
import type { BoxDimensions } from './geometry/types';

export const WARNING = 'PackBox模板参考：按零板厚面板生成，尺寸不是自动补偿后的内径或外径；需另行核对板厚、折线、胶位、配合和白样。自锁底模板未建模完整咬合槽；天地盖底与盖须分别导出。';
const names: Record<string, string> = {
  'tuck-end': '直插口折叠盒', rsc: '普通开槽运输箱', 'card-deck': '胶合卡牌盒',
  'card-deck-lock': '插锁盖卡牌盒', mailer: '翻盖邮寄盒', hsc: '开口托盘',
  'auto-lock-tray': '自锁底托盘（槽口待完善）', 'tuck-top-auto-bottom': '插盖底盒',
  'two-piece-base': '天地盖·底盒', 'two-piece-lid': '天地盖·上盖',
};
export function templates() {
  return BOX_STYLES.map(s => ({ id: s.id, name: names[s.id], originalName: s.label, defaults: s.defaultDimensions }));
}
function measure(value: unknown, name: string, allowZero = false): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value > 10000 || (allowZero ? value < 0 : value < 0.1))
    throw new Error(`${name}必须是${allowZero ? '0' : '0.1'}至10000之间的毫米数值。`);
  return value;
}
function xml(text: string) {
  return text.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c]!));
}
export function createTemplate(input: unknown) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('请求必须是JSON对象。');
  const r = input as Record<string, unknown>;
  const style = BOX_STYLES.find(s => s.id === r.style);
  if (!style) throw new Error('未识别的PackBox模板。');
  const dims: BoxDimensions = {
    ...style.defaultDimensions,
    length: measure(r.length, 'length'), width: measure(r.width, 'width'), height: measure(r.height, 'height'),
  };
  for (const key of ['endFlapLeft', 'endFlapRight', 'lidDepth'] as const)
    if (r[key] !== undefined) dims[key] = measure(r[key], key, true);
  if (r.flaps !== undefined) {
    if (!r.flaps || typeof r.flaps !== 'object' || Array.isArray(r.flaps)) throw new Error('flaps必须是对象。');
    const flaps = r.flaps as Record<string, unknown>; dims.flaps = {};
    for (const key of Object.keys(flaps)) {
      if (!['front', 'back', 'left', 'right'].includes(key)) throw new Error('未知的摇盖位置：' + key);
      dims.flaps[key as keyof NonNullable<BoxDimensions['flaps']>] = measure(flaps[key], 'flaps.' + key, true);
    }
  }
  if (r.title !== undefined && (typeof r.title !== 'string' || r.title.length > 200)) throw new Error('标题必须为200字以内的文字。');
  const root = style.build(dims);
  const layout = computeFlatLayout(root);
  if (!layout.panels.length || layout.panels.some(p => !(p.w > 0 && p.h > 0) || p.points.some(v => v.some(n => !Number.isFinite(n)))))
    throw new Error('这些参数未形成有效面板，请调整模板尺寸或摇盖。');
  const title = typeof r.title === 'string' && r.title.trim() ? r.title.trim() : names[style.id];
  const svg = generateDielineSVG(layout, { dimensions: true, title: xml(title + ' — 模板参考 / 待白样验证') });
  const project = serializeProject(style.id, dims, emptyArtwork());
  return { svg, project, width: layout.bounds.width, height: layout.bounds.height, panels: layout.panels.length, warning: WARNING };
}
