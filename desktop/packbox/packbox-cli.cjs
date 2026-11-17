// desktop/packbox/src/cli.ts
var import_node_fs = require("node:fs");
var import_node_path = require("node:path");

// desktop/packbox/src/geometry/types.ts
var FLAP_WALLS = ["front", "back", "left", "right"];

// desktop/packbox/src/geometry/shapes.ts
function rect(w, h) {
  return [
    [0, 0],
    [w, 0],
    [w, h],
    [0, h]
  ];
}
function dustFlap(w, h) {
  const cut = Math.min(h, w / 2);
  return [
    [0, 0],
    [w, 0],
    [w, h - cut],
    [w - cut, h],
    [cut, h],
    [0, h - cut]
  ];
}
function tuckFlap(w, h) {
  const inset = Math.min(w * 0.12, h * 0.6);
  const slotW = w * 0.22;
  const slotDepth = Math.min(h * 0.18, 6);
  const cx = w / 2;
  return [
    [0, 0],
    [w, 0],
    [w - inset, h],
    [cx + slotW / 2, h],
    [cx + slotW / 2, h - slotDepth],
    [cx - slotW / 2, h - slotDepth],
    [cx - slotW / 2, h],
    [inset, h]
  ];
}
function lockTab(w, h) {
  const tip = w * 0.3;
  return [
    [0, 0],
    [w, 0],
    [w, h * 0.55],
    [w / 2 + tip / 2, h],
    [w / 2 - tip / 2, h],
    [0, h * 0.55]
  ];
}

// desktop/packbox/src/geometry/boxBuilders.ts
var uid = 0;
function nextId(prefix) {
  uid += 1;
  return `${prefix}-${uid}`;
}
function makePanel(opts) {
  return {
    id: nextId(opts.face),
    name: opts.name,
    w: opts.w,
    h: opts.h,
    outline: opts.outline,
    foldAngle: opts.foldAngle ?? -Math.PI / 2,
    hinged: opts.hinged ?? true,
    face: opts.face,
    isInside: opts.isInside,
    foldStart: opts.foldStart,
    foldEnd: opts.foldEnd,
    children: []
  };
}
var PHASE_TUBE = { foldStart: 0, foldEnd: 0.4 };
var PHASE_INNER = { foldStart: 0.4, foldEnd: 0.7 };
var PHASE_OUTER = { foldStart: 0.7, foldEnd: 1 };
function attach(parent, edge, offset, child) {
  child.parentEdge = edge;
  child.parentOffset = offset;
  child.parentId = parent.id;
  parent.children.push(child);
  return child;
}
var TUBE_FOLD = Math.PI / 2;
var TOP_FOLD = -Math.PI / 2;
var BOTTOM_FOLD = Math.PI / 2;
function resolveFlaps(dims, defaults) {
  const out = {};
  for (const wall of FLAP_WALLS) {
    const v = dims.flaps?.[wall];
    out[wall] = Math.max(0, v ?? defaults[wall]);
  }
  return out;
}
function buildTube(dims) {
  const { length: L, width: W, height: H } = dims;
  const glue = dims.endFlapRight ?? Math.max(20, W * 0.5);
  const leftEnd = dims.endFlapLeft ?? 0;
  const front = makePanel({ name: "Front", face: "front", w: L, h: H, foldAngle: 0, hinged: false });
  const side1 = attach(front, "right", 0, makePanel({ name: "Side (Left)", face: "left", w: W, h: H, foldAngle: TUBE_FOLD, ...PHASE_TUBE }));
  const back = attach(side1, "right", 0, makePanel({ name: "Back", face: "back", w: L, h: H, foldAngle: TUBE_FOLD, ...PHASE_TUBE }));
  const side2 = attach(back, "right", 0, makePanel({ name: "Side (Right)", face: "right", w: W, h: H, foldAngle: TUBE_FOLD, ...PHASE_TUBE }));
  if (glue > 0) {
    attach(side2, "right", 0, makePanel({ name: "Glue Flap (Right End)", face: "glue", w: glue, h: H, foldAngle: TUBE_FOLD, ...PHASE_TUBE }));
  }
  if (leftEnd > 0) {
    attach(front, "left", 0, makePanel({ name: "End Flap (Left End)", face: "glue", w: leftEnd, h: H, foldAngle: -TUBE_FOLD, ...PHASE_TUBE }));
  }
  return { front, side1, back, side2 };
}
function rscFlapDefaults(dims) {
  const { length: L, width: W } = dims;
  const minor = Math.max(8, L / 2 - 1);
  const major = Math.max(8, W - 6);
  return { front: major, back: major, left: minor, right: minor };
}
function attachRSCFlaps(edge, tube, depths) {
  const { front, side1, back, side2 } = tube;
  const foldAngle = edge === "top" ? TOP_FOLD : BOTTOM_FOLD;
  const cap = (s) => s[0].toUpperCase() + s.slice(1);
  const add = (parent, wall, label, phase) => {
    const h = depths[wall];
    if (h <= 0) return;
    attach(parent, edge, 0, makePanel({ name: `${cap(edge)} Flap (${label})`, face: `flap-${edge}-${wall}`, w: parent.w, h, foldAngle, ...phase }));
  };
  add(side1, "left", "Left", PHASE_INNER);
  add(side2, "right", "Right", PHASE_INNER);
  add(front, "front", "Front", PHASE_OUTER);
  add(back, "back", "Back", PHASE_OUTER);
}
function buildTuckEndCarton(dims) {
  const { width: W } = dims;
  const tube = buildTube(dims);
  const { front, side1, back, side2 } = tube;
  const tuck = W * 0.55;
  const depths = resolveFlaps(dims, { front: tuck, back: tuck, left: tuck * 0.85, right: tuck * 0.85 });
  const addTuck = (parent, wall, edge, label) => {
    const h = depths[wall];
    if (h <= 0) return;
    const isSide = wall === "left" || wall === "right";
    attach(
      parent,
      edge,
      0,
      makePanel({
        name: `${edge === "top" ? "Top" : "Bottom"} ${isSide ? "Dust" : "Tuck"} Flap (${label})`,
        face: `flap-${edge}-${wall}`,
        w: parent.w,
        h,
        outline: isSide ? dustFlap(parent.w, h) : tuckFlap(parent.w, h),
        foldAngle: edge === "top" ? TOP_FOLD : BOTTOM_FOLD,
        ...isSide ? PHASE_INNER : PHASE_OUTER
      })
    );
  };
  for (const edge of ["top", "bottom"]) {
    addTuck(front, "front", edge, "Front");
    addTuck(side1, "left", edge, "Left");
    addTuck(back, "back", edge, "Back");
    addTuck(side2, "right", edge, "Right");
  }
  return front;
}
function buildRSC(dims) {
  const tube = buildTube(dims);
  const depths = resolveFlaps(dims, rscFlapDefaults(dims));
  attachRSCFlaps("top", tube, depths);
  attachRSCFlaps("bottom", tube, depths);
  return tube.front;
}
function buildOpenTray(dims) {
  const tube = buildTube(dims);
  attachRSCFlaps("bottom", tube, resolveFlaps(dims, rscFlapDefaults(dims)));
  return tube.front;
}
function buildAutoLockTray(dims) {
  const { front, side1, back, side2 } = buildTube(dims);
  const depths = resolveFlaps(dims, rscFlapDefaults(dims));
  const addLock = (parent, wall, label, phase) => {
    const h = depths[wall];
    if (h <= 0) return;
    attach(parent, "bottom", 0, makePanel({ name: `Lock Flap (${label})`, face: `flap-bottom-${wall}`, w: parent.w, h, outline: lockTab(parent.w, h), foldAngle: BOTTOM_FOLD, ...phase }));
  };
  addLock(side1, "left", "Left", PHASE_INNER);
  addLock(side2, "right", "Right", PHASE_INNER);
  addLock(front, "front", "Front", PHASE_OUTER);
  addLock(back, "back", "Back", PHASE_OUTER);
  return front;
}
function buildTuckTopAutoBottom(dims) {
  const { front, side1, back, side2 } = buildTube(dims);
  const depths = resolveFlaps(dims, rscFlapDefaults(dims));
  attachRSCFlaps("bottom", { front, side1, back, side2 }, depths);
  const addTop = (parent, wall, label) => {
    const h = depths[wall];
    if (h <= 0) return;
    const isSide = wall === "left" || wall === "right";
    attach(
      parent,
      "top",
      0,
      makePanel({
        name: `Top ${isSide ? "Dust" : "Tuck"} Flap (${label})`,
        face: `flap-top-${wall}`,
        w: parent.w,
        h,
        outline: isSide ? dustFlap(parent.w, h) : tuckFlap(parent.w, h),
        foldAngle: TOP_FOLD,
        ...isSide ? PHASE_INNER : PHASE_OUTER
      })
    );
  };
  addTop(front, "front", "Front");
  addTop(side1, "left", "Left");
  addTop(back, "back", "Back");
  addTop(side2, "right", "Right");
  return front;
}
function buildMailer(dims) {
  const { width: W, height: H } = dims;
  const { front, side1, back, side2 } = buildTube(dims);
  const lidDepth = dims.lidDepth ?? H * 0.9;
  const lockDefault = Math.min(W * 0.6, 30);
  const depths = resolveFlaps(dims, { front: lockDefault, back: lockDefault, left: lockDefault, right: lockDefault });
  back.name = "Back / Lid";
  const lid = attach(back, "top", 0, makePanel({ name: "Lid Top", face: "top", w: back.w, h: H, foldAngle: TOP_FOLD * 2, foldStart: 0.55, foldEnd: 0.85 }));
  attach(lid, "top", 0, makePanel({ name: "Lid Tuck Flap", face: "flap-top-back", w: lid.w, h: lidDepth, outline: tuckFlap(lid.w, lidDepth), foldAngle: -TOP_FOLD, foldStart: 0.85, foldEnd: 1 }));
  const addSideDust = (parent, wall, label) => {
    const h = depths[wall];
    if (h <= 0) return;
    attach(parent, "top", 0, makePanel({ name: `Lid Side Dust (${label})`, face: `flap-top-${wall}`, w: W, h, outline: dustFlap(W, h), foldAngle: TOP_FOLD, ...PHASE_INNER }));
  };
  addSideDust(side1, "left", "Left");
  addSideDust(side2, "right", "Right");
  const addLock = (parent, wall, label, phase) => {
    const h = depths[wall];
    if (h <= 0) return;
    attach(parent, "bottom", 0, makePanel({ name: `Bottom Lock (${label})`, face: `flap-bottom-${wall}`, w: parent.w, h, outline: lockTab(parent.w, h), foldAngle: BOTTOM_FOLD, ...phase }));
  };
  addLock(front, "front", "Front", PHASE_OUTER);
  addLock(side1, "left", "Left", PHASE_INNER);
  addLock(back, "back", "Back", PHASE_OUTER);
  addLock(side2, "right", "Right", PHASE_INNER);
  return front;
}
function buildCardDeckLockLid(dims) {
  const { length: L, width: W, height: H } = dims;
  const glue = dims.endFlapRight ?? 18;
  const depths = resolveFlaps(dims, {
    left: L / 2,
    right: L / 2,
    front: W,
    back: Math.max(8, W - 6)
  });
  const lockDepth = depths.back;
  const SEQ_TUBE = { foldStart: 0, foldEnd: 0.3 };
  const SEQ_DUST = { foldStart: 0.3, foldEnd: 0.55 };
  const SEQ_INNER = { foldStart: 0.55, foldEnd: 0.78 };
  const SEQ_LID = { foldStart: 0.78, foldEnd: 1 };
  const side1 = makePanel({ name: "Side (Left)", face: "left", w: W, h: H, foldAngle: 0, hinged: false });
  const front = attach(side1, "right", 0, makePanel({ name: "Front", face: "front", w: L, h: H, foldAngle: TUBE_FOLD, ...SEQ_TUBE }));
  const side2 = attach(front, "right", 0, makePanel({ name: "Side (Right)", face: "right", w: W, h: H, foldAngle: TUBE_FOLD, ...SEQ_TUBE }));
  const back = attach(side2, "right", 0, makePanel({ name: "Back", face: "back", w: L, h: H, foldAngle: TUBE_FOLD, ...SEQ_TUBE }));
  const gluePanel = glue > 0 ? attach(back, "right", 0, makePanel({ name: "Glue Flap", face: "glue", w: glue, h: H, foldAngle: TUBE_FOLD, ...SEQ_TUBE })) : null;
  for (const edge of ["top", "bottom"]) {
    const foldAngle = edge === "top" ? TOP_FOLD : BOTTOM_FOLD;
    const cap = edge === "top" ? "Top" : "Bottom";
    const add = (parent, wall, label, h, phase) => {
      if (h <= 0) return;
      attach(parent, edge, 0, makePanel({ name: `${cap} ${label}`, face: `flap-${edge}-${wall}`, w: parent.w, h, foldAngle, ...phase }));
    };
    add(side1, "left", "Dust Flap (Left)", depths.left, SEQ_DUST);
    add(side2, "right", "Dust Flap (Right)", depths.right, SEQ_DUST);
    add(back, "back", "Inner Flap (Back)", depths.back, SEQ_INNER);
    add(front, "front", "Lid (Front)", depths.front, SEQ_LID);
    if (gluePanel && lockDepth > 0) {
      attach(gluePanel, edge, 0, makePanel({ name: `${cap} Lock Flap`, face: "glue", w: gluePanel.w, h: lockDepth, foldAngle, ...SEQ_INNER }));
    }
  }
  return side1;
}
var BOX_STYLES = [
  {
    id: "tuck-end",
    label: "Straight Tuck End Carton",
    description: "Classic retail folding carton with tuck flaps top & bottom.",
    defaultDimensions: { length: 100, width: 60, height: 150, endFlapRight: 15 },
    build: buildTuckEndCarton
  },
  {
    id: "rsc",
    label: "RSC Shipping Box",
    description: "Regular Slotted Container \u2014 the standard corrugated shipping box.",
    defaultDimensions: { length: 300, width: 200, height: 200, endFlapRight: 40 },
    build: buildRSC
  },
  {
    id: "card-deck",
    label: "Card Deck Box (Glued)",
    description: "Glued RSC-style tuck box sized for a deck of cards \u2014 minor flaps meet in the middle, major flaps glue down on top.",
    defaultDimensions: { length: 70, width: 24, height: 100, endFlapRight: 18 },
    build: buildRSC
  },
  {
    id: "card-deck-lock",
    label: "Card Deck Box (Lock Lid)",
    description: "Re-openable card deck box: side dust flaps meet in the middle, a short inner back flap, a lid exactly one box-depth deep that closes flush over the cards, and a lock flap on the glue panel that tucks under the lid.",
    defaultDimensions: { length: 70, width: 24, height: 100, endFlapRight: 18 },
    build: buildCardDeckLockLid
  },
  {
    id: "mailer",
    label: "Mailer Box",
    description: "Tuck-lid mailer box with auto-lock bottom tabs.",
    defaultDimensions: { length: 250, width: 180, height: 80, endFlapRight: 15 },
    build: buildMailer
  },
  {
    id: "hsc",
    label: "Open Tray (HSC)",
    description: "Half Slotted Container \u2014 RSC-style glued bottom, open top. Used on its own as a tray, or as the base under a separate lid.",
    defaultDimensions: { length: 300, width: 200, height: 100, endFlapRight: 40 },
    build: buildOpenTray
  },
  {
    id: "auto-lock-tray",
    label: "Auto-Lock Bottom Tray",
    description: "Open-top tray with snap-lock bottom tabs instead of glue. Shows the general tab shape \u2014 the interlocking slots between tabs aren't modeled.",
    defaultDimensions: { length: 220, width: 150, height: 90, endFlapRight: 30 },
    build: buildAutoLockTray
  },
  {
    id: "tuck-top-auto-bottom",
    label: "Tuck-Top Auto-Bottom Box",
    description: "Glued RSC-style bottom with a reusable tuck lid on top \u2014 common for cosmetics and cereal-style retail boxes.",
    defaultDimensions: { length: 180, width: 90, height: 250, endFlapRight: 20 },
    build: buildTuckTopAutoBottom
  },
  {
    id: "two-piece-base",
    label: "Two-Piece Box \u2014 Base",
    description: "Open-top base tray for a telescoping lid-and-base box. Pair with the matching Lid template below.",
    defaultDimensions: { length: 150, width: 100, height: 60, endFlapRight: 25 },
    build: buildOpenTray
  },
  {
    id: "two-piece-lid",
    label: "Two-Piece Box \u2014 Lid",
    description: "Shallow cap that slips over the Base template. Enter L/W a few mm larger than the base (board thickness + a little clearance) and a shallow height.",
    defaultDimensions: { length: 154, width: 104, height: 35, endFlapRight: 25 },
    build: buildOpenTray
  }
];

// desktop/packbox/src/geometry/layout.ts
function hingeEdgeIndex(node, pointCount) {
  if (!node.parentEdge) return -1;
  if (node.parentEdge === "right") return pointCount - 1;
  if (node.parentEdge === "left") return 1;
  return 0;
}
function edgeIndexForAttachment(edge, pointCount) {
  if (pointCount !== 4) return -1;
  switch (edge) {
    case "bottom":
      return 0;
    case "right":
      return 1;
    case "top":
      return 2;
    case "left":
      return 3;
    default:
      return -1;
  }
}
function excludedEdges(node, pointCount) {
  const excluded = /* @__PURE__ */ new Set();
  if (node.parentEdge) excluded.add(hingeEdgeIndex(node, pointCount));
  for (const child of node.children) {
    if (!child.parentEdge) continue;
    const idx = edgeIndexForAttachment(child.parentEdge, pointCount);
    if (idx >= 0) excluded.add(idx);
  }
  return excluded;
}
function cutPaths(points, excluded) {
  const n = points.length;
  if (excluded.size === 0) return [[...points, points[0]]];
  if (excluded.size >= n) return [];
  const start = (Math.min(...excluded) + 1) % n;
  const runs = [];
  let current = null;
  for (let k = 0; k < n; k++) {
    const i = (start + k) % n;
    if (excluded.has(i)) {
      current = null;
      continue;
    }
    if (!current) {
      current = [points[i]];
      runs.push(current);
    }
    current.push(points[(i + 1) % n]);
  }
  return runs;
}
function bboxOf(points) {
  const xs = points.map((p) => p[0]);
  const ys = points.map((p) => p[1]);
  return {
    min: [Math.min(...xs), Math.min(...ys)],
    max: [Math.max(...xs), Math.max(...ys)]
  };
}
function walk(node, originX, originY) {
  const localPoints = node.outline ?? rect(node.w, node.h);
  const worldPoints = localPoints.map(([x, y]) => [originX + x, originY + y]);
  const n = worldPoints.length;
  const panels = [
    {
      id: node.id,
      name: node.name,
      face: node.face,
      isInside: node.isInside,
      points: worldPoints,
      origin: [originX, originY],
      angle: 0,
      ...bboxOf(worldPoints),
      mirrored: false,
      w: node.w,
      h: node.h
    }
  ];
  const foldLines = [];
  if (node.parentEdge) {
    const hingeIdx = hingeEdgeIndex(node, n);
    if (node.hinged) foldLines.push({ a: worldPoints[hingeIdx], b: worldPoints[(hingeIdx + 1) % n] });
  }
  const cutOutline = cutPaths(worldPoints, excludedEdges(node, n));
  for (const child of node.children) {
    let childOriginX = originX;
    let childOriginY = originY;
    let flip = false;
    const offset = child.parentOffset ?? 0;
    if (child.parentEdge === "right") {
      childOriginX = originX + node.w;
      childOriginY = originY + offset;
    } else if (child.parentEdge === "left") {
      childOriginX = originX - child.w;
      childOriginY = originY + offset;
    } else if (child.parentEdge === "top") {
      childOriginX = originX + offset;
      childOriginY = originY + node.h;
    } else if (child.parentEdge === "bottom") {
      childOriginX = originX + offset;
      childOriginY = originY;
      flip = true;
    }
    const res = flip ? mirrorWalk(child, childOriginX, childOriginY) : walk(child, childOriginX, childOriginY);
    panels.push(...res.panels);
    foldLines.push(...res.foldLines);
    cutOutline.push(...res.cutOutline);
  }
  return { panels, foldLines, cutOutline };
}
function mirrorWalk(node, originX, originY) {
  const localPoints = node.outline ?? rect(node.w, node.h);
  const worldPoints = localPoints.map(([x, y]) => [originX + x, originY - y]);
  const n = worldPoints.length;
  const panels = [
    {
      id: node.id,
      name: node.name,
      face: node.face,
      isInside: node.isInside,
      points: worldPoints,
      origin: [originX, originY],
      angle: Math.PI,
      ...bboxOf(worldPoints),
      mirrored: true,
      w: node.w,
      h: node.h
    }
  ];
  const foldLines = [];
  if (node.parentEdge && node.hinged) {
    const hingeIdx = hingeEdgeIndex(node, n);
    foldLines.push({ a: worldPoints[hingeIdx], b: worldPoints[(hingeIdx + 1) % n] });
  }
  const cutOutline = cutPaths(worldPoints, excludedEdges(node, n));
  return { panels, foldLines, cutOutline };
}
function computeFlatLayout(root) {
  const { panels, foldLines, cutOutline } = walk(root, 0, 0);
  const allPoints = panels.flatMap((p) => p.points);
  const xs = allPoints.map((p) => p[0]);
  const ys = allPoints.map((p) => p[1]);
  const minX = Math.min(...xs);
  const minY = Math.min(...ys);
  const maxX = Math.max(...xs);
  const maxY = Math.max(...ys);
  const margin = 5;
  const shiftX = -minX + margin;
  const shiftY = -minY + margin;
  const shift = (pts) => pts.map(([x, y]) => [x + shiftX, y + shiftY]);
  return {
    panels: panels.map((p) => ({
      ...p,
      points: shift(p.points),
      origin: [p.origin[0] + shiftX, p.origin[1] + shiftY],
      min: [p.min[0] + shiftX, p.min[1] + shiftY],
      max: [p.max[0] + shiftX, p.max[1] + shiftY]
    })),
    cutOutline: cutOutline.map(shift),
    foldLines: foldLines.map((f) => ({ a: shift([f.a])[0], b: shift([f.b])[0] })),
    bounds: { width: maxX - minX + margin * 2, height: maxY - minY + margin * 2 }
  };
}

// desktop/packbox/src/geometry/dimensions.ts
var OUTER_OFFSET_MM = 14;
var MIN_DIM_MM = 16;
var ROW_EPS_MM = 0.5;
function groupIntoRows(panels) {
  const rows = [];
  for (const p of panels) {
    const y0 = p.min[1];
    const y1 = p.max[1];
    let row = rows.find((r) => Math.abs(r.y0 - y0) < ROW_EPS_MM && Math.abs(r.y1 - y1) < ROW_EPS_MM);
    if (!row) {
      row = { y0, y1, panels: [] };
      rows.push(row);
    }
    row.panels.push(p);
  }
  return rows;
}
function computeDimensionLines(layout) {
  const lines = [];
  const { width: W, height: H } = layout.bounds;
  lines.push({
    id: "overall-w",
    orientation: "h",
    x1: 0,
    y1: -OUTER_OFFSET_MM,
    x2: W,
    y2: -OUTER_OFFSET_MM,
    label: `${W.toFixed(0)} mm`,
    labelX: W / 2,
    labelY: -OUTER_OFFSET_MM
  });
  lines.push({
    id: "overall-h",
    orientation: "v",
    x1: -OUTER_OFFSET_MM,
    y1: 0,
    x2: -OUTER_OFFSET_MM,
    y2: H,
    label: `${H.toFixed(0)} mm`,
    labelX: -OUTER_OFFSET_MM,
    labelY: H / 2
  });
  const rows = groupIntoRows(layout.panels);
  for (const row of rows) {
    const rowH = row.y1 - row.y0;
    const sorted = [...row.panels].sort((a, b) => a.min[0] - b.min[0]);
    if (rowH >= MIN_DIM_MM) {
      const rep = sorted[0];
      const x = rep.min[0] + rep.w * 0.4;
      const inset = Math.min(rowH * 0.14, 6);
      lines.push({
        // Must be unique per row BAND, not just per y0 — two different flap tiers
        // (e.g. an RSC-style major flap and minor flap) can share the same y0 with
        // a different y1/height. A collision here isn't just cosmetic: this id
        // doubles as the React key in the 2D editor, so two rows colliding on the
        // same key made React mismatch/reuse the wrong element's position between
        // re-renders — which is what caused a height label to render in the wrong
        // spot (reported on the card-deck box's bottom flap row).
        id: `row-${row.y0.toFixed(1)}-${row.y1.toFixed(1)}-h`,
        orientation: "v",
        x1: x,
        y1: row.y0 + inset,
        x2: x,
        y2: row.y1 - inset,
        label: `${rowH.toFixed(0)}`,
        labelX: x,
        labelY: (row.y0 + row.y1) / 2
      });
    }
    const laneY = row.y0 + rowH * 0.5;
    for (const p of sorted) {
      if (p.w < MIN_DIM_MM) continue;
      const inset = Math.min(p.w * 0.12, 5);
      lines.push({
        id: `${p.id}-w`,
        orientation: "h",
        x1: p.min[0] + inset,
        y1: laneY,
        x2: p.min[0] + p.w - inset,
        y2: laneY,
        label: `${p.w.toFixed(0)}`,
        labelX: p.min[0] + p.w / 2,
        labelY: laneY
      });
    }
  }
  return lines;
}

// desktop/packbox/src/geometry/svgExport.ts
var MM_TO_PT = 72 / 25.4;
function flipY([x, y], height) {
  return [x, height - y];
}
function pathFromPoints(points, height) {
  if (points.length === 0) return "";
  const flipped = points.map((p) => flipY(p, height));
  const [first, ...rest] = flipped;
  return `M ${first[0].toFixed(2)},${first[1].toFixed(2)} ` + rest.map((p) => `L ${p[0].toFixed(2)},${p[1].toFixed(2)}`).join(" ");
}
function buildTrimGuide(sheetWidth, sheetHeight, viewHeight, markLen = 6) {
  const corners = [
    [0, 0],
    [sheetWidth, 0],
    [sheetWidth, sheetHeight],
    [0, sheetHeight]
  ];
  const rectPath = pathFromPoints([...corners, corners[0]], viewHeight);
  const rect2 = `<path d="${rectPath}" fill="none" stroke="#94a3b8" stroke-width="0.25" stroke-dasharray="3,2" />`;
  const ticks = corners.map(([x, y]) => {
    const dx = x === 0 ? -1 : 1;
    const dy = y === 0 ? -1 : 1;
    const [fx, fy] = flipY([x, y], viewHeight);
    return [
      `<line x1="${fx}" y1="${fy}" x2="${fx + dx * markLen}" y2="${fy}" stroke="#111827" stroke-width="0.3" />`,
      `<line x1="${fx}" y1="${fy}" x2="${fx}" y2="${fy + dy * markLen}" stroke="#111827" stroke-width="0.3" />`
    ].join("\n    ");
  }).join("\n    ");
  return `${rect2}
    ${ticks}`;
}
function buildSeamMarks(sheetWidth, sheetHeight, viewHeight, usableWidth, usableHeight, spacing = 50) {
  const marks = [];
  const crossAt = (x, y) => {
    const [fx, fy] = flipY([x, y], viewHeight);
    marks.push(
      `<line x1="${fx - 2.5}" y1="${fy}" x2="${fx + 2.5}" y2="${fy}" stroke="#2563eb" stroke-width="0.3" />`,
      `<line x1="${fx}" y1="${fy - 2.5}" x2="${fx}" y2="${fy + 2.5}" stroke="#2563eb" stroke-width="0.3" />`
    );
  };
  for (let x = usableWidth; x < sheetWidth; x += usableWidth) {
    for (let y = 0; y <= sheetHeight; y += spacing) crossAt(x, y);
  }
  for (let y = usableHeight; y < sheetHeight; y += usableHeight) {
    for (let x = 0; x <= sheetWidth; x += spacing) crossAt(x, y);
  }
  return marks.join("\n    ");
}
function buildDimensionMarkup(entries, viewHeight, mirrorTx, mirrorTy) {
  const HEAD_LEN = 2.2;
  const HEAD_WIDTH = 1.1;
  const COLOR = "#2563eb";
  const mirrorLabelX = (x) => mirrorTx !== void 0 ? mirrorTx - x : x;
  const mirrorLabelY = (y) => mirrorTy !== void 0 ? mirrorTy - y : y;
  const arrowHead = (tipX, tipY, outX, outY) => {
    const baseX = tipX - outX * HEAD_LEN;
    const baseY = tipY - outY * HEAD_LEN;
    const perpX = -outY;
    const perpY = outX;
    const p1x = baseX + perpX * HEAD_WIDTH;
    const p1y = baseY + perpY * HEAD_WIDTH;
    const p2x = baseX - perpX * HEAD_WIDTH;
    const p2y = baseY - perpY * HEAD_WIDTH;
    return `<polygon points="${tipX.toFixed(2)},${tipY.toFixed(2)} ${p1x.toFixed(2)},${p1y.toFixed(2)} ${p2x.toFixed(2)},${p2y.toFixed(2)}" fill="${COLOR}" />`;
  };
  const shapes = [];
  const labels = [];
  for (const e of entries) {
    const [ax, ay] = flipY([e.x1, e.y1], viewHeight);
    const [bx, by] = flipY([e.x2, e.y2], viewHeight);
    const dx = bx - ax;
    const dy = by - ay;
    const len = Math.hypot(dx, dy) || 1;
    const ux = dx / len;
    const uy = dy / len;
    const [flippedLx, flippedLy] = flipY([e.labelX, e.labelY], viewHeight);
    const lx = mirrorLabelX(flippedLx);
    const ly = mirrorLabelY(flippedLy);
    shapes.push(`<line x1="${ax.toFixed(2)}" y1="${ay.toFixed(2)}" x2="${bx.toFixed(2)}" y2="${by.toFixed(2)}" stroke="${COLOR}" stroke-width="0.25" />`);
    shapes.push(arrowHead(ax, ay, -ux, -uy));
    shapes.push(arrowHead(bx, by, ux, uy));
    if (e.orientation === "h") {
      const bgW = e.label.length * 1.9 + 1.5;
      labels.push(`<rect x="${(lx - bgW / 2).toFixed(2)}" y="${(ly - 2.6).toFixed(2)}" width="${bgW.toFixed(2)}" height="3.4" fill="#ffffff" opacity="0.85" />`);
      labels.push(`<text x="${lx.toFixed(2)}" y="${ly.toFixed(2)}" font-size="3" fill="${COLOR}" text-anchor="middle" dominant-baseline="middle">${e.label}</text>`);
    } else {
      labels.push(
        `<text x="${lx.toFixed(2)}" y="${ly.toFixed(2)}" font-size="3" fill="${COLOR}" text-anchor="middle" dominant-baseline="middle" transform="rotate(-90 ${lx.toFixed(2)} ${ly.toFixed(2)})">${e.label}</text>`
      );
    }
  }
  return { shapes: shapes.join("\n    "), labels: labels.join("\n    ") };
}
function generateDielineSVG(layout, opts = {}) {
  const { width: sheetWidth, height: sheetHeight } = layout.bounds;
  const autoMargin = opts.dimensions ? 18 : opts.trimGuide ? 6 : 0;
  const view = opts.viewBox ?? { x: -autoMargin, y: -autoMargin, width: sheetWidth + 2 * autoMargin, height: sheetHeight + 2 * autoMargin };
  const outputSize = opts.outputSizeMm ?? { width: view.width, height: view.height };
  const widthPt = (outputSize.width * MM_TO_PT).toFixed(2);
  const heightPt = (outputSize.height * MM_TO_PT).toFixed(2);
  const viewBoxY = sheetHeight - (view.y + view.height);
  const artwork = opts.artworkDataUrl ? `<image href="${opts.artworkDataUrl}" x="0" y="0" width="${sheetWidth}" height="${sheetHeight}" preserveAspectRatio="none" opacity="0.95" />` : "";
  const cutPaths2 = layout.cutOutline.map((poly) => `<path d="${pathFromPoints(poly, sheetHeight)}" fill="none" stroke="#e2231a" stroke-width="0.35" stroke-linejoin="round" />`).join("\n    ");
  const foldPaths = layout.foldLines.map((f) => {
    const [ax, ay] = flipY(f.a, sheetHeight);
    const [bx, by] = flipY(f.b, sheetHeight);
    return `<line x1="${ax.toFixed(2)}" y1="${ay.toFixed(2)}" x2="${bx.toFixed(2)}" y2="${by.toFixed(2)}" stroke="#1a9e4f" stroke-width="0.3" stroke-dasharray="2,1.5" />`;
  }).join("\n    ");
  const mirrorTx = 2 * view.x + view.width;
  const mirrorTy = 2 * viewBoxY + view.height;
  const contentOpen = opts.mirror === "x" ? `<g transform="translate(${mirrorTx.toFixed(2)},0) scale(-1,1)">` : opts.mirror === "y" ? `<g transform="translate(0,${mirrorTy.toFixed(2)}) scale(1,-1)">` : "<g>";
  const contentClose = "</g>";
  const mirrorLabelTx = opts.mirror === "x" ? mirrorTx : void 0;
  const mirrorLabelTy = opts.mirror === "y" ? mirrorTy : void 0;
  const dimsRawX = view.x + 2;
  const dimsRawY = flipY([0, view.y + 2], sheetHeight)[1];
  const dimsLabelX = mirrorLabelTx !== void 0 ? mirrorLabelTx - dimsRawX : dimsRawX;
  const dimsLabelY = mirrorLabelTy !== void 0 ? mirrorLabelTy - dimsRawY : dimsRawY;
  const dims = opts.showDimensions ? `<text x="${dimsLabelX.toFixed(2)}" y="${dimsLabelY.toFixed(2)}" font-size="3.5" fill="#666">${sheetWidth.toFixed(0)} x ${sheetHeight.toFixed(0)} mm</text>` : "";
  const trimGuide = opts.trimGuide ? buildTrimGuide(sheetWidth, sheetHeight, sheetHeight) : "";
  const seamMarks = opts.tileGrid ? buildSeamMarks(sheetWidth, sheetHeight, sheetHeight, opts.tileGrid.usableWidth, opts.tileGrid.usableHeight) : "";
  const dimensionMarkup = opts.dimensions ? buildDimensionMarkup(computeDimensionLines(layout), sheetHeight, mirrorLabelTx, mirrorLabelTy) : { shapes: "", labels: "" };
  return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
     width="${widthPt}pt" height="${heightPt}pt"
     viewBox="${view.x} ${viewBoxY} ${view.width} ${view.height}">
  <title>${opts.title ?? "Dieline"}</title>
  <rect x="${view.x}" y="${viewBoxY}" width="${view.width}" height="${view.height}" fill="#ffffff" />
  ${contentOpen}
  <g id="artwork">${artwork}</g>
  <g id="fold-lines">
    ${foldPaths}
  </g>
  <g id="cut-lines">
    ${cutPaths2}
  </g>
  <g id="trim-guide">
    ${trimGuide}
  </g>
  <g id="seam-marks">
    ${seamMarks}
  </g>
  <g id="dimension-shapes">
    ${dimensionMarkup.shapes}
  </g>
  ${contentClose}
  <g id="dimension-labels">
    ${dimensionMarkup.labels}
  </g>
  ${dims}
  ${opts.extra ?? ""}
</svg>`;
}

// desktop/packbox/src/artwork/types.ts
function emptySide(defaultColor) {
  return {
    sheetImage: void 0,
    sheetOffset: { x: 0, y: 0 },
    sheetWidth: 100,
    sheetHeight: 100,
    sheetRotation: 0,
    panelImages: {},
    layers: [],
    backgroundColor: defaultColor
  };
}
function emptyArtwork() {
  return {
    outside: emptySide("#ffffff"),
    inside: emptySide("#f2ede3")
  };
}

// desktop/packbox/src/project/projectFile.ts
function serializeProject(boxStyleId, dims, artwork) {
  const file = {
    app: "packbox",
    version: 1,
    savedAt: (/* @__PURE__ */ new Date()).toISOString(),
    boxStyleId,
    dims,
    artwork
  };
  return JSON.stringify(file);
}

// desktop/packbox/src/adapter.ts
var WARNING = "PackBox\u6A21\u677F\u53C2\u8003\uFF1A\u6309\u96F6\u677F\u539A\u9762\u677F\u751F\u6210\uFF0C\u5C3A\u5BF8\u4E0D\u662F\u81EA\u52A8\u8865\u507F\u540E\u7684\u5185\u5F84\u6216\u5916\u5F84\uFF1B\u9700\u53E6\u884C\u6838\u5BF9\u677F\u539A\u3001\u6298\u7EBF\u3001\u80F6\u4F4D\u3001\u914D\u5408\u548C\u767D\u6837\u3002\u81EA\u9501\u5E95\u6A21\u677F\u672A\u5EFA\u6A21\u5B8C\u6574\u54AC\u5408\u69FD\uFF1B\u5929\u5730\u76D6\u5E95\u4E0E\u76D6\u987B\u5206\u522B\u5BFC\u51FA\u3002";
var names = {
  "tuck-end": "\u76F4\u63D2\u53E3\u6298\u53E0\u76D2",
  rsc: "\u666E\u901A\u5F00\u69FD\u8FD0\u8F93\u7BB1",
  "card-deck": "\u80F6\u5408\u5361\u724C\u76D2",
  "card-deck-lock": "\u63D2\u9501\u76D6\u5361\u724C\u76D2",
  mailer: "\u7FFB\u76D6\u90AE\u5BC4\u76D2",
  hsc: "\u5F00\u53E3\u6258\u76D8",
  "auto-lock-tray": "\u81EA\u9501\u5E95\u6258\u76D8\uFF08\u69FD\u53E3\u5F85\u5B8C\u5584\uFF09",
  "tuck-top-auto-bottom": "\u63D2\u76D6\u5E95\u76D2",
  "two-piece-base": "\u5929\u5730\u76D6\xB7\u5E95\u76D2",
  "two-piece-lid": "\u5929\u5730\u76D6\xB7\u4E0A\u76D6"
};
function templates() {
  return BOX_STYLES.map((s) => ({ id: s.id, name: names[s.id], originalName: s.label, defaults: s.defaultDimensions }));
}
function measure(value, name, allowZero = false) {
  if (typeof value !== "number" || !Number.isFinite(value) || value > 1e4 || (allowZero ? value < 0 : value < 0.1))
    throw new Error(`${name}\u5FC5\u987B\u662F${allowZero ? "0" : "0.1"}\u81F310000\u4E4B\u95F4\u7684\u6BEB\u7C73\u6570\u503C\u3002`);
  return value;
}
function xml(text) {
  return text.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[c]);
}
function createTemplate(input) {
  if (!input || typeof input !== "object" || Array.isArray(input)) throw new Error("\u8BF7\u6C42\u5FC5\u987B\u662FJSON\u5BF9\u8C61\u3002");
  const r = input;
  const style = BOX_STYLES.find((s) => s.id === r.style);
  if (!style) throw new Error("\u672A\u8BC6\u522B\u7684PackBox\u6A21\u677F\u3002");
  const dims = {
    ...style.defaultDimensions,
    length: measure(r.length, "length"),
    width: measure(r.width, "width"),
    height: measure(r.height, "height")
  };
  for (const key of ["endFlapLeft", "endFlapRight", "lidDepth"])
    if (r[key] !== void 0) dims[key] = measure(r[key], key, true);
  if (r.flaps !== void 0) {
    if (!r.flaps || typeof r.flaps !== "object" || Array.isArray(r.flaps)) throw new Error("flaps\u5FC5\u987B\u662F\u5BF9\u8C61\u3002");
    const flaps = r.flaps;
    dims.flaps = {};
    for (const key of Object.keys(flaps)) {
      if (!["front", "back", "left", "right"].includes(key)) throw new Error("\u672A\u77E5\u7684\u6447\u76D6\u4F4D\u7F6E\uFF1A" + key);
      dims.flaps[key] = measure(flaps[key], "flaps." + key, true);
    }
  }
  if (r.title !== void 0 && (typeof r.title !== "string" || r.title.length > 200)) throw new Error("\u6807\u9898\u5FC5\u987B\u4E3A200\u5B57\u4EE5\u5185\u7684\u6587\u5B57\u3002");
  const root = style.build(dims);
  const layout = computeFlatLayout(root);
  if (!layout.panels.length || layout.panels.some((p) => !(p.w > 0 && p.h > 0) || p.points.some((v) => v.some((n) => !Number.isFinite(n)))))
    throw new Error("\u8FD9\u4E9B\u53C2\u6570\u672A\u5F62\u6210\u6709\u6548\u9762\u677F\uFF0C\u8BF7\u8C03\u6574\u6A21\u677F\u5C3A\u5BF8\u6216\u6447\u76D6\u3002");
  const title = typeof r.title === "string" && r.title.trim() ? r.title.trim() : names[style.id];
  const svg = generateDielineSVG(layout, { dimensions: true, title: xml(title + " \u2014 \u6A21\u677F\u53C2\u8003 / \u5F85\u767D\u6837\u9A8C\u8BC1") });
  const project = serializeProject(style.id, dims, emptyArtwork());
  return { svg, project, width: layout.bounds.width, height: layout.bounds.height, panels: layout.panels.length, warning: WARNING };
}

// desktop/packbox/src/cli.ts
try {
  if (process.argv.length === 3 && process.argv[2] === "--templates") {
    process.stdout.write(JSON.stringify(templates()));
  } else {
    if (process.argv.length !== 4) throw new Error("\u7528\u6CD5\uFF1Anode packbox-cli.cjs request.json output-base-path");
    const request = JSON.parse((0, import_node_fs.readFileSync)(process.argv[2], "utf8").replace(/^\uFEFF/, ""));
    const result = createTemplate(request);
    const base = (0, import_node_path.resolve)(process.argv[3]), svgPath = base + ".svg", projectPath = base + ".pbox";
    if ((0, import_node_fs.existsSync)(svgPath) || (0, import_node_fs.existsSync)(projectPath)) throw new Error("\u8F93\u51FA\u6587\u4EF6\u5DF2\u5B58\u5728\uFF0C\u8BF7\u9009\u62E9\u65B0\u7684\u6587\u4EF6\u540D\u3002");
    (0, import_node_fs.writeFileSync)(svgPath, result.svg, { encoding: "utf8", flag: "wx" });
    (0, import_node_fs.writeFileSync)(projectPath, result.project, { encoding: "utf8", flag: "wx" });
    process.stdout.write(JSON.stringify({ ok: true, svgPath, projectPath, width: result.width, height: result.height, panels: result.panels, warning: result.warning }));
  }
} catch (error) {
  process.stderr.write(JSON.stringify({ ok: false, error: error instanceof Error ? error.message : String(error) }));
  process.exitCode = 1;
}
