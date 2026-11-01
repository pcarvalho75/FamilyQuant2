throw new Error(
  "DEPRECATED: this script builds the rejected isotope-heavy 1.4 talk and must not be used for " +
  "the source-aware 1.5 presentation. Generate the reproducible figures from FamilyQuant via " +
  "Analytics > Save presentation evidence pack, then use the current simplified PEAKS/DeepNovo deck."
);

// Kept below the guard only as a historical record. Dynamic imports ensure the deprecation message is
// emitted even on machines that do not carry the old Codex artifact runtime.
const fs = (await import("node:fs/promises")).default;
const path = (await import("node:path")).default;
const { Presentation, PresentationFile } = await import("file:///C:/Users/paulo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs");

const ROOT = String.raw`C:\Users\paulo\OneDrive\Documents\ChatGPT\Dias\FamilyQuant_scientific_revision`;
const TALK = String.raw`C:\Users\paulo\Desktop\FamilyQuant\BrProt_Talk`;
const OUT = path.join(ROOT, "artifacts", "presentation");
const PREVIEWS = path.join(OUT, "artifact_previews");

const src = {
  numbers: path.join(TALK, "numbers.md"),
  method: path.join(ROOT, "SCIENTIFIC_METHOD.md"),
  audit: String.raw`C:\Users\paulo\OneDrive\Documents\ChatGPT\Dias\FamilyQuant_Adversarial_Review_Report.md`,
  fresh: path.join(ROOT, "artifacts", "verification", "talk_fresh_3raw.stdout.log"),
  finalTalk: path.join(ROOT, "artifacts", "verification", "talk_final_cached.stdout.log"),
  regression: path.join(ROOT, "artifacts", "verification", "regression_postfix.stdout.log"),
  estimator: path.join(ROOT, "FamilyQuant", "DeNovo", "ReferenceEnvelopeEstimator.cs"),
  detector: path.join(ROOT, "FamilyQuant", "DeNovo", "Ms1EnvelopeDetector.cs"),
  curation: path.join(ROOT, "FamilyQuant", "USearchTools.cs"),
  security: path.join(ROOT, "SECURITY.md"),
  identityFig: path.join(TALK, "figures", "identity_sweep.png"),
  familyFig: path.join(TALK, "figures", "family_contribution.png"),
};

const C = {
  bg: "#F7F4ED",
  paper: "#FFFEFB",
  ink: "#17252C",
  muted: "#657179",
  faint: "#D9DDD9",
  dark: "#13252E",
  teal: "#177F78",
  tealLight: "#D8ECE8",
  orange: "#D7653B",
  orangeLight: "#F5DED3",
  gold: "#D7A337",
  goldLight: "#F5E9C7",
  rose: "#A44C67",
  roseLight: "#F0DCE3",
  slate: "#53616A",
  slateLight: "#E8ECEC",
  white: "#FFFFFF",
};

const W = 1280;
const H = 720;
const presentation = Presentation.create({ slideSize: { width: W, height: H } });

async function writeBlob(filePath, blob) {
  await fs.writeFile(filePath, new Uint8Array(await blob.arrayBuffer()));
}

function shape(slide, geometry, position, fill = "none", lineFill = "none", lineWidth = 0, radius = undefined) {
  return slide.shapes.add({
    geometry,
    position,
    fill,
    line: { style: "solid", fill: lineFill, width: lineWidth },
    ...(radius ? { borderRadius: radius } : {}),
  });
}

function textBox(slide, text, position, fontSize = 24, color = C.ink, bold = false, alignment = "left") {
  const t = shape(slide, "textbox", position, "none", "none", 0);
  t.text = text;
  t.text.style = { fontSize, color, bold, alignment };
  return t;
}

function pill(slide, text, x, y, w, fill, color) {
  const p = shape(slide, "roundRect", { left: x, top: y, width: w, height: 34 }, fill, "none", 0, "rounded-full");
  p.text = text;
  p.text.style = { fontSize: 13, color, bold: true, alignment: "center" };
  return p;
}

function addHeader(slide, kicker, title, subtitle, n, dark = false) {
  const ink = dark ? C.white : C.ink;
  const muted = dark ? "#BDD0D5" : C.muted;
  const twoLine = title.length > 60;
  textBox(slide, kicker.toUpperCase(), { left: 72, top: 48, width: 520, height: 26 }, 13, dark ? "#F0B08C" : C.orange, true);
  textBox(slide, title, { left: 72, top: 82, width: 1040, height: twoLine ? 104 : 74 }, 40, ink, true);
  if (subtitle) textBox(slide, subtitle, { left: 74, top: twoLine ? 185 : 153, width: 1050, height: 42 }, 19, muted, false);
  textBox(slide, String(n).padStart(2, "0"), { left: 1175, top: 50, width: 40, height: 24 }, 13, muted, true, "right");
}

function addFooter(slide, label, dark = false) {
  shape(slide, "line", { left: 72, top: 682, width: 1136, height: 0 }, "none", dark ? "#3A525C" : C.faint, 1);
  textBox(slide, label, { left: 72, top: 688, width: 1050, height: 18 }, 10, dark ? "#9FB4BC" : "#7B858A", false);
}

function addNotes(slide, speaker, sources) {
  slide.speakerNotes.textFrame.setText(
    `${speaker}\n\n[Sources]\n${sources.join("\n")}\n[/Sources]`,
  );
  slide.speakerNotes.setVisible(true);
}

function metricCard(slide, x, y, w, h, value, title, body, fill, valueColor, tag) {
  const card = shape(slide, "roundRect", { left: x, top: y, width: w, height: h }, fill, "#FFFFFF", 1, "rounded-2xl");
  card.shadow = "shadow-sm";
  if (h < 200) {
    textBox(slide, value, { left: x + 20, top: y + 14, width: w - 40, height: 42 }, 30, valueColor, true);
    textBox(slide, title, { left: x + 20, top: y + 60, width: w - 40, height: 28 }, 16, C.ink, true);
    textBox(slide, body, { left: x + 20, top: y + 96, width: w - 40, height: Math.max(20, h - 104) }, 13, C.muted, false);
  } else {
    if (tag) pill(slide, tag, x + 22, y + 20, 132, C.white, valueColor);
    textBox(slide, value, { left: x + 22, top: y + 67, width: w - 44, height: 72 }, 48, valueColor, true);
    textBox(slide, title, { left: x + 22, top: y + 139, width: w - 44, height: 52 }, 20, C.ink, true);
    textBox(slide, body, { left: x + 22, top: y + 198, width: w - 44, height: h - 218 }, 15, C.muted, false);
  }
}

function bullet(slide, x, y, w, title, body, color = C.teal, titleColor = C.ink, bodyColor = C.muted) {
  shape(slide, "ellipse", { left: x, top: y + 7, width: 12, height: 12 }, color, "none", 0);
  textBox(slide, title, { left: x + 24, top: y, width: w - 24, height: 26 }, 18, titleColor, true);
  textBox(slide, body, { left: x + 24, top: y + 28, width: w - 24, height: 54 }, 15, bodyColor, false);
}

function bar(slide, x, y, totalW, value, max, fill, label, rightLabel) {
  textBox(slide, label, { left: x, top: y - 2, width: 240, height: 28 }, 16, C.ink, true);
  shape(slide, "roundRect", { left: x + 250, top: y, width: totalW, height: 24 }, C.slateLight, "none", 0, "rounded-full");
  shape(slide, "roundRect", { left: x + 250, top: y, width: Math.max(4, totalW * value / max), height: 24 }, fill, "none", 0, "rounded-full");
  textBox(slide, rightLabel, { left: x + 250 + totalW + 16, top: y - 3, width: 100, height: 30 }, 17, fill, true);
}

async function addImage(slide, imagePath, position, fit = "contain") {
  const bytes = await fs.readFile(imagePath);
  const blob = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
  return slide.images.add({ blob, contentType: "image/png", alt: path.basename(imagePath), fit, position });
}

// 1 — title
{
  const s = presentation.slides.add();
  s.background.fill = C.dark;
  shape(s, "ellipse", { left: 870, top: 96, width: 92, height: 92 }, "#F0B08C", "none", 0);
  shape(s, "ellipse", { left: 967, top: 128, width: 66, height: 66 }, "#E08458", "none", 0);
  shape(s, "ellipse", { left: 1040, top: 149, width: 42, height: 42 }, "#B95C3C", "none", 0);
  shape(s, "ellipse", { left: 1090, top: 165, width: 26, height: 26 }, "#7F4239", "none", 0);
  pill(s, "FAMILYQUANT · BRPROT", 72, 66, 220, "#23404A", "#F0B08C");
  textBox(s, "How much venom signal\ndid identification recover?", { left: 72, top: 160, width: 830, height: 180 }, 52, C.white, true);
  textBox(s, "A calibrated, auditable answer from three LC–MS/MS technical replicates", { left: 76, top: 362, width: 760, height: 70 }, 24, "#C4D4D9", false);
  shape(s, "roundRect", { left: 72, top: 494, width: 604, height: 92 }, "#1D343E", "#35505B", 1, "rounded-xl");
  textBox(s, "42.64%", { left: 96, top: 508, width: 190, height: 54 }, 38, "#F0B08C", true);
  textBox(s, "primary identified-reference estimate", { left: 294, top: 518, width: 350, height: 38 }, 18, C.white, true);
  textBox(s, "Not a detector residual. Not the unadjusted six-window value.", { left: 96, top: 559, width: 530, height: 24 }, 13, "#AFC2C8", false);
  addFooter(s, "Fresh three-RAW regeneration · 27 Aug 2026", true);
  addNotes(s, "Open with the question and the answer. Emphasize that 42.64% is an explicitly model-based primary estimate, not a truth label or a confidence interval.", [src.numbers, src.method, src.fresh]);
}

// 2 — answer first
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Answer first", "One dataset. Three deliberately different readings.", "Only the middle card is the primary estimate.", 2);
  metricCard(s, 72, 230, 350, 350, "17.33%", "M+0 sensitivity", "Direct M+0 windows only. Stable to the isotope ceiling, but not a formal lower bound if an anchor is wrong or contaminated.", C.slateLight, C.slate, "SENSITIVITY");
  metricCard(s, 465, 210, 350, 390, "42.64%", "Primary estimate", "Identified observed envelope + de novo M+0 scaled by the identified envelope/M+0 relation in fixed mass strata.", C.tealLight, C.teal, "PRIMARY");
  metricCard(s, 858, 230, 350, 350, "51.69%", "Unadjusted diagnostic", "The directly credited M0…M+5 windows. Reproducible, but the de novo isotope ratio is implausibly high.", C.orangeLight, C.orange, "DIAGNOSTIC");
  addFooter(s, "Pooled across three technical replicates; shares of raw-file TIC");
  addNotes(s, "Do not describe these as a confidence interval. The M+0 and unadjusted values are sensitivity readings around a model-based primary estimate, and their ordering is empirical.", [src.numbers, src.method]);
}

// 3 — estimand
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "Define the question", "“Recovered signal” must specify grain, numerator, and denominator.", "The corrected analysis uses one peptide once, on one integration basis, over raw TIC.", 3);
  const cols = [72, 464, 856];
  const titles = ["GRAIN", "NUMERATOR", "DENOMINATOR"];
  const values = ["peptide × run × charge", "representative XIC area", "raw-file TIC"];
  const bodies = [
    "All MS2 anchors for the same sequence and charge collapse to the largest-M+0 representative.",
    "Identified uses its observed envelope; de novo uses reference-calibrated M+0 for the primary result.",
    "TIC and peptide XICs share the same intensity-versus-retention-time trapezoid integration.",
  ];
  for (let i = 0; i < 3; i++) {
    shape(s, "roundRect", { left: cols[i], top: 240, width: 320, height: 270 }, i === 1 ? C.tealLight : C.slateLight, "#D5D9D7", 1, "rounded-2xl");
    pill(s, titles[i], cols[i] + 22, 262, 118, C.white, i === 1 ? C.teal : C.slate);
    textBox(s, values[i], { left: cols[i] + 22, top: 326, width: 276, height: 62 }, 25, C.ink, true);
    textBox(s, bodies[i], { left: cols[i] + 22, top: 405, width: 276, height: 88 }, 16, C.muted, false);
  }
  shape(s, "rightArrow", { left: 397, top: 342, width: 54, height: 42 }, C.orange, "none", 0);
  shape(s, "rightArrow", { left: 789, top: 342, width: 54, height: 42 }, C.orange, "none", 0);
  textBox(s, "160,337 anchors → 38,356 peptide representatives", { left: 340, top: 548, width: 600, height: 36 }, 20, C.orange, true, "center");
  addFooter(s, "Acceptance criterion: no anchor-level sum may be presented as a peptide-level recovery fraction");
  addNotes(s, "The earlier headline was vulnerable because anchor rows were easy to sum repeatedly. The corrected estimand fixes the unit of analysis before any percentages are calculated.", [src.numbers, src.method]);
}

// 4 — inputs
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Data and provenance", "Three RAW files feed one measurement engine.", "Search results and de novo candidates provide anchors; RAW spectra determine every area and TIC.", 4);
  const xs = [72, 356, 640];
  for (let i = 0; i < 3; i++) {
    shape(s, "roundRect", { left: xs[i], top: 246, width: 240, height: 190 }, C.paper, C.faint, 1, "rounded-2xl");
    textBox(s, `RUN 0${i + 1}`, { left: xs[i] + 22, top: 268, width: 196, height: 26 }, 14, C.orange, true);
    textBox(s, ["41,035", "40,489", "40,420"][i], { left: xs[i] + 22, top: 310, width: 196, height: 48 }, 34, C.ink, true);
    textBox(s, "MS1 scans", { left: xs[i] + 22, top: 360, width: 196, height: 28 }, 17, C.muted, false);
    textBox(s, "SHA-256 bound cache", { left: xs[i] + 22, top: 399, width: 196, height: 25 }, 13, C.teal, true);
  }
  shape(s, "roundRect", { left: 948, top: 225, width: 260, height: 250 }, C.dark, C.dark, 0, "rounded-2xl");
  textBox(s, "ONE RULER", { left: 974, top: 250, width: 208, height: 26 }, 14, "#F0B08C", true);
  textBox(s, "10 ppm", { left: 974, top: 294, width: 208, height: 48 }, 34, C.white, true);
  textBox(s, "M0…M+5 ceiling\n≥3 points / isotope\nsame trapezoid basis", { left: 974, top: 356, width: 208, height: 90 }, 17, "#C4D4D9", false);
  textBox(s, "2.7058 × 10¹² pooled TIC", { left: 72, top: 506, width: 620, height: 54 }, 28, C.ink, true);
  textBox(s, "All reported shares divide by this measured denominator.", { left: 74, top: 562, width: 620, height: 32 }, 16, C.muted, false);
  addFooter(s, "Fresh run: 8.4 min; no legacy RAW cache reused");
  addNotes(s, "Stress that nothing in the final percentage is copied from stored PatternLab quantitation. Identifications and PEAKS rows define where to look; the FamilyQuant RAW engine remeasures the signal.", [src.numbers, src.fresh]);
}

// 5 — workflow
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "One engine, one ruler", "The corrected path makes double credit visible—and suppresses exact cross-side overlap.", "Each stage leaves a provenance record.", 5);
  const stages = [
    ["1", "Anchor", "PSM or filtered de novo row"],
    ["2", "Extract", "M0…M+5 from the same RAW"],
    ["3", "Collapse", "one representative per peptide/run/charge"],
    ["4", "Calibrate", "de novo M0 × identified mass-bin ratio"],
    ["5", "Divide", "identified + de novo estimate over TIC"],
  ];
  for (let i = 0; i < stages.length; i++) {
    const x = 72 + i * 226;
    shape(s, "roundRect", { left: x, top: 250, width: 190, height: 240 }, i === 3 ? C.tealLight : C.slateLight, "#D1D7D5", 1, "rounded-2xl");
    shape(s, "ellipse", { left: x + 18, top: 270, width: 40, height: 40 }, i === 3 ? C.teal : C.orange, "none", 0);
    textBox(s, stages[i][0], { left: x + 18, top: 277, width: 40, height: 27 }, 18, C.white, true, "center");
    textBox(s, stages[i][1], { left: x + 18, top: 333, width: 154, height: 34 }, 22, C.ink, true);
    textBox(s, stages[i][2], { left: x + 18, top: 379, width: 154, height: 82 }, 15, C.muted, false);
    if (i < stages.length - 1) shape(s, "rightArrow", { left: x + 194, top: 346, width: 28, height: 34 }, C.gold, "none", 0);
  }
  pill(s, "0 exact / I-L-folded overlaps suppressed in this dataset", 328, 535, 624, C.orangeLight, C.orange);
  addFooter(s, "Cross-side suppression is conservative and keyed by run + sequence (I/L folded) + charge");
  addNotes(s, "Walk from anchors to the final denominator. The dataset happened to have zero exact/I-L-folded cross-side duplicates after filtering, but the guard now exists and is tested.", [src.method, src.numbers, path.join(ROOT, "FamilyQuant", "DeNovo", "NativeQuantFractions.cs")]);
}

// 6 — why old number is not primary
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "The dominant correction", "The de novo six-window ratio is not credible as a primary estimator.", "Identified and de novo anchors were receiving the same six slots—but not the same isotope behavior.", 6);
  bar(s, 96, 260, 450, 2.372, 4.6, C.teal, "Identified envelope / M+0", "2.372×");
  bar(s, 96, 338, 450, 2.652, 4.6, C.gold, "Mass-standardised reference", "2.652×");
  bar(s, 96, 416, 450, 4.308, 4.6, C.orange, "De novo unadjusted ratio", "4.308×");
  shape(s, "roundRect", { left: 900, top: 246, width: 282, height: 256 }, C.orangeLight, "#EDC8B7", 1, "rounded-2xl");
  textBox(s, "+62%", { left: 930, top: 274, width: 220, height: 62 }, 44, C.orange, true);
  textBox(s, "de novo ratio above the mass-standardised reference", { left: 930, top: 347, width: 220, height: 72 }, 19, C.ink, true);
  textBox(s, "That inflation moves the pooled total by 9.05 TIC points.", { left: 930, top: 432, width: 220, height: 52 }, 15, C.muted, false);
  textBox(s, "51.69% is preserved for auditability—never promoted as the answer.", { left: 155, top: 535, width: 820, height: 40 }, 23, C.rose, true, "center");
  addFooter(s, "Reference comparison from identified isotope behavior reweighted to de novo mass composition");
  addNotes(s, "This is the scientific reason the headline changed. The old value is computationally reproducible, but reproducibility does not remove a shared systematic inflation.", [src.numbers, src.audit]);
}

// 7 — estimator
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "Primary estimator", "Let identified peptides teach the envelope/M+0 relationship.", "The relation is pooled across runs and learned inside fixed neutral-mass strata.", 7);
  const bins = ["<800", "800–1200", "1200–1600", "1600–2000", "2000–2500", "2500–3000", "≥3000"];
  const x0 = 84;
  for (let i = 0; i < bins.length; i++) {
    const x = x0 + i * 158;
    shape(s, "roundRect", { left: x, top: 250, width: 132, height: 108 }, i % 2 === 0 ? C.tealLight : C.slateLight, "#D4DAD8", 1, "rounded-xl");
    textBox(s, bins[i], { left: x + 8, top: 270, width: 116, height: 28 }, 15, C.ink, true, "center");
    textBox(s, "Σ envelope\n────────\nΣ M+0", { left: x + 16, top: 305, width: 100, height: 48 }, 13, C.teal, true, "center");
  }
  shape(s, "downArrow", { left: 594, top: 380, width: 90, height: 70 }, C.gold, "none", 0);
  shape(s, "roundRect", { left: 250, top: 475, width: 780, height: 92 }, C.dark, C.dark, 0, "rounded-2xl");
  textBox(s, "de novo primary envelope  =  de novo M+0  ×  matched identified ratio", { left: 282, top: 501, width: 716, height: 42 }, 24, C.white, true, "center");
  textBox(s, "Pooled identified fallback only if a mass stratum has no identified signal. No arbitrary upper clamp.", { left: 270, top: 586, width: 740, height: 32 }, 15, C.muted, false, "center");
  addFooter(s, "Pre-specified bins: 800, 1200, 1600, 2000, 2500, 3000 Da boundaries");
  addNotes(s, "This estimator does not claim that mass captures every isotope-shape difference. It is a transparent calibration assumption, and the two sensitivity readings remain visible beside it.", [src.estimator, src.method, src.numbers]);
}

// 8 — per run
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Technical replicates", "The primary estimate is stable across runs.", "Agreement shows reproducibility of this algorithm—not freedom from shared bias.", 8);
  const runs = [
    ["RUN 01", 17.12, 41.57, 50.67],
    ["RUN 02", 17.38, 43.06, 52.54],
    ["RUN 03", 17.49, 43.24, 51.80],
  ];
  for (let i = 0; i < runs.length; i++) {
    const y = 245 + i * 122;
    textBox(s, runs[i][0], { left: 76, top: y + 12, width: 94, height: 28 }, 15, C.ink, true);
    const labels = [[runs[i][1], C.slate, "M+0"], [runs[i][2], C.teal, "PRIMARY"], [runs[i][3], C.orange, "RAW 6-WINDOW"]];
    for (let j = 0; j < labels.length; j++) {
      const x = 190 + j * 330;
      shape(s, "roundRect", { left: x, top: y, width: 284, height: 72 }, j === 1 ? C.tealLight : (j === 2 ? C.orangeLight : C.slateLight), "none", 0, "rounded-xl");
      textBox(s, `${labels[j][0].toFixed(2)}%`, { left: x + 18, top: y + 10, width: 105, height: 42 }, 27, labels[j][1], true);
      textBox(s, labels[j][2], { left: x + 132, top: y + 22, width: 136, height: 24 }, 12, C.muted, true, "right");
    }
  }
  textBox(s, "Primary range: 1.67 percentage points", { left: 378, top: 594, width: 524, height: 38 }, 22, C.teal, true, "center");
  addFooter(s, "Runs are technical replicates from the same acquisition design");
  addNotes(s, "Use the run-to-run agreement as a reproducibility statement only. It cannot detect a model assumption shared by all three runs.", [src.numbers, src.fresh]);
}

// 9 — pooled composition
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "Pooled primary result", "42.64% = identified observed envelope + calibrated de novo estimate.", "The remainder is deliberately left biologically unnamed.", 9);
  const x = 110, y = 278, bw = 1060, bh = 98;
  shape(s, "roundRect", { left: x, top: y, width: bw, height: bh }, C.slateLight, "none", 0, "rounded-2xl");
  const idW = bw * 0.2815;
  const dnW = bw * 0.1448;
  shape(s, "roundRect", { left: x, top: y, width: idW, height: bh }, C.teal, "none", 0, "rounded-2xl");
  shape(s, "rect", { left: x + idW - 12, top: y, width: 12, height: bh }, C.teal, "none", 0);
  shape(s, "rect", { left: x + idW, top: y, width: dnW, height: bh }, C.orange, "none", 0);
  textBox(s, "28.15%\nidentified", { left: x + 26, top: y + 19, width: idW - 40, height: 60 }, 22, C.white, true);
  textBox(s, "14.48%\nde novo", { left: x + idW + 22, top: y + 19, width: dnW - 28, height: 60 }, 21, C.white, true);
  textBox(s, "57.36% not assigned by the primary estimator", { left: x + idW + dnW + 20, top: y + 31, width: bw - idW - dnW - 36, height: 38 }, 18, C.slate, true, "center");
  metricCard(s, 220, 440, 380, 132, "1.1537 × 10¹²", "primary integrated area", "intensity × minutes", C.tealLight, C.teal, null);
  metricCard(s, 680, 440, 380, 132, "38,356", "peptide representatives", "14,892 identified · 23,464 de novo", C.slateLight, C.slate, null);
  addFooter(s, "No detector-derived “unidentified peptide” or “noise” residual is calculated");
  addNotes(s, "This is the single main result slide. The grey region is simply not assigned by this estimator; it is not classified as non-peptide, noise, or unidentified peptide signal.", [src.numbers, src.method]);
}

// 10 — fresh regeneration proof
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Reproducibility", "The final evidence was regenerated from source content, not timestamps.", "A changed RAW or database invalidates the derived cache.", 10);
  const items = [
    ["RAW", "SHA-256", "3 files", C.teal],
    ["XIC cache", "version 4", "6 content-bound files", C.orange],
    ["detector cache", "version 3", "3 content-bound files", C.rose],
    ["curation", "database SHA-256", "method + row count stamped", C.gold],
  ];
  for (let i = 0; i < items.length; i++) {
    const x = 72 + i * 284;
    shape(s, "roundRect", { left: x, top: 245, width: 250, height: 230 }, C.paper, C.faint, 1, "rounded-2xl");
    shape(s, "ellipse", { left: x + 86, top: 268, width: 78, height: 78 }, items[i][3], "none", 0);
    textBox(s, "✓", { left: x + 86, top: 281, width: 78, height: 48 }, 34, C.white, true, "center");
    textBox(s, items[i][0], { left: x + 20, top: 365, width: 210, height: 30 }, 20, C.ink, true, "center");
    textBox(s, items[i][1], { left: x + 20, top: 405, width: 210, height: 24 }, 14, items[i][3], true, "center");
    textBox(s, items[i][2], { left: x + 20, top: 438, width: 210, height: 24 }, 13, C.muted, false, "center");
  }
  pill(s, "8.4 min fresh extraction · 48 / 48 scientific gates", 314, 535, 652, C.tealLight, C.teal);
  addFooter(s, "Machine-readable scientific_results.fqproj round-tripped after generation");
  addNotes(s, "Content hashing closes the stale-cache loophole. The fresh run created a new cache directory that did not exist before execution.", [src.fresh, src.finalTalk, src.numbers, src.method]);
}

// 11 — detector controls
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "Independent diagnostic", "Resolved isotope series are useful—but not a peptide classifier.", "The detector requires ≥3 isotopes; shifted-spacing controls retain substantial area.", 11);
  bar(s, 92, 250, 450, 56.8274, 65, C.teal, "Independent ≥3-isotope detector", "56.83%");
  bar(s, 92, 332, 450, 14.8605, 65, C.orange, "Wrong spacing: 0.9700 Da", "14.86%");
  bar(s, 92, 414, 450, 9.4809, 65, C.rose, "Wrong spacing: 1.0367 Da", "9.48%");
  shape(s, "roundRect", { left: 900, top: 244, width: 286, height: 262 }, C.slateLight, C.faint, 1, "rounded-2xl");
  textBox(s, "What it can say", { left: 928, top: 270, width: 230, height: 30 }, 20, C.teal, true);
  textBox(s, "• isotope-series signal is abundant\n• z≥2 is more specific than z=1\n• result is definition-sensitive", { left: 928, top: 316, width: 230, height: 96 }, 16, C.ink, false);
  textBox(s, "What it cannot say", { left: 928, top: 427, width: 230, height: 28 }, 18, C.rose, true);
  textBox(s, "not peptide FDR; not a TIC partition", { left: 928, top: 464, width: 230, height: 32 }, 15, C.muted, false);
  textBox(s, "Fresh production diagnostic: 59.60% of TIC", { left: 292, top: 548, width: 696, height: 38 }, 21, C.slate, true, "center");
  addFooter(s, "Shifted-spacing controls are imperfect nulls; their purpose is to expose chance/coincident-series background");
  addNotes(s, "Keep this diagnostic separate from the 42.64% estimator. The independent audit and production implementation differ by several points, which is exactly why it must not define biological residual classes.", [src.audit, src.numbers, src.detector]);
}

// 12 — clustering semantics
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Curation semantics", "312 is not a biological venom-family count.", "It is the number of 70%-edge single-linkage connected components over targets, contaminants, and reversed decoys.", 12);
  const cards = [
    ["312", "all components", C.slateLight, C.slate],
    ["154", "target-bearing", C.goldLight, C.gold],
    ["67", "non-contaminant target", C.tealLight, C.teal],
    ["88", "contaminant-\nbearing target", C.orangeLight, C.orange],
    ["158", "pure decoy", C.roseLight, C.rose],
  ];
  for (let i = 0; i < cards.length; i++) {
    const x = 72 + i * 226;
    shape(s, "roundRect", { left: x, top: 250, width: 194, height: 180 }, cards[i][2], "none", 0, "rounded-2xl");
    textBox(s, cards[i][0], { left: x + 18, top: 275, width: 158, height: 58 }, 40, cards[i][3], true, "center");
    textBox(s, cards[i][1], { left: x + 18, top: 350, width: 158, height: 52 }, 16, C.ink, true, "center");
  }
  shape(s, "line", { left: 290, top: 510, width: 700, height: 0 }, "none", C.faint, 2);
  for (const x of [320, 540, 760, 980]) shape(s, "ellipse", { left: x, top: 494, width: 32, height: 32 }, C.teal, C.white, 2);
  textBox(s, "A—B ≥70% and B—C ≥70% does not imply A—C ≥70%.", { left: 248, top: 550, width: 784, height: 38 }, 21, C.orange, true, "center");
  addFooter(s, "The 67-component count is a database-cluster descriptor, not a taxonomic family count");
  addNotes(s, "There is one component that is both contaminant-bearing and non-contaminant-target-bearing, so those two categories are not additive. Use ‘connected component’ throughout the talk.", [src.numbers, src.curation, src.method]);
}

// 13 — identity sweep image
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "Identity sensitivity", "The 70% choice sits on a full, regenerated sweep.", "Counts fall as single-linkage components connect; target/decoy mixing rises below the cleaner region.", 13);
  shape(s, "roundRect", { left: 66, top: 210, width: 850, height: 444 }, C.white, C.faint, 1, "rounded-2xl");
  await addImage(s, src.identityFig, { left: 78, top: 220, width: 826, height: 424 }, "contain");
  shape(s, "roundRect", { left: 950, top: 228, width: 258, height: 388 }, C.dark, C.dark, 0, "rounded-2xl");
  textBox(s, "AT 70%", { left: 976, top: 256, width: 206, height: 24 }, 13, "#F0B08C", true);
  textBox(s, "312", { left: 976, top: 300, width: 206, height: 54 }, 38, C.white, true);
  textBox(s, "all components", { left: 976, top: 356, width: 206, height: 28 }, 16, "#C4D4D9", false);
  textBox(s, "67", { left: 976, top: 414, width: 206, height: 52 }, 36, "#66C8BD", true);
  textBox(s, "non-contaminant\ntarget components", { left: 976, top: 470, width: 206, height: 58 }, 16, "#C4D4D9", false);
  textBox(s, "1 mixed target/decoy", { left: 976, top: 558, width: 206, height: 24 }, 14, "#F0B08C", true);
  addFooter(s, "Exhaustive USEARCH sweep; source database hash and clustering method stamped");
  addNotes(s, "This slide replaces the old ‘312 venom families’ claim. The curve reports database connected components under a chosen algorithm and threshold.", [src.numbers, src.identityFig]);
}

// 14 — family contribution image
{
  const s = presentation.slides.add();
  s.background.fill = C.bg;
  addHeader(s, "Exploratory attribution", "Signal concentrates in expected toxin-related components—but the bars overlap.", "A peptide mapped to two components counts in both; this chart is not additive against pooled TIC.", 14);
  shape(s, "roundRect", { left: 66, top: 242, width: 868, height: 412 }, C.white, C.faint, 1, "rounded-2xl");
  await addImage(s, src.familyFig, { left: 78, top: 252, width: 844, height: 392 }, "contain");
  shape(s, "roundRect", { left: 970, top: 248, width: 238, height: 370 }, C.paper, C.faint, 1, "rounded-2xl");
  textBox(s, "TOP SIGNAL", { left: 994, top: 270, width: 190, height: 24 }, 13, C.orange, true);
  textBox(s, "SVMP", { left: 994, top: 306, width: 190, height: 38 }, 27, C.ink, true);
  textBox(s, "thrombin-like enzyme", { left: 994, top: 356, width: 190, height: 30 }, 17, C.ink, true);
  textBox(s, "C-type lectin", { left: 994, top: 397, width: 190, height: 30 }, 17, C.ink, true);
  shape(s, "line", { left: 994, top: 442, width: 190, height: 0 }, "none", C.faint, 1);
  textBox(s, "87 components reached", { left: 994, top: 462, width: 190, height: 28 }, 17, C.teal, true);
  textBox(s, "80% homology gate\ntarget–decoy competition\ncount-in-every-component", { left: 994, top: 510, width: 190, height: 72 }, 14, C.muted, false);
  addFooter(s, "Family-level attribution remains exploratory and is not peptide-FDR calibrated");
  addNotes(s, "Use this slide to discuss biological plausibility, not to claim compositional percentages. The unattributed de novo estimate remains large, and overlapping mappings make the bars deliberately non-additive.", [src.numbers, src.familyFig]);
}

// 15 — claims table
{
  const s = presentation.slides.add();
  s.background.fill = C.paper;
  addHeader(s, "What the evidence supports", "Be precise about the claim—and equally precise about the limits.", null, 15);
  shape(s, "roundRect", { left: 72, top: 210, width: 540, height: 410 }, C.tealLight, "none", 0, "rounded-2xl");
  shape(s, "roundRect", { left: 668, top: 210, width: 540, height: 410 }, C.roseLight, "none", 0, "rounded-2xl");
  textBox(s, "DEFENSIBLE", { left: 102, top: 238, width: 480, height: 32 }, 18, C.teal, true);
  textBox(s, "NOT DEFENSIBLE", { left: 698, top: 238, width: 480, height: 32 }, 18, C.rose, true);
  bullet(s, 108, 294, 454, "42.64% primary estimate", "under the stated identified-reference mass calibration", C.teal);
  bullet(s, 108, 382, 454, "Technical reproducibility", "41.57–43.24% across the three runs", C.teal);
  bullet(s, 108, 470, 454, "Substantial added de novo signal", "14.48 TIC points after reference calibration", C.teal);
  bullet(s, 704, 294, 454, "“51.69% explained”", "the unadjusted de novo envelope is known-inflated", C.rose);
  bullet(s, 704, 382, 454, "Detector residual biology", "59.60% has no peptide FDR and does not partition TIC", C.rose);
  bullet(s, 704, 470, 454, "“312 venom families”", "312 includes contaminants and reversed decoys", C.rose);
  addFooter(s, "No statistical confidence interval or external ground-truth recovery benchmark is claimed");
  addNotes(s, "This is the integrity slide. Say explicitly that the code is now more defensible, but the primary estimate still rests on a transparent calibration assumption rather than external ground truth.", [src.method, src.numbers, src.audit]);
}

// 16 — takeaway
{
  const s = presentation.slides.add();
  s.background.fill = C.dark;
  addHeader(s, "Take-home", "The direction survives. The headline changes.", null, 16, true);
  textBox(s, "42.64%", { left: 72, top: 224, width: 420, height: 98 }, 70, "#F0B08C", true);
  textBox(s, "primary recovered share of raw TIC", { left: 78, top: 330, width: 500, height: 40 }, 23, C.white, true);
  shape(s, "line", { left: 652, top: 220, width: 0, height: 330 }, "none", "#3B5660", 2);
  bullet(s, 710, 222, 450, "The result is auditable", "fresh RAW extraction, content hashes, machine-readable project", "#66C8BD", C.white, "#C4D4D9");
  bullet(s, 710, 330, 450, "The caveats are part of the result", "M+0 and raw six-window readings remain visible", "#F0B08C", C.white, "#C4D4D9");
  bullet(s, 710, 438, 450, "Family claims are narrower", "67 non-contaminant target connected components at 70%", "#D9B458", C.white, "#C4D4D9");
  pill(s, "Substantial recovery · calibrated estimate · no false closure", 72, 494, 536, "#23404A", "#DCE8EA");
  textBox(s, "Questions", { left: 72, top: 592, width: 260, height: 44 }, 30, C.white, true);
  addFooter(s, "FamilyQuant scientific revision · branch codex/scientific-talk-revision", true);
  addNotes(s, "Close in one sentence: identification plus de novo anchoring recovers substantial signal, but the scientifically defensible headline is a calibrated 42.64% estimate—not 51.69% explained TIC.", [src.numbers, src.method, src.fresh, src.regression]);
}

async function main() {
  await fs.mkdir(PREVIEWS, { recursive: true });
  for (const [index, slide] of presentation.slides.items.entries()) {
    const stem = `slide-${String(index + 1).padStart(2, "0")}`;
    await writeBlob(path.join(PREVIEWS, `${stem}.png`), await presentation.export({ slide, format: "png", scale: 1 }));
    await fs.writeFile(path.join(PREVIEWS, `${stem}.layout.json`), await (await slide.export({ format: "layout" })).text());
  }
  await writeBlob(path.join(OUT, "deck-montage.webp"), await presentation.export({ format: "webp", montage: true, scale: 1 }));
  const pptx = await PresentationFile.exportPptx(presentation);
  await pptx.save(path.join(OUT, "BrProt_FamilyQuant_Scientifically_Defensible.pptx"));
  const inspect = await presentation.inspect({ kind: "slide,textbox,shape,image,notes", maxChars: 60000 });
  await fs.writeFile(path.join(OUT, "deck-inspect.ndjson"), inspect.ndjson);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
