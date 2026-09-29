"use strict";
const $ = (id) => document.getElementById(id);
const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
let draft,
  saved = "",
  revision = 0,
  token = "",
  selected = "",
  busy = false,
  connected = false,
  running = false;
const dirty = () => draft && JSON.stringify(draft) !== saved;
const keyName = (key) =>
  key === 32 ? "SP" : key ? String.fromCharCode(key) : "—";
const keyOptions = [
  0,
  32,
  ...Array.from({ length: 10 }, (_, i) => 48 + i),
  ...Array.from({ length: 26 }, (_, i) => 65 + i).filter(
    (k) => ![65, 68, 83, 87].includes(k),
  ),
];
function message(text = "") {
  $("message").textContent = text;
  $("message").hidden = !text;
}
async function api(path, method = "GET", body) {
  const response = await fetch(path, {
    method,
    headers:
      body === undefined
        ? {}
        : { "Content-Type": "application/json", "X-Bloodybot2-Token": token },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(7000),
  });
  const result = await response.json();
  if (!response.ok)
    throw new Error(result.error || `请求失败 (${response.status})`);
  return result;
}
function controls() {
  $("dirty").textContent = dirty() ? "有未保存的修改" : "已保存";
  $("save").disabled = !dirty() || busy || !connected;
  $("run").disabled = !draft || dirty() || busy || !connected || running;
  $("run").textContent = draft?.preview !== false ? "启动预览" : "启动战斗";
  $("stop").disabled = !connected;
  $("workspace").inert = busy || !draft;
  $("mode-chip").textContent =
    (draft?.preview !== false ? "预览模式" : "实际按键") +
    (dirty() ? " · 草稿" : "");
}
function setBusy(value) {
  busy = value;
  controls();
}
async function load() {
  setBusy(true);
  try {
    const data = await api("/api/bootstrap");
    draft = data.config;
    saved = JSON.stringify(draft);
    revision = data.revision;
    token = data.token;
    selected = draft.combat.rules[0]?.id || "";
    connected = true;
    $("warning").textContent = data.warning;
    $("warning").hidden = !data.warning;
    renderConfig();
    renderRules();
    renderEditor();
    message();
  } catch (error) {
    message(error.message);
  } finally {
    setBusy(false);
  }
}
function renderConfig() {
  $("config-name").value = draft.name;
  $("preview").checked = draft.preview;
  $("combat-enabled").checked = draft.combat.enabled;
  $("monitor-character").value = draft.monitorCharacter;
  $("controller").checked = draft.allowControllerWithoutChat;
  controls();
}
function ruleSummary(r) {
  const rarities = [
    [1, "普通"],
    [2, "魔法"],
    [4, "稀有"],
    [8, "Unique"],
  ]
    .filter(([v]) => r.rarities & v)
    .map(([, n]) => n)
    .join(" / ");
  return `${r.enabled ? "" : "已禁用 · "}${r.requireEnemy ? rarities + " · " + r.range + " 格" : "人物状态"} · ${r.cooldownMilliseconds / 1000}s`;
}
function renderRules() {
  const rules = draft.combat.rules;
  $("rule-count").textContent = `${rules.length} / 32`;
  $("add").disabled = rules.length >= 32;
  $("rules").innerHTML = rules.length
    ? rules
        .map(
          (r, i) =>
            `<button class="rule ${r.id === selected ? "selected" : ""} ${r.enabled ? "" : "disabled"}" data-rule="${esc(r.id)}" aria-pressed="${r.id === selected}"><span class="number">${String(i + 1).padStart(2, "0")}</span><span><strong>${esc(r.name)}</strong><small>${esc(ruleSummary(r))}</small></span><span class="key">${keyName(r.key)}</span></button>`,
        )
        .join("")
    : '<div class="empty">还没有技能规则<br>添加技能，开始编排战斗。</div>';
}
function field(key, label, value, min, max, step = 1) {
  return `<label class="field">${label}<input data-key="${key}" type="number" min="${min}" max="${max}" step="${step}" value="${value}"></label>`;
}
function textField(key, label, value, list = "", hint = "") {
  return `<label class="field">${label}<input data-key="${key}" value="${esc(value)}" maxlength="160" autocomplete="off" ${list ? 'list="' + list + '"' : ""}>${hint ? "<small>" + hint + "</small>" : ""}</label>`;
}
function renderEditor() {
  const r = draft.combat.rules.find((r) => r.id === selected);
  if (!r) {
    $("editor").innerHTML =
      '<div class="empty"><div class="empty-icon">◇</div><h3>让技能在合适的时候触发</h3><p>设置按键，选择敌人或人物状态条件，再用预览确认触发结果。</p><button class="button primary" data-action="add">＋ 添加第一个技能</button></div>';
    return;
  }
  const index = draft.combat.rules.indexOf(r);
  $("editor").innerHTML =
    `<div class="editor-head"><div><div class="eyebrow">ACTION ${String(index + 1).padStart(2, "0")}</div><h2>技能规则</h2><p>当前瞄准 · 单次短按</p></div><div class="editor-actions"><button class="button" data-action="up" aria-label="上移" ${index === 0 ? "disabled" : ""}>↑</button><button class="button" data-action="down" aria-label="下移" ${index === draft.combat.rules.length - 1 ? "disabled" : ""}>↓</button><button class="button" data-action="copy" ${draft.combat.rules.length >= 32 ? "disabled" : ""}>复制</button><button class="button danger" data-action="delete">删除</button></div></div>
    <div class="form-row">${textField("name", "技能名称", r.name)}<label class="field">游戏按键<select data-key="key">${keyOptions.map((k) => `<option value="${k}" ${k === r.key ? "selected" : ""}>${k === 0 ? "选择按键" : k === 32 ? "Space 空格" : keyName(k)}</option>`).join("")}</select></label></div>
    <label class="switch-row"><span>启用此规则</span><input data-key="enabled" type="checkbox" ${r.enabled ? "checked" : ""}></label>
    <div class="form-section"><label class="switch-row"><span><strong>需要附近敌人</strong><small>只计入可攻击的存活敌人。</small></span><input data-key="requireEnemy" type="checkbox" ${r.requireEnemy ? "checked" : ""}></label>
    <div class="rarities">${[
      [1, "普通", ""],
      [2, "魔法", "magic"],
      [4, "稀有", "rare"],
      [8, "Unique", "unique"],
    ]
      .map(
        ([v, n, c]) =>
          `<label class="${c}"><input type="checkbox" data-rarity="${v}" ${r.rarities & v ? "checked" : ""}>${n}</label>`,
      )
      .join("")}</div>
    <div class="form-row">${field("range", "检测范围 / 格", r.range, 1, 150, 0.5)}${field("minimumEnemies", "至少敌人数", r.minimumEnemies, 1, 100)}</div></div>
    <div class="form-section"><h3>人物状态 <small>· 所有启用的条件同时满足</small></h3><div class="form-row three">
    ${[
      ["useLifeBelow", "lifeBelow", "生命"],
      ["useEnergyShieldBelow", "energyShieldBelow", "护盾"],
      ["useManaBelow", "manaBelow", "魔力"],
    ]
      .map(
        ([use, key, name]) =>
          `<div><label class="threshold"><input type="checkbox" data-key="${use}" ${r[use] ? "checked" : ""}>${name}低于</label>${field(key, "% 未保留上限", r[key], 1, 100)}</div>`,
      )
      .join("")}</div>
    <div class="form-row">${field("minimumMana", "最低魔力 %（0 = 不限制）", r.minimumMana, 0, 100)}${textField("readySkill", "技能就绪（可选）", r.readySkill, "skill-options", "使用扫描出的完整内部名称；空白表示不检查。")}</div>
    <div class="form-row">${textField("requiredBuff", "存在 Buff（可选）", r.requiredBuff, "buff-options")}${textField("missingBuff", "缺少 Buff（可选）", r.missingBuff, "buff-options")}</div><p class="footnote">Buff 名称不区分大小写，支持部分匹配。条件所需数据未知时不会触发。</p></div>
    <div class="form-section"><h3>施法节奏</h3><div class="form-row three">${field("cooldownMilliseconds", "重复间隔 / ms", r.cooldownMilliseconds, 300, 600000)}${field("pressMilliseconds", "按住时长 / ms", r.pressMilliseconds, 30, 200)}${field("pauseMilliseconds", "施法停顿 / ms", r.pauseMilliseconds, 0, 2000)}</div><p class="footnote">同键规则共享间隔；两次输入至少间隔 300 ms。输入成功只代表系统接受按键。</p></div>`;
}
function newRule(template = "elite") {
  return {
    id: crypto.randomUUID().replaceAll("-", ""),
    name:
      template === "life"
        ? "低生命技能"
        : template === "buff"
          ? "补充 Buff"
          : "精英 / Unique 技能",
    enabled: true,
    key: 0,
    requireEnemy: template === "elite",
    rarities: 12,
    range: 50,
    minimumEnemies: 1,
    useLifeBelow: template === "life",
    lifeBelow: 35,
    useEnergyShieldBelow: false,
    energyShieldBelow: 50,
    useManaBelow: false,
    manaBelow: 30,
    minimumMana: 0,
    requiredBuff: "",
    missingBuff: "",
    readySkill: "",
    cooldownMilliseconds: 2000,
    pressMilliseconds: 80,
    pauseMilliseconds: 250,
  };
}
function add(template = "elite") {
  if (!draft || draft.combat.rules.length >= 32) return;
  const rule = newRule(template);
  draft.combat.rules.push(rule);
  selected = rule.id;
  renderRules();
  renderEditor();
  controls();
}
$("rules").addEventListener("click", (e) => {
  const button = e.target.closest("[data-rule]");
  if (button) {
    selected = button.dataset.rule;
    renderRules();
    renderEditor();
  }
});
$("editor").addEventListener("input", (e) => {
  const r = draft.combat.rules.find((r) => r.id === selected);
  if (!r) return;
  const element = e.target;
  if (element.dataset.rarity)
    r.rarities = [
      ...$("editor").querySelectorAll("[data-rarity]:checked"),
    ].reduce((n, c) => n | Number(c.dataset.rarity), 0);
  else if (element.dataset.key)
    r[element.dataset.key] =
      element.type === "checkbox"
        ? element.checked
        : element.type === "number" || element.tagName === "SELECT"
          ? Number(element.value)
          : element.value;
  renderRules();
  controls();
});
$("editor").addEventListener("click", (e) => {
  const action = e.target.closest("[data-action]")?.dataset.action;
  if (!action) return;
  if (action === "add") {
    add();
    return;
  }
  const rules = draft.combat.rules,
    index = rules.findIndex((r) => r.id === selected);
  if (index < 0) return;
  if (action === "delete") {
    rules.splice(index, 1);
    selected = rules[Math.min(index, rules.length - 1)]?.id || "";
  } else if (action === "copy" && rules.length < 32) {
    const copy = structuredClone(rules[index]);
    copy.id = crypto.randomUUID().replaceAll("-", "");
    copy.name = (copy.name + " 副本").slice(0, 80);
    rules.splice(index + 1, 0, copy);
    selected = copy.id;
  } else if (action === "up" && index > 0)
    [rules[index - 1], rules[index]] = [rules[index], rules[index - 1]];
  else if (action === "down" && index < rules.length - 1)
    [rules[index], rules[index + 1]] = [rules[index + 1], rules[index]];
  renderRules();
  renderEditor();
  controls();
});
$("add").addEventListener("click", () => add());
document
  .querySelectorAll("[data-template]")
  .forEach((button) =>
    button.addEventListener("click", () => add(button.dataset.template)),
  );
document.querySelectorAll("[data-tab]").forEach((button) =>
  button.addEventListener("click", () => {
    const tab = button.dataset.tab;
    document
      .querySelectorAll(".tab-panel")
      .forEach((panel) => (panel.hidden = panel.id !== "tab-" + tab));
    document
      .querySelectorAll("[data-tab]")
      .forEach((item) => item.classList.toggle("active", item === button));
    $("page-title").textContent = {
      combat: "战斗编排",
      monitor: "实时监测",
      config: "配置文件",
    }[tab];
    $("page-crumb").textContent = tab.toUpperCase();
  }),
);
for (const [id, key] of [
  ["config-name", "name"],
  ["preview", "preview"],
  ["monitor-character", "monitorCharacter"],
  ["controller", "allowControllerWithoutChat"],
])
  $(id).addEventListener("input", (e) => {
    draft[key] =
      e.target.type === "checkbox" ? e.target.checked : e.target.value;
    controls();
  });
$("combat-enabled").addEventListener("input", (e) => {
  draft.combat.enabled = e.target.checked;
  controls();
});
async function save() {
  if (busy || !dirty()) return;
  setBusy(true);
  message();
  try {
    const data = await api("/api/config", "PUT", { revision, config: draft });
    draft = data.config;
    saved = JSON.stringify(draft);
    revision = data.revision;
    token = data.token;
    $("warning").hidden = true;
    renderConfig();
    renderRules();
    renderEditor();
    running = false;
  } catch (error) {
    message(error.message);
  } finally {
    setBusy(false);
  }
}
$("save").addEventListener("click", save);
document.addEventListener("keydown", (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
    e.preventDefault();
    save();
  }
});
for (const [id, value] of [
  ["run", true],
  ["stop", false],
])
  $(id).addEventListener("click", async () => {
    try {
      const status = await api("/api/run", "POST", { running: value });
      renderStatus(status);
      message();
    } catch (error) {
      message(error.message);
    }
  });
$("reload").addEventListener("click", () => {
  if (!dirty() || confirm("丢弃未保存的修改并重新加载？")) load();
});
$("export").addEventListener("click", () => {
  const blob = new Blob([JSON.stringify(draft, null, 2)], {
      type: "application/json",
    }),
    url = URL.createObjectURL(blob),
    link = document.createElement("a");
  link.href = url;
  link.download = "bloodybot2-config.json";
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});
$("import").addEventListener("click", () => $("import-file").click());
$("import-file").addEventListener("change", async (e) => {
  const file = e.target.files[0];
  e.target.value = "";
  if (!file) return;
  try {
    if (file.size > 256 * 1024) throw new Error("配置超过 256 KiB。");
    const imported = JSON.parse(await file.text());
    // Strict server validation remains authoritative. Reject malformed drafts before rendering controls.
    if (
      imported.schemaVersion !== 1 ||
      typeof imported.name !== "string" ||
      typeof imported.preview !== "boolean" ||
      typeof imported.monitorCharacter !== "string" ||
      typeof imported.allowControllerWithoutChat !== "boolean" ||
      typeof imported.combat?.enabled !== "boolean" ||
      !Array.isArray(imported.combat.rules) ||
      imported.combat.rules.length > 32
    )
      throw new Error("不是有效的 Bloodybot2 v1 配置。");
    const sample = newRule();
    if (
      imported.combat.rules.some(
        (r) =>
          !r ||
          Object.keys(sample).some(
            (key) => typeof r[key] !== typeof sample[key],
          ),
      )
    )
      throw new Error("技能规则字段不完整。");
    if (dirty() && !confirm("用导入文件替换当前草稿？")) return;
    draft = imported;
    selected = draft.combat.rules[0]?.id || "";
    renderConfig();
    renderRules();
    renderEditor();
    message("已导入草稿，请核对并保存。");
  } catch (error) {
    message(error.message);
  }
});
function renderStatus(status) {
  running = status.running;
  $("run-dot").className = "dot" + (running ? " on" : "");
  $("run-label").textContent = running
    ? status.preview
      ? "预览运行中"
      : "战斗运行中"
    : "已停止";
  $("reason").textContent = status.reason;
  const age = status.updatedAt
    ? Math.max(0, Date.now() - status.updatedAt)
    : Infinity;
  $("snapshot-age").textContent = Number.isFinite(age)
    ? age > 1500
      ? "快照已过期"
      : `${Math.round(age)} ms 前`
    : "尚无快照";
  $("observed-player").textContent =
    (status.character || "未读取到角色") +
    " · 区域 " +
    (status.area || "—") +
    " · " +
    status.navigation;
  for (const key of ["life", "shield", "mana"]) {
    $(key + "-value").textContent =
      status[key] === null ? "—" : status[key].toFixed(0) + "%";
    $(key).value = status[key] ?? 0;
  }
  for (const key of ["normal", "magic", "rare", "unique"])
    $(key + "-count").textContent = status[key];
  const ready = new Set(status.readySkills),
    skills = [...new Set([...status.skills, ...status.readySkills])].sort();
  $("skill-count").textContent = skills.length + " 个";
  $("skills").innerHTML = skills.length
    ? skills
        .map(
          (s) =>
            `<button class="${ready.has(s) ? "ready" : ""}" data-copy="${esc(s)}" title="点击复制">${esc(s)}</button>`,
        )
        .join("")
    : "<small>等待技能数据</small>";
  $("buffs").innerHTML = status.buffs.length
    ? status.buffs
        .map(
          (s) =>
            `<button data-copy="${esc(s)}" title="点击复制">${esc(s)}</button>`,
        )
        .join("")
    : "<small>当前没有可读 Buff</small>";
  for (const [id, items] of [
    ["player-options", status.players],
    ["skill-options", skills],
    ["buff-options", status.buffs],
  ])
    $(id).innerHTML = items
      .map((s) => `<option value="${esc(s)}"></option>`)
      .join("");
  $("accepted").textContent = status.acceptedInputs + " 次输入";
  $("candidate").textContent = status.action
    ? `${status.preview ? "预览建议" : "当前建议"}：${status.action.name} · ${keyName(status.action.key)} · 匹配 ${status.action.matchingEnemies} 个敌人`
    : "尚无匹配的技能";
  $("events").innerHTML = status.events.length
    ? [...status.events]
        .reverse()
        .map(
          (e) =>
            `<div class="event"><time>${new Date(e.at).toLocaleTimeString("zh-CN", { hour12: false })}</time><span>${esc(e.message)}</span></div>`,
        )
        .join("")
    : '<span class="muted">启动后会显示输入和停止记录。</span>';
  controls();
}
document.addEventListener("click", async (e) => {
  const value = e.target.closest("[data-copy]")?.dataset.copy;
  if (value)
    try {
      await navigator.clipboard.writeText(value);
      message("已复制：" + value);
    } catch {
      message("复制失败，请手动选择名称。");
    }
});
async function poll() {
  try {
    renderStatus(await api("/api/status"));
    connected = true;
    $("connection").textContent = "本地服务已连接";
    $("connection-dot").className = "dot on";
  } catch {
    connected = false;
    $("connection").textContent = "连接已断开";
    $("connection-dot").className = "dot error";
    $("reason").textContent =
      "无法连接本地服务，请检查 GameHelper 中的 Bloodybot2。";
  }
  controls();
  setTimeout(poll, 600);
}
window.addEventListener("beforeunload", (e) => {
  if (dirty()) {
    e.preventDefault();
    e.returnValue = "";
  }
});
load().then(poll);
