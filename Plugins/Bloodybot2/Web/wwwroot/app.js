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
  running = false,
  recording = null,
  captureTimer;
const pendingOptions = new Map();
const dirty = () => draft && JSON.stringify(draft) !== saved;
const keyName = (key) =>
  key === 32
    ? "Space"
    : key >= 112 && key <= 135
      ? `F${key - 111}`
      : {
          8: "Backspace",
          9: "Tab",
          19: "Pause",
          20: "CapsLock",
          33: "PageUp",
          34: "PageDown",
          35: "End",
          36: "Home",
          45: "Insert",
          46: "Delete",
          144: "NumLock",
          145: "ScrollLock",
        }[key] || (key ? String.fromCharCode(key) : "—");
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
  $("dirty").hidden = !dirty();
  $("profile-label").textContent = draft?.name || "—";
  $("save").disabled = !dirty() || busy || !connected || !draft?.mode;
  $("run").disabled = !draft?.mode || dirty() || busy || !connected || running;
  $("run").textContent = draft?.preview !== false ? "启动预览" : "启动 Follow";
  $("toggle-key-hint").textContent = keyName(draft?.toggleKey || 117);
  $("stop").disabled = !connected;
  $("workspace").inert = busy || !draft;
  $("follow-nav").hidden = draft?.mode !== "Follow";
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
    if (!draft.mode) selectTab("general");
    message();
  } catch (error) {
    message(error.message);
  } finally {
    setBusy(false);
  }
}
function renderConfig() {
  $("mode").value = draft.mode;
  $("toggle-record").querySelector("kbd").textContent = keyName(
    draft.toggleKey,
  );
  for (const element of document.querySelectorAll("[data-follow]")) {
    const value = draft.follow[element.dataset.follow];
    if (element.type === "checkbox") element.checked = value;
    else element.value = value;
  }
  $("follow-coop-fields").hidden = !draft.follow.localCoopFollow;
  $("config-name").value = draft.name;
  $("preview").checked = draft.preview;
  $("combat-enabled").checked = draft.combat.enabled;
  $("monitor-character").value = draft.monitorCharacter;
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
  $("nav-action-count").textContent = rules.length;
  $("add").disabled = rules.length >= 32;
  $("rules").innerHTML = rules.length
    ? rules
        .map(
          (r, i) => `
    <div class="action-item rule ${r.id === selected ? "selected" : ""} ${r.enabled ? "" : "action-disabled"}" data-rule="${esc(r.id)}" role="button" tabindex="0" aria-pressed="${r.id === selected}">
      <span class="drag-handle rule-mark" aria-hidden="true">◆</span><span class="action-order">${String(i + 1).padStart(2, "0")}</span>
      <div class="action-summary"><div class="action-title-line"><span class="category-badge ${r.requireEnemy ? "badge-damage" : "badge-buff"}">${r.requireEnemy ? "ENEMY" : "PLAYER"}</span><b><strong>${esc(r.name)}</strong></b></div><div class="action-detail"><kbd>${keyName(r.key)}</kbd><span>${esc(ruleSummary(r))}</span></div></div>
      <div class="action-controls"><button class="icon-button" data-move="-1" aria-label="上移一位" ${i === 0 ? "disabled" : ""}>↑</button><button class="icon-button" data-move="1" aria-label="下移一位" ${i === rules.length - 1 ? "disabled" : ""}>↓</button></div>
    </div>`,
        )
        .join("")
    : '<div class="queue-empty"><div><b>动作队列为空</b><p>点击“添加技能”，设置按键与触发条件。</p></div></div>';
}
function field(key, label, value, min, max, step = 1) {
  return `<label class="field"><span>${label}</span><input data-key="${key}" type="number" min="${min}" max="${max}" step="${step}" value="${value}"></label>`;
}
function textField(key, label, value, list = "", hint = "") {
  return `<label class="field"><span>${label}</span><input data-key="${key}" value="${esc(value)}" maxlength="${key === "name" ? 80 : 160}" autocomplete="off" ${list ? 'list="' + list + '"' : ""}>${hint ? "<small>" + hint + "</small>" : ""}</label>`;
}
function switchControl(key, value, label) {
  return `<label class="simple-switch"><input data-key="${key}" type="checkbox" aria-label="${label}" ${value ? "checked" : ""}><i aria-hidden="true"></i></label>`;
}
function renderEditor() {
  cancelRecording();
  const r = draft.combat.rules.find((r) => r.id === selected);
  if (!r) {
    $("editor").innerHTML =
      '<div class="editor-empty"><div><span aria-hidden="true">◇</span><b>选择一个动作开始编辑</b><p>这里会显示技能按键、施法节奏与全部触发条件。</p><button class="button button-primary" data-action="add">＋ 添加第一个技能</button></div></div>';
    return;
  }
  const index = draft.combat.rules.indexOf(r);
  $("editor").innerHTML = `
    <header class="editor-head"><div><span class="eyebrow">ACTION ${String(index + 1).padStart(2, "0")}</span><h3 id="editor-title">${esc(r.name)}</h3></div><div class="editor-actions"><button class="mini-button" data-action="up" aria-label="上移" ${index === 0 ? "disabled" : ""}>↑</button><button class="mini-button" data-action="down" aria-label="下移" ${index === draft.combat.rules.length - 1 ? "disabled" : ""}>↓</button><button class="mini-button" data-action="copy" ${draft.combat.rules.length >= 32 ? "disabled" : ""}>复制</button><button class="mini-button" data-action="delete">删除</button></div></header>
    <div class="editor-form"><div class="form-grid">
      <div class="field"><span>启用动作 Enabled</span><div class="enabled-box"><span>启用这个动作</span>${switchControl("enabled", r.enabled, "启用动作")}</div><small>关闭后保留配置，不参与优先级检查。</small></div>
      ${textField("name", "动作名称 Name", r.name)}
      <div class="field field-wide"><span class="capture-label">施法按键 Key<button class="clear-key" data-action="clear-key">清除绑定</button></span><button type="button" class="key-capture editor-key" id="key-record" data-action="record" aria-label="录制游戏按键"><kbd>${r.key === 32 ? "Space" : keyName(r.key)}</kbd><small>点击录制</small></button><small>点击后按一下技能键。Esc 取消，仅支持单个按键。</small></div>
      <div class="editor-divider">目标与施法</div>
      ${field("cooldownMilliseconds", "重复间隔 / ms", r.cooldownMilliseconds, 300, 600000)}
      ${field("pressMilliseconds", "按住时长 / ms", r.pressMilliseconds, 30, 200)}
      ${field("pauseMilliseconds", "施法停顿 / ms", r.pauseMilliseconds, 0, 2000)}
      ${field("minimumMana", "最低魔力 %（0 = 不限制）", r.minimumMana, 0, 100)}
      <div class="editor-divider">执行条件 · 全部满足才会执行</div>
      <section class="condition-group"><div class="condition-toggle"><span><b>需要附近敌人</b><small>只计入可攻击的存活敌人。</small></span>${switchControl("requireEnemy", r.requireEnemy, "需要附近敌人")}</div>
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
        <div class="form-grid">${field("range", "检测范围 / 格", r.range, 1, 150, 0.5)}${field("minimumEnemies", "至少敌人数", r.minimumEnemies, 1, 100)}</div>
      </section>
      ${[
        ["useLifeBelow", "lifeBelow", "生命"],
        ["useEnergyShieldBelow", "energyShieldBelow", "护盾"],
        ["useManaBelow", "manaBelow", "魔力"],
      ]
        .map(
          ([use, key, name]) =>
            `<div><label class="threshold"><input type="checkbox" data-key="${use}" ${r[use] ? "checked" : ""}>${name}低于</label>${field(key, "% 未保留上限", r[key], 1, 100)}</div>`,
        )
        .join("")}
      ${textField("readySkill", "技能就绪（可选）", r.readySkill, "skill-options", "使用扫描出的完整内部名称，空白表示不检查。")}
      ${textField("requiredBuff", "存在 Buff（可选）", r.requiredBuff, "buff-options")}
      ${textField("missingBuff", "缺少 Buff（可选）", r.missingBuff, "buff-options")}
    </div><p class="editor-footer-note">Buff 名称支持不区分大小写的部分匹配；所需数据未知时不触发。同键规则共享间隔，两次输入至少相隔 300 ms。</p></div>`;
}

function cancelRecording() {
  recording = null;
  clearTimeout(captureTimer);
  $("capture-toast").hidden = true;
  const button = $("key-record");
  if (button) {
    button.classList.remove("capturing");
    const rule = draft?.combat.rules.find((r) => r.id === selected);
    button.querySelector("kbd").textContent =
      rule?.key === 32 ? "Space" : keyName(rule?.key);
    button.querySelector("small").textContent = "点击录制";
  }
  const toggle = $("toggle-record");
  if (toggle) {
    toggle.classList.remove("capturing");
    toggle.querySelector("kbd").textContent = keyName(draft?.toggleKey || 117);
    toggle.querySelector("small").textContent = "点击录制";
  }
}
function startRecording(kind = "skill") {
  cancelRecording();
  if (
    busy ||
    !draft ||
    (kind === "skill" && !draft.combat.rules.some((r) => r.id === selected))
  )
    return;
  recording = { kind, id: selected };
  $("capture-title").textContent =
    kind === "skill" ? "请按下技能键" : "请按下启停快捷键";
  $("capture-hint").textContent =
    kind === "skill"
      ? "支持字母（除 WASD）、数字和空格。Esc 取消。"
      : "支持功能键或单个非移动键，推荐 F6。Esc 取消。";
  $("capture-toast").hidden = false;
  const button = $(kind === "skill" ? "key-record" : "toggle-record");
  button.classList.add("capturing");
  button.querySelector("kbd").textContent = "…";
  button.querySelector("small").textContent = "等待按键 · Esc 取消";
  button.focus();
  captureTimer = setTimeout(cancelRecording, 12000);
}
document.addEventListener(
  "keydown",
  (e) => {
    if (!recording) return;
    if (e.key === "Tab") {
      cancelRecording();
      return;
    }
    e.preventDefault();
    e.stopImmediatePropagation();
    if (e.key === "Escape") {
      cancelRecording();
      return;
    }
    if (e.repeat) return;
    const key = /^Key[A-Z]$/.test(e.code)
      ? e.code.charCodeAt(3)
      : /^Digit[0-9]$/.test(e.code)
        ? e.code.charCodeAt(5)
        : e.code === "Space"
          ? 32
          : /^F([1-9]|1[0-9]|2[0-4])$/.test(e.key)
            ? 111 + Number(e.key.slice(1))
            : {
                Backspace: 8,
                Pause: 19,
                CapsLock: 20,
                PageUp: 33,
                PageDown: 34,
                End: 35,
                Home: 36,
                Insert: 45,
                Delete: 46,
                NumLock: 144,
                ScrollLock: 145,
              }[e.key] || 0;
    const allowed =
      recording.kind === "skill"
        ? keyOptions.includes(key) && key !== 0
        : key >= 8 &&
          key <= 254 &&
          ![
            13, 16, 17, 18, 27, 37, 38, 39, 40, 65, 68, 83, 87, 91, 92, 160,
            161, 162, 163, 164, 165,
          ].includes(key);
    if (
      e.isComposing ||
      e.ctrlKey ||
      e.altKey ||
      e.metaKey ||
      e.shiftKey ||
      !allowed
    ) {
      $("capture-title").textContent = "此按键暂不支持，请重新按键";
      return;
    }
    if (recording.kind === "toggle") draft.toggleKey = key;
    else {
      const rule = draft.combat.rules.find((r) => r.id === recording.id);
      if (rule) rule.key = key;
    }
    cancelRecording();
    renderRules();
    controls();
  },
  true,
);
document.addEventListener("pointerdown", (e) => {
  if (
    recording &&
    !e.target.closest("#key-record,#toggle-record,#capture-toast")
  )
    cancelRecording();
});
$("cancel-capture").addEventListener("click", cancelRecording);
$("toggle-record").addEventListener("click", () => startRecording("toggle"));
window.addEventListener("blur", cancelRecording);
document.addEventListener("visibilitychange", () => {
  if (document.hidden) cancelRecording();
});

// Keep native datalist nodes stable across status polls. Updating a focused
// input's options can dismiss Chrome's currently open suggestion popup.
function updateOptions(id, items) {
  const values = JSON.stringify(items);
  if ($(id).dataset.values === values) {
    pendingOptions.delete(id);
    return;
  }
  if (document.activeElement?.getAttribute("list") === id) {
    pendingOptions.set(id, items);
    return;
  }
  $(id).innerHTML = items
    .map((s) => `<option value="${esc(s)}"></option>`)
    .join("");
  $(id).dataset.values = values;
  pendingOptions.delete(id);
}
document.addEventListener("focusout", () => {
  setTimeout(() => {
    for (const [id, items] of pendingOptions) updateOptions(id, items);
  }, 0);
});

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
    const move = e.target.closest("[data-move]");
    if (move) {
      const rules = draft.combat.rules,
        from = rules.findIndex((r) => r.id === button.dataset.rule),
        to = from + Number(move.dataset.move);
      if (from >= 0 && to >= 0 && to < rules.length)
        [rules[from], rules[to]] = [rules[to], rules[from]];
    }
    selected = button.dataset.rule;
    renderRules();
    renderEditor();
    controls();
  }
});
$("rules").addEventListener("keydown", (e) => {
  if ((e.key === "Enter" || e.key === " ") && e.target.matches("[data-rule]")) {
    e.preventDefault();
    e.target.click();
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
  if (element.dataset.key === "name") $("editor-title").textContent = r.name;
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
  if (action === "record") {
    startRecording();
    return;
  }
  const rules = draft.combat.rules,
    index = rules.findIndex((r) => r.id === selected);
  if (index < 0) return;
  if (action === "clear-key") rules[index].key = 0;
  else if (action === "delete") {
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
function selectTab(tab) {
  const button = document.querySelector(`[data-tab="${tab}"]`);
  cancelRecording();
  document
    .querySelectorAll(".tab-panel")
    .forEach((panel) => (panel.hidden = panel.id !== "tab-" + tab));
  document.querySelectorAll("[data-tab]").forEach((item) => {
    item.classList.toggle("active", item === button);
    if (item === button) item.setAttribute("aria-current", "page");
    else item.removeAttribute("aria-current");
  });
  $("page-title").textContent = {
    combat: "技能优先级",
    monitor: "实时监测",
    config: "配置文件",
    general: "General",
    follow: "Follow",
  }[tab];
  $("page-subtitle").textContent = {
    combat: "从上到下安排动作，并设置技能按键与触发条件。",
    monitor: "查看人物、附近敌人和最近的战斗状态。",
    config: "管理本机配置与备份。",
    general: "选择运行模式，配置通用行为与启停。",
    follow: "配置队长、双人协调与导航。",
  }[tab];
}
document
  .querySelectorAll("[data-tab]")
  .forEach((button) =>
    button.addEventListener("click", () => selectTab(button.dataset.tab)),
  );
$("mode").addEventListener("change", (e) => {
  draft.mode = e.target.value;
  controls();
  if (draft.mode === "Follow") selectTab("follow");
});
document.querySelectorAll("[data-follow]").forEach((element) =>
  element.addEventListener("input", () => {
    draft.follow[element.dataset.follow] =
      element.type === "checkbox"
        ? element.checked
        : element.type === "number"
          ? Number(element.value)
          : element.value;
    $("follow-coop-fields").hidden = !draft.follow.localCoopFollow;
    controls();
  }),
);
for (const [id, key] of [
  ["config-name", "name"],
  ["preview", "preview"],
  ["monitor-character", "monitorCharacter"],
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
  if (!draft.mode) {
    selectTab("general");
    message("请先选择运行模式。");
    return;
  }
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
    // Retired switch in older exports; controller mode now handles this automatically.
    delete imported.allowControllerWithoutChat;
    // Strict server validation remains authoritative. Reject malformed drafts before rendering controls.
    if (imported.schemaVersion === 1) {
      imported.schemaVersion = 2;
      imported.mode = "";
      imported.toggleKey = 117;
      imported.follow = {
        leaderName: "",
        localCoopFollow: false,
        p1Name: "",
        p2Name: "",
        p2LagDistance: 35,
        p2RejoinDistance: 12,
        stopDistance: 18,
        resumeDistance: 25,
        clearance: 1,
        repathMilliseconds: 350,
        stuckMilliseconds: 2500,
        showStatus: true,
        showRoute: true,
      };
    }
    if (
      imported.schemaVersion !== 2 ||
      !["", "Follow"].includes(imported.mode) ||
      !Number.isInteger(imported.toggleKey) ||
      !imported.follow ||
      typeof imported.name !== "string" ||
      typeof imported.preview !== "boolean" ||
      typeof imported.monitorCharacter !== "string" ||
      typeof imported.combat?.enabled !== "boolean" ||
      !Array.isArray(imported.combat.rules) ||
      imported.combat.rules.length > 32
    )
      throw new Error("不是有效的 Bloodybot2 配置。");
    const sample = newRule();
    const followSample = {
      leaderName: "",
      localCoopFollow: false,
      p1Name: "",
      p2Name: "",
      p2LagDistance: 35,
      p2RejoinDistance: 12,
      stopDistance: 18,
      resumeDistance: 25,
      clearance: 1,
      repathMilliseconds: 350,
      stuckMilliseconds: 2500,
      showStatus: true,
      showRoute: true,
    };
    if (
      Object.keys(followSample).some(
        (key) => typeof imported.follow[key] !== typeof followSample[key],
      )
    )
      throw new Error("Follow 配置字段不完整。");
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
    if (!draft.mode) selectTab("general");
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
    updateOptions(id, items);
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
    $("connection-status").className = "connection online";
  } catch {
    connected = false;
    $("connection").textContent = "连接已断开";
    $("connection-dot").className = "dot error";
    $("connection-status").className = "connection offline";
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
