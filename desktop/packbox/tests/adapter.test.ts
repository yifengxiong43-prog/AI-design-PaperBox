import assert from 'node:assert/strict';
import { createTemplate, templates } from '../src/adapter';
import { parseProject } from '../src/project/projectFile';

assert.equal(templates().length, 10);
for (const t of templates()) {
  const dims = { length: 186, width: 65, height: 176.4 };
  const result = createTemplate({ style: t.id, ...dims, title: '纸盒 <script>alert(1)</script> & 尺寸' });
  const restored = parseProject(result.project);
  assert.equal(restored.boxStyleId, t.id);
  for (const key of ['length', 'width', 'height'] as const) assert.equal(restored.dims[key], dims[key]);
  assert(result.width > dims.length && result.height > dims.height);
  assert(result.panels > 4);
  assert(result.svg.includes('id="cut-lines"') && result.svg.includes('id="fold-lines"') && result.svg.includes('id="dimension-labels"'));
  assert(!result.svg.includes('<script>') && result.svg.includes('&lt;script&gt;'));
  assert(!result.svg.includes('NaN') && !result.svg.includes('Infinity'));
}
for (const value of [-1, 0, NaN, Infinity, '150', null])
  assert.throws(() => createTemplate({ style: 'tuck-end', length: value, width: 60, height: 150 }));
assert.throws(() => createTemplate({ style: 'unknown', length: 100, width: 60, height: 150 }));
assert.throws(() => createTemplate({ style: 'tuck-end', length: 100, width: 60, height: 150, flaps: { front: -1 } }));
const omitted = createTemplate({ style: 'rsc', length: 100, width: 60, height: 150, flaps: { front: 0, back: 0, left: 0, right: 0 }, endFlapLeft: 0, endFlapRight: 0 });
assert.equal(omitted.panels, 4);
assert.equal(parseProject(omitted.project).dims.flaps?.front, 0);
console.log('PackBox adapter: 10 templates, project round-trips, dimensions, input and SVG escaping checks passed.');
