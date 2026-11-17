import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { createTemplate, templates } from './adapter';

try {
  if (process.argv.length === 3 && process.argv[2] === '--templates') {
    process.stdout.write(JSON.stringify(templates()));
  } else {
    if (process.argv.length !== 4) throw new Error('用法：node packbox-cli.cjs request.json output-base-path');
    const request = JSON.parse(readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
    const result = createTemplate(request);
    const base = resolve(process.argv[3]), svgPath = base + '.svg', projectPath = base + '.pbox';
    if (existsSync(svgPath) || existsSync(projectPath)) throw new Error('输出文件已存在，请选择新的文件名。');
    writeFileSync(svgPath, result.svg, { encoding: 'utf8', flag: 'wx' });
    writeFileSync(projectPath, result.project, { encoding: 'utf8', flag: 'wx' });
    process.stdout.write(JSON.stringify({ ok: true, svgPath, projectPath, width: result.width, height: result.height, panels: result.panels, warning: result.warning }));
  }
} catch (error) {
  process.stderr.write(JSON.stringify({ ok: false, error: error instanceof Error ? error.message : String(error) }));
  process.exitCode = 1;
}
