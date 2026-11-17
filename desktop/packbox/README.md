# PackBox export adapter

This folder vendors PackBox's pure geometry and project serialization code at the commit recorded in `UPSTREAM.json`. The WPF application calls a portable Node process, without installing Node or launching Electron. All upstream files under `src/geometry`, `src/project`, and `src/artwork` are unmodified; `src/adapter.ts` and `src/cli.ts` implement this application's bridge.

## Runtime contract

```
runtime/node.exe packbox-cli.cjs request.json output-base-path
runtime/node.exe packbox-cli.cjs --templates
```

Request UTF-8 JSON (a BOM is accepted):

```json
{"style":"tuck-end","length":186,"width":65,"height":176.4,"title":"包装模板"}
```

Required dimensions are millimetres, from 0.1 through 10000. Optional `flaps` keys are `front`, `back`, `left`, `right`. Optional `endFlapLeft`, `endFlapRight`, `lidDepth` and flap values are from 0 through 10000; zero explicitly omits a supported flap. Unspecified options inherit that style's upstream defaults. Dimensions are zero-thickness panel dimensions, not a board-thickness-compensated internal or external measurement.

Success writes `output-base-path.svg` (dimensioned reference) and `output-base-path.pbox` (upstream version 1 project), then emits one JSON object on stdout: `ok`, `svgPath`, `projectPath`, `width`, `height`, `panels`, `warning`. Width/height describe the flat sheet bounds; the dimension overlay adds SVG page margin. The output directory must already exist, and existing outputs are not overwritten. Failure emits `{ok:false,error:...}` on stderr and exit code 1. The WPF adapter stages these files in a temporary directory and exports a ZIP only after completion.

This is a manual template tool. It does not convert an arbitrary AI assembly into a corresponding production dieline, compensate board thickness, validate mechanism clearances, or certify manufacture. Auto-lock mating slots are incomplete upstream; two-piece base and lid are separate templates. Generated projects contain empty artwork, not texture inferred from AI images.

## Rebuild and verify

Use Node 24 and esbuild 0.25.12. Install build tooling in a temporary directory with lifecycle scripts disabled; it is not part of the shipped runtime:

```
npm.cmd install --ignore-scripts --no-audit --no-fund --prefix tmp/packbox-build-tools esbuild@0.25.12
tmp/packbox-build-tools/node_modules/.bin/esbuild.cmd desktop/packbox/src/cli.ts --bundle --platform=node --format=cjs --target=node24 --outfile=desktop/packbox/packbox-cli.cjs
tmp/packbox-build-tools/node_modules/.bin/esbuild.cmd desktop/packbox/tests/adapter.test.ts --bundle --platform=node --format=cjs --target=node24 --outfile=tmp/packbox-adapter-tests.cjs
desktop/packbox/runtime/node.exe tmp/packbox-adapter-tests.cjs
tmp/packbox-build-tools/node_modules/.bin/esbuild.cmd desktop/packbox/tests/upstream-smoke-test.ts --bundle --platform=node --format=cjs --target=node24 --outfile=tmp/packbox-upstream-tests.cjs
desktop/packbox/runtime/node.exe tmp/packbox-upstream-tests.cjs
desktop/packbox/runtime/node.exe desktop/packbox/tests/cli.test.cjs
```

The upstream smoke test is adapted only by omitting its DXF dependency and assertions, since this integration exports SVG and .pbox. Its geometry, bounding-box, cut/fold accounting and SVG assertions are retained.

Distribute the bundle, `templates.json`, `UPSTREAM.json`, full `PACKBOX-LICENSE.txt`, portable `runtime/node.exe`, and its complete `runtime/LICENSE`. The Node executable is a sidecar, not embedded in the WPF executable. Its official SHA-256 checksum and upstream source hashes are recorded in `UPSTREAM.json`.
