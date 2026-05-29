const fs = require("fs");
const path = require("path");

const outDir = __dirname;
const assetDir = path.join(outDir, "assets");

function imageData(name) {
  const filePath = path.join(assetDir, name);
  const ext = path.extname(name).toLowerCase().replace(".", "");
  const mime = ext === "jpg" || ext === "jpeg" ? "image/jpeg" : "image/png";
  return `data:${mime};base64,${fs.readFileSync(filePath).toString("base64")}`;
}

const desertImage = imageData("desert_playmode.png");
const rootGhost = imageData("root_ghost_preview.png");
const rootPlaced = imageData("root_final_placed.png");

const html = `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>Desert Raw 3 Demo Analysis</title>
<style>
  @page { size: A4; margin: 13mm; }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    font-family: "Segoe UI", Arial, sans-serif;
    color: #1f1f1f;
    background: #f6efe3;
    line-height: 1.36;
  }
  h1, h2, h3 { margin: 0; letter-spacing: 0; }
  h1 { font-size: 34px; line-height: 1.02; color: #27180f; max-width: 680px; }
  h2 { font-size: 20px; margin-bottom: 10px; color: #3b2415; }
  h3 { font-size: 13px; text-transform: uppercase; color: #744425; margin-bottom: 6px; }
  p { margin: 0 0 8px; }
  ul { margin: 8px 0 0 18px; padding: 0; }
  li { margin: 4px 0; }
  .page { page-break-after: always; min-height: 270mm; }
  .page:last-child { page-break-after: auto; }
  .hero {
    position: relative;
    min-height: 258mm;
    padding: 22mm 18mm 12mm;
    color: #fff;
    overflow: hidden;
    background:
      linear-gradient(180deg, rgba(21, 14, 9, .1), rgba(21, 14, 9, .88)),
      url("${desertImage}") center / cover no-repeat;
  }
  .hero .stamp {
    display: inline-block;
    padding: 6px 10px;
    border: 1px solid rgba(255,255,255,.55);
    background: rgba(0,0,0,.25);
    border-radius: 6px;
    font-size: 12px;
    margin-bottom: 14px;
  }
  .hero h1 { color: #fff4df; text-shadow: 0 2px 12px rgba(0,0,0,.6); }
  .hero .deck {
    position: absolute;
    left: 18mm;
    right: 18mm;
    bottom: 14mm;
    display: grid;
    grid-template-columns: 1.1fr .9fr;
    gap: 14px;
    align-items: stretch;
  }
  .verdict {
    background: rgba(27, 18, 12, .78);
    border: 1px solid rgba(255,255,255,.2);
    border-radius: 8px;
    padding: 14px;
  }
  .verdict strong { color: #ffdf85; }
  .scorecard {
    background: rgba(255, 244, 222, .92);
    color: #2b1a10;
    border-radius: 8px;
    padding: 12px;
  }
  .score-row { display: grid; grid-template-columns: 100px 1fr 34px; gap: 8px; align-items: center; margin: 8px 0; font-size: 12px; }
  .bar { height: 8px; border-radius: 99px; background: #d8c5aa; overflow: hidden; }
  .bar span { display: block; height: 100%; background: linear-gradient(90deg, #c4582b, #f1b64b); }
  .section { background: #fffaf1; border: 1px solid #e7d8c2; border-radius: 8px; padding: 14px; margin-bottom: 12px; }
  .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
  .grid3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
  .pill { display: inline-block; padding: 4px 8px; border-radius: 99px; background: #efe0ca; color: #5c351c; font-size: 11px; margin: 2px 3px 2px 0; }
  .callout { background: #2f241d; color: #fff4df; padding: 12px; border-radius: 8px; border-left: 5px solid #f0b84d; }
  .warn { background: #fff1e8; border-left: 5px solid #d55c35; }
  .good { background: #eef8ea; border-left: 5px solid #50a051; }
  .callout.warn, .callout.good { color: #2d2119; }
  .callout.warn h3, .callout.good h3 { color: #744425; }
  .visual { background: #f7ead8; border-radius: 8px; padding: 10px; border: 1px solid #e4ceb1; }
  .image-row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 8px; }
  .image-card img { width: 100%; height: 120px; object-fit: cover; border-radius: 6px; display: block; background: #5b5141; }
  .caption { font-size: 10px; color: #6a5644; margin-top: 4px; }
  .matrix { width: 100%; border-collapse: collapse; font-size: 11px; }
  .matrix th { text-align: left; color: #6a341b; background: #f0dfc5; }
  .matrix td, .matrix th { border: 1px solid #e2cfb3; padding: 7px; vertical-align: top; }
  .dot { width: 11px; height: 11px; display: inline-block; border-radius: 99px; margin-right: 5px; vertical-align: -1px; }
  .hi { background: #2f9d58; }
  .mid { background: #f0b84d; }
  .lo { background: #d85b35; }
  .roadmap { display: grid; grid-template-columns: 54px 1fr; gap: 8px; }
  .rank { width: 40px; height: 40px; border-radius: 50%; background: #4b2a18; color: #fff1d8; display: grid; place-items: center; font-weight: 700; }
  .feature { border-bottom: 1px solid #eadcc8; padding-bottom: 8px; margin-bottom: 8px; }
  .feature:last-child { border-bottom: 0; }
  .mini { font-size: 11px; color: #68533f; }
  .footer { position: fixed; bottom: 7mm; left: 13mm; right: 13mm; display: flex; justify-content: space-between; font-size: 9px; color: #866c55; }
  .chart-label { font-size: 10px; fill: #4e3929; }
  .source-list { font-size: 10px; color: #604a38; }
  .source-list a { color: #604a38; text-decoration: none; }
</style>
</head>
<body>
<div class="page hero">
  <div class="stamp">Honest demo readiness analysis - May 20, 2026</div>
  <h1>Desert Raw 3 has a real hook. It needs sharper player-facing goals before the stream.</h1>
  <div class="deck">
    <div class="verdict">
      <h2>Short Verdict</h2>
      <p><strong>Your game is not low-function technically.</strong> It already has procedural terrain, deformable sand, survival stats, heat/shade logic, sleep rules, crafting, placement, animals, loot, oasis systems, and root sand stabilization.</p>
      <p><strong>The risk is presentation:</strong> players may not immediately understand why these systems matter unless the demo gives them a simple mission and visible feedback.</p>
    </div>
    <div class="scorecard">
      <h3>Demo Readiness Score</h3>
      <div class="score-row"><span>Uniqueness</span><div class="bar"><span style="width:82%"></span></div><b>8.2</b></div>
      <div class="score-row"><span>Core loop</span><div class="bar"><span style="width:58%"></span></div><b>5.8</b></div>
      <div class="score-row"><span>Clarity</span><div class="bar"><span style="width:46%"></span></div><b>4.6</b></div>
      <div class="score-row"><span>Stream value</span><div class="bar"><span style="width:74%"></span></div><b>7.4</b></div>
      <p class="mini">Best stream angle: "I survive by changing the desert, not only by collecting items."</p>
    </div>
  </div>
</div>

<div class="page" style="padding: 12mm 13mm;">
  <div class="section">
    <h2>What The Game Already Has</h2>
    <div class="grid3">
      <div><h3>World</h3><span class="pill">Procedural sand chunks</span><span class="pill">Mountains</span><span class="pill">Oasis basin</span><span class="pill">Palm / bush / grass spawning</span></div>
      <div><h3>Survival</h3><span class="pill">Health</span><span class="pill">Hunger</span><span class="pill">Thirst</span><span class="pill">Sleep</span><span class="pill">Temperature</span><span class="pill">Shade check</span></div>
      <div><h3>Interaction</h3><span class="pill">Dig / place sand</span><span class="pill">Inventory</span><span class="pill">Crafting</span><span class="pill">Item placement</span><span class="pill">Melee / ranged</span></div>
      <div><h3>Living World</h3><span class="pill">2 deer target</span><span class="pill">1 kitty target</span><span class="pill">Animal avoidance</span><span class="pill">Grass eating</span><span class="pill">Loot drops</span></div>
      <div><h3>Visual Systems</h3><span class="pill">Day / night</span><span class="pill">Moon visual</span><span class="pill">Underwater effect</span><span class="pill">UI stats</span><span class="pill">Root ghost preview</span></div>
      <div><h3>Data Scale</h3><span class="pill">32 item assets</span><span class="pill">14 recipes</span><span class="pill">14 plant data assets</span><span class="pill">4 animal data assets</span></div>
    </div>
  </div>

  <div class="section">
    <h2>Mechanics Map</h2>
    <svg viewBox="0 0 760 280" width="100%" height="255" role="img">
      <defs>
        <filter id="shadow"><feDropShadow dx="0" dy="2" stdDeviation="3" flood-color="#8a5b2d" flood-opacity=".28"/></filter>
      </defs>
      <rect x="10" y="15" width="185" height="84" rx="8" fill="#ffe3a8" stroke="#d8a448" filter="url(#shadow)"/>
      <text x="25" y="43" class="chart-label" style="font-size:15px;font-weight:700">Desert Surface</text>
      <text x="25" y="65" class="chart-label">Dig, place, flow, chunks</text>
      <text x="25" y="83" class="chart-label">Root stabilization mask</text>

      <rect x="290" y="15" width="180" height="84" rx="8" fill="#dff2ff" stroke="#76a9cf" filter="url(#shadow)"/>
      <text x="306" y="43" class="chart-label" style="font-size:15px;font-weight:700">Heat / Shade</text>
      <text x="306" y="65" class="chart-label">Temperature changes thirst</text>
      <text x="306" y="83" class="chart-label">Shadow shelters player</text>

      <rect x="565" y="15" width="180" height="84" rx="8" fill="#e6f6d8" stroke="#7faf5b" filter="url(#shadow)"/>
      <text x="581" y="43" class="chart-label" style="font-size:15px;font-weight:700">Survival Stats</text>
      <text x="581" y="65" class="chart-label">Health, hunger, thirst</text>
      <text x="581" y="83" class="chart-label">Sleep deprivation</text>

      <rect x="145" y="178" width="190" height="84" rx="8" fill="#f5dfcb" stroke="#bc7d4c" filter="url(#shadow)"/>
      <text x="161" y="206" class="chart-label" style="font-size:15px;font-weight:700">Craft / Place</text>
      <text x="161" y="228" class="chart-label">Root, shovel, spear</text>
      <text x="161" y="246" class="chart-label">Preview and valid placement</text>

      <rect x="430" y="178" width="190" height="84" rx="8" fill="#eee5ff" stroke="#9983d0" filter="url(#shadow)"/>
      <text x="446" y="206" class="chart-label" style="font-size:15px;font-weight:700">Oasis / Animals</text>
      <text x="446" y="228" class="chart-label">Water, fish, deer, kitty</text>
      <text x="446" y="246" class="chart-label">Loot and food loop</text>

      <path d="M195 57 C235 57,250 57,290 57" stroke="#8b6b3e" stroke-width="3" fill="none" marker-end="url(#arrow)"/>
      <path d="M470 57 C505 57,525 57,565 57" stroke="#5d7d94" stroke-width="3" fill="none"/>
      <path d="M240 178 C235 135,210 125,170 99" stroke="#b87544" stroke-width="3" fill="none"/>
      <path d="M525 178 C535 135,570 125,615 99" stroke="#8066b7" stroke-width="3" fill="none"/>
      <path d="M335 220 C365 220,395 220,430 220" stroke="#806b51" stroke-width="3" fill="none"/>
      <circle cx="380" cy="136" r="42" fill="#2f241d"/>
      <text x="347" y="132" fill="#fff4df" style="font-size:13px;font-weight:700">Core Hook</text>
      <text x="340" y="151" fill="#ffdf85" style="font-size:11px">change desert to live</text>
    </svg>
  </div>

  <div class="grid2">
    <div class="section good">
      <h2>Honest Strength</h2>
      <p>The most original system is not "desert survival." The original part is the relationship between <b>sand shape, root placement, shade, temperature, and sleep</b>.</p>
      <p>That can feel different from games where desert is mostly a backdrop.</p>
    </div>
    <div class="section warn">
      <h2>Honest Weakness</h2>
      <p>The game has many systems, but the player needs clearer reasons to use them. A stream viewer should understand the goal in 10 seconds: heat is killing me, I need shade, roots stabilize sand, crafting changes survival odds.</p>
    </div>
  </div>
</div>

<div class="page" style="padding: 12mm 13mm;">
  <div class="section">
    <h2>Uniqueness Compared To Desert / Survival Games</h2>
    <table class="matrix">
      <tr><th>Game reference</th><th>Common focus</th><th>Where your demo can differ</th></tr>
      <tr><td>Starsand</td><td>Desert survival, crafting, shelter, exploration.</td><td><span class="dot hi"></span>Your mutable sand and root-stability angle is more physical and systemic.</td></tr>
      <tr><td>Conan Exiles</td><td>Open-world survival, building, combat, domination.</td><td><span class="dot mid"></span>You cannot out-content it, but you can be more intimate and desert-mechanical.</td></tr>
      <tr><td>Wildmender</td><td>Restore a desert through gardening and magic-like growth.</td><td><span class="dot mid"></span>It owns restoration fantasy; your angle should be survival engineering with sand physics.</td></tr>
      <tr><td>The Planet Crafter</td><td>Terraform a hostile planet into a livable ecosystem.</td><td><span class="dot mid"></span>It is large-scale transformation; your strength is moment-to-moment terrain manipulation.</td></tr>
    </table>
    <p class="mini" style="margin-top:8px;">Conclusion: do not pitch only "desert survival." Pitch "survive by reshaping sand, managing shade, and placing roots that physically change the terrain behavior."</p>
  </div>

  <div class="section">
    <h2>Visual Proof Of The Best Hook</h2>
    <div class="image-row">
      <div class="image-card">
        <img src="${rootGhost}" alt="Root ghost preview">
        <div class="caption">Root placement preview: a strong stream moment because viewers understand green = valid instantly.</div>
      </div>
      <div class="image-card">
        <img src="${rootPlaced}" alt="Placed root">
        <div class="caption">Placed root: should visibly brace sand, create strategy, and become part of the survival loop.</div>
      </div>
    </div>
  </div>

  <div class="section">
    <h2>Feature Priority: Add Few, Make Them Loud</h2>
    <div class="feature">
      <div class="roadmap"><div class="rank">1</div><div><h3>Demo Objective System</h3><p>Add a small on-screen goal chain: "Find shade -> craft root -> stabilize dune -> sleep safely -> hunt/cook." This is the highest ROI before stream because it makes existing mechanics readable.</p><p class="mini">Scope: one UI panel, 5 objective states, checkmarks, no big architecture.</p></div></div>
    </div>
    <div class="feature">
      <div class="roadmap"><div class="rank">2</div><div><h3>Heat Feedback Pass</h3><p>Add heat shimmer, red edge vignette, heartbeat/sand wind audio, and a simple "Shade: -15 C" popup when entering shadow. Your temperature system exists; make it visible.</p></div></div>
    </div>
    <div class="feature">
      <div class="roadmap"><div class="rank">3</div><div><h3>Root Stabilization Visual</h3><p>When a root is placed, show a short pulse across nearby sand and a small "sand stabilized" marker. This makes the uncommon mechanic understandable.</p></div></div>
    </div>
    <div class="feature">
      <div class="roadmap"><div class="rank">4</div><div><h3>Mini Sandstorm Event</h3><p>Every few minutes, reduce visibility, push loose sand, drain thirst faster, and make shelter/shade matter. This turns desert from background into antagonist.</p></div></div>
    </div>
    <div class="feature">
      <div class="roadmap"><div class="rank">5</div><div><h3>One Signature Oasis Moment</h3><p>Make oasis discovery heal/cool the player, show fish, and give water refill. Do not build a huge biome now; build one memorable relief beat.</p></div></div>
    </div>
  </div>
</div>

<div class="page" style="padding: 12mm 13mm;">
  <div class="section">
    <h2>What To Avoid Before Stream</h2>
    <div class="grid2">
      <div class="callout warn"><h3>Avoid</h3><p>Adding many new animals, huge enemy AI, base-building, or story systems right now. They will dilute your best hook and create bugs before the demo.</p></div>
      <div class="callout good"><h3>Do Instead</h3><p>Make the mechanics already in the project feel intentional: UI prompts, effects, one demo route, and reliable spawn/recipe setup.</p></div>
    </div>
  </div>

  <div class="section">
    <h2>Suggested 6 Minute Stream Demo Script</h2>
    <svg viewBox="0 0 760 240" width="100%" height="218">
      <g font-family="Segoe UI, Arial" font-size="12" fill="#2d2119">
        <rect x="8" y="30" width="118" height="70" rx="8" fill="#ffe1a8" stroke="#d5a14f"/>
        <text x="23" y="58" font-weight="700">1. Heat Threat</text><text x="23" y="80">show thirst/temp</text>
        <rect x="160" y="30" width="118" height="70" rx="8" fill="#e7f4ff" stroke="#7bb0d7"/>
        <text x="179" y="58" font-weight="700">2. Dig Sand</text><text x="179" y="80">reshape dune</text>
        <rect x="312" y="30" width="118" height="70" rx="8" fill="#e8f8dc" stroke="#78ad58"/>
        <text x="330" y="58" font-weight="700">3. Root Place</text><text x="330" y="80">stabilize area</text>
        <rect x="464" y="30" width="118" height="70" rx="8" fill="#f4e1ff" stroke="#aa80cf"/>
        <text x="486" y="58" font-weight="700">4. Sleep</text><text x="486" y="80">shade rule</text>
        <rect x="616" y="30" width="118" height="70" rx="8" fill="#f7dccf" stroke="#cb805c"/>
        <text x="633" y="58" font-weight="700">5. Hunt/Craft</text><text x="633" y="80">deer + food</text>
        <path d="M126 65 H160 M278 65 H312 M430 65 H464 M582 65 H616" stroke="#8b6b3e" stroke-width="3"/>
        <rect x="120" y="142" width="520" height="56" rx="8" fill="#2f241d"/>
        <text x="145" y="166" fill="#fff4df" font-weight="700">One sentence to repeat on stream:</text>
        <text x="145" y="187" fill="#ffdf85">"The desert is not just a map. I can dig it, brace it, hide in shade, and survive by changing it."</text>
      </g>
    </svg>
  </div>

  <div class="section">
    <h2>Technical Risks To Fix Or Hide Before Stream</h2>
    <ul>
      <li><b>Clarity risk:</b> root placement and sand stabilization are strong but invisible unless you add pulse/label feedback.</li>
      <li><b>Balance risk:</b> item data has many default-looking values. Stream viewers will notice if every plant restores or damages strangely.</li>
      <li><b>Spawn risk:</b> keep the two-deer/one-kitty spawner simple for demo stability.</li>
      <li><b>UI risk:</b> white slots/placeholders in older screenshots read unfinished. Use filled icons or hide empty panels during the stream.</li>
      <li><b>Performance risk:</b> sand, vegetation, and oasis systems are ambitious; keep camera route controlled for the demo.</li>
    </ul>
  </div>

  <div class="section">
    <h2>Final Honest Recommendation</h2>
    <p><b>Add functions, but only the kind that explain the game.</b> You do not need more raw systems before the stream. You need a clearer demo objective, stronger feedback, and one dramatic desert event.</p>
    <p>The best version of this game is not "many survival features." It is a focused survival sandbox where sand physics, shade, roots, heat, and sleep connect into one readable desert machine.</p>
  </div>

  <div class="section source-list">
    <h2>Sources And Local Evidence</h2>
    <p>Local project: Unity 6000.3.8f1, URP 17.3.0, scene components inspected from MainScene. Evidence scripts: PlayerStats, SleepSystem, SandChunk, TerrainManager, ItemPlacer, AnimalSpawner, CraftingSystem.</p>
    <p>External references used for genre comparison: <a href="https://store.steampowered.com/app/1380220/Starsand/">Starsand</a>, <a href="https://store.steampowered.com/app/440900/Conan_Exiles/">Conan Exiles</a>, <a href="https://store.steampowered.com/app/1599330/Wildmender/">Wildmender</a>, and <a href="https://store.steampowered.com/app/1284190/The_Planet_Crafter/">The Planet Crafter</a>.</p>
  </div>
</div>
</body>
</html>`;

fs.writeFileSync(path.join(outDir, "desert_demo_analysis.html"), html, "utf8");
console.log(path.join(outDir, "desert_demo_analysis.html"));
