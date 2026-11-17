const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const runtime = path.join(root, 'runtime', 'node.exe');
const cli = path.join(root, 'packbox-cli.cjs');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'packbox-中文-check-'));
function run(args) { return spawnSync(runtime, [cli, ...args], { cwd: work, encoding: 'utf8', windowsHide: true, timeout: 15000 }); }
const list = run(['--templates']);
assert.equal(list.status, 0, list.stderr);
const templates = JSON.parse(list.stdout);
assert.equal(templates.length, 10);
for (const t of templates) {
  const request = path.join(work, t.id + '.json'), base = path.join(work, t.id);
  fs.writeFileSync(request, '\ufeff' + JSON.stringify({ style: t.id, ...t.defaults, title: '中文 模板 & 尺寸' }));
  const result = run([request, base]);
  assert.equal(result.status, 0, result.stderr);
  const output = JSON.parse(result.stdout);
  assert(output.ok && output.panels > 4 && output.width > 0 && output.height > 0);
  assert.equal(output.svgPath, base + '.svg');
  assert.equal(output.projectPath, base + '.pbox');
  assert(fs.readFileSync(output.svgPath, 'utf8').includes('dimension-labels'));
  const saved = JSON.parse(fs.readFileSync(output.projectPath, 'utf8'));
  assert.equal(saved.app, 'packbox'); assert.equal(saved.version, 1); assert.equal(saved.boxStyleId, t.id);
  const before = fs.readFileSync(output.svgPath);
  const duplicate = run([request, base]);
  assert.equal(duplicate.status, 1); assert.equal(JSON.parse(duplicate.stderr).ok, false);
  assert.deepEqual(fs.readFileSync(output.svgPath), before);
}
const invalid = path.join(work, 'invalid.json');
fs.writeFileSync(invalid, JSON.stringify({ style: 'mailer', length: 100, width: -1, height: 50 }));
const rejected = run([invalid, path.join(work, 'invalid')]);
assert.equal(rejected.status, 1); assert.equal(JSON.parse(rejected.stderr).ok, false);
assert(!fs.existsSync(path.join(work, 'invalid.svg')));
console.log('PackBox portable CLI: 10 templates, BOM/Chinese paths, separate cwd, no-overwrite and invalid-input checks passed.');
console.log('Evidence: ' + work);
