// Chrome only, isolated profile, real embedded Web UI + fake runtime server.
// Usage: node browser-smoke.mjs http://127.0.0.1:PORT/ [chrome.exe]
import { spawn } from "node:child_process";
import { mkdir, readFile, writeFile, readdir } from "node:fs/promises";
import path from "node:path";
import assert from "node:assert/strict";

const url = process.argv[2];
if (!/^http:\/\/127\.0\.0\.1:\d+\/$/.test(url || ""))
  throw new Error("Pass the UI_TEST_URL from the fake test host.");
const chromePath =
  process.argv[3] ||
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const output = path.resolve("artifacts/bloodybot2", "ui-" + Date.now()),
  profile = path.join(output, "chrome");
await mkdir(profile, { recursive: true });
const chrome = spawn(
  chromePath,
  [
    "--headless=new",
    "--disable-gpu",
    "--disable-background-networking",
    "--no-first-run",
    "--no-default-browser-check",
    "--remote-debugging-port=0",
    "--user-data-dir=" + profile,
    "about:blank",
  ],
  { windowsHide: true, stdio: "ignore" },
);
let socket;
let checks = 0;
let sequence = 0;
const pending = new Map(),
  errors = [];
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const check = (condition, label) => {
  assert.ok(condition, label);
  console.log("PASS " + label);
  checks++;
};
async function waitFor(action, description) {
  for (let i = 0; i < 60; i++) {
    try {
      if (await action()) return;
    } catch {}
    await sleep(100);
  }
  throw new Error("Timeout: " + description);
}
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error("CDP timeout: " + method));
    }, 10000);
    pending.set(id, {
      resolve: (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      reject,
    });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const result = await send("Runtime.evaluate", {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (result.exceptionDetails)
    throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
const click = (selector) =>
  evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
async function input(selector, value) {
  await evaluate(
    `(()=>{const el=document.querySelector(${JSON.stringify(selector)});el.value=${JSON.stringify(value)};el.dispatchEvent(new Event('input',{bubbles:true}));})()`,
  );
}
const bootstrap = async () => await (await fetch(url + "api/bootstrap")).json();
async function key(key, code, windowsVirtualKeyCode, modifiers = 0) {
  await send("Input.dispatchKeyEvent", {
    type: "rawKeyDown",
    key,
    code,
    windowsVirtualKeyCode,
    modifiers,
  });
  await send("Input.dispatchKeyEvent", {
    type: "keyUp",
    key,
    code,
    windowsVirtualKeyCode,
    modifiers,
  });
}
async function record(keyName, code, vk, selector = "#key-record") {
  await click(selector);
  await key(keyName, code, vk);
}
async function save() {
  await click("#save");
  await waitFor(
    () => evaluate(`document.getElementById('dirty').textContent==='已保存'`),
    "save",
  );
}
try {
  let debugPort;
  await waitFor(async () => {
    const data = await readFile(
      path.join(profile, "DevToolsActivePort"),
      "utf8",
    );
    debugPort = Number(data.split("\n")[0]);
    return !!debugPort;
  }, "Chrome DevTools");
  const target = await (
    await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`, {
      method: "PUT",
    })
  ).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.onopen = resolve;
    socket.onerror = reject;
  });
  socket.onmessage = (event) => {
    const value = JSON.parse(event.data);
    if (value.id) {
      const item = pending.get(value.id);
      pending.delete(value.id);
      if (item)
        value.error ? item.reject(value.error) : item.resolve(value.result);
    } else if (value.method === "Runtime.exceptionThrown")
      errors.push(value.params.exceptionDetails);
    else if (
      value.method === "Log.entryAdded" &&
      value.params.entry.source === "security"
    )
      errors.push(value.params.entry);
  };
  await send("Runtime.enable");
  await send("Log.enable");
  await send("Page.enable");
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1440,
    height: 1100,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await send("Page.navigate", { url });
  await waitFor(
    () =>
      evaluate(
        `document.querySelectorAll('.rule').length===1 && !document.getElementById('workspace').inert`,
      ),
    "initial UI",
  );
  check(
    await evaluate(`document.title.includes('Bloodybot2')`),
    "Chrome renders embedded UI",
  );
  check(
    await evaluate(
      `document.getElementById('mode-chip').textContent==='预览模式'`,
    ),
    "preview default visible",
  );
  await click('[data-tab="general"]');
  check(
    await evaluate(
      `document.querySelector('#tab-general #preview')!==null && document.querySelector('#tab-general #mode')!==null`,
    ),
    "common settings live in General",
  );
  await evaluate(
    `document.getElementById('mode').value='';document.getElementById('mode').dispatchEvent(new Event('change',{bubbles:true}))`,
  );
  check(
    await evaluate(
      `document.getElementById('run').disabled && document.getElementById('save').disabled && document.getElementById('follow-nav').hidden`,
    ),
    "mode must be selected before save or start",
  );
  await evaluate(
    `document.getElementById('mode').value='Follow';document.getElementById('mode').dispatchEvent(new Event('change',{bubbles:true}))`,
  );
  check(
    await evaluate(
      `!document.getElementById('tab-follow').hidden && !document.getElementById('follow-nav').hidden`,
    ),
    "Follow selection opens mode-specific settings",
  );
  await input('[data-follow="leaderName"]', "Chrome 队长");
  await click('[data-follow="localCoopFollow"]');
  check(
    await evaluate(`!document.getElementById('follow-coop-fields').hidden`),
    "co-op exposes P1/P2 fields",
  );
  await click('[data-follow="localCoopFollow"]');
  await click('[data-tab="general"]');
  await record("F7", "F7", 118, "#toggle-record");
  check(
    await evaluate(
      `document.querySelector('#toggle-record kbd').textContent==='F7'`,
    ),
    "General hotkey uses recording",
  );
  await save();
  check(
    (await bootstrap()).config.follow.leaderName === "Chrome 队长" &&
      (await bootstrap()).config.toggleKey === 118,
    "Follow and General fields persist",
  );
  await waitFor(
    () =>
      evaluate(
        `document.querySelectorAll('#player-options option').length===2`,
      ),
    "player suggestions",
  );
  await evaluate(
    `window.originalOption=document.querySelector('#player-options option')`,
  );
  await sleep(1400);
  check(
    await evaluate(
      `window.originalOption===document.querySelector('#player-options option')`,
    ),
    "unchanged polling preserves datalist option nodes",
  );
  check(
    await evaluate(
      `(()=>{document.getElementById('monitor-character').focus(); updateOptions('player-options',['changed']); return window.originalOption===document.querySelector('#player-options option');})()`,
    ),
    "changed suggestions are deferred while the input is focused",
  );
  await evaluate(`document.getElementById('monitor-character').blur()`);
  await waitFor(
    () =>
      evaluate(
        `document.querySelector('#player-options option').value==='changed'`,
      ),
    "deferred suggestions",
  );
  check(true, "deferred suggestions apply after focus leaves");
  await click('[data-tab="combat"]');
  await input('[data-key="name"]', "Chrome 精英技能");
  check(
    await evaluate(`document.querySelector('select[data-key="key"]')===null`),
    "skill key dropdown replaced by recording",
  );
  await record("w", "KeyW", 87);
  check(
    await evaluate(
      `!document.getElementById('capture-toast').hidden && document.getElementById('capture-title').textContent.includes('不支持')`,
    ),
    "movement key is rejected while recording continues",
  );
  await key("Escape", "Escape", 27);
  check(
    await evaluate(
      `document.getElementById('capture-toast').hidden && document.querySelector('#key-record kbd').textContent==='Q'`,
    ),
    "Escape cancels without changing the binding",
  );
  await click("#key-record");
  const beforeCapture = (await bootstrap()).revision;
  await key("s", "KeyS", 83, 2);
  check(
    (await bootstrap()).revision === beforeCapture,
    "Ctrl+S during recording cannot save the draft",
  );
  await key("e", "KeyE", 69);
  check(
    await evaluate(
      `document.querySelector('#key-record kbd').textContent==='E' && document.getElementById('capture-toast').hidden`,
    ),
    "Chrome keyboard event records skill key",
  );
  await click("#key-record");
  await evaluate(`window.dispatchEvent(new Event('blur'))`);
  check(
    await evaluate(
      `document.getElementById('capture-toast').hidden && document.querySelector('#key-record kbd').textContent==='E'`,
    ),
    "focus loss cancels recording and keeps the binding",
  );
  check(
    await evaluate(`document.getElementById('run').disabled`),
    "unsaved edits prevent starting wrong config",
  );
  await sleep(800);
  check(
    await evaluate(
      `document.querySelector('[data-key="name"]').value==='Chrome 精英技能'`,
    ),
    "status polling preserves draft",
  );
  await save();
  check(
    (await bootstrap()).config.combat.rules[0].key === 69,
    "recorded key persists through real API",
  );
  await click('[data-template="life"]');
  await record("r", "KeyR", 82);
  await click('[data-action="up"]');
  check(
    await evaluate(
      `document.querySelector('.rule strong').textContent==='低生命技能'`,
    ),
    "priority reorder works",
  );
  await click('[data-action="copy"]');
  check(
    await evaluate(`document.querySelectorAll('.rule').length===3`),
    "duplicate creates a rule",
  );
  await click('[data-action="delete"]');
  check(
    await evaluate(`document.querySelectorAll('.rule').length===2`),
    "delete removes selected rule",
  );
  await save();
  await click("#run");
  await waitFor(
    () =>
      evaluate(
        `document.getElementById('run-label').textContent==='预览运行中'`,
      ),
    "preview start",
  );
  await click('[data-tab="monitor"]');
  await waitFor(
    () =>
      evaluate(
        `document.getElementById('candidate').textContent.includes('Chrome 精英技能')`,
      ),
    "preview rule evaluated",
  );
  check(
    await evaluate(
      `document.getElementById('accepted').textContent==='0 次输入'`,
    ),
    "preview emits no fake input",
  );
  check(
    await evaluate(
      `document.getElementById('rare-count').textContent==='1' && document.getElementById('unique-count').textContent==='1'`,
    ),
    "enemy rarity counters rendered",
  );
  check(
    await evaluate(
      `document.querySelector('#skills .ready').textContent==='test_skill'`,
    ),
    "skill readiness scanner rendered",
  );
  await writeFile(
    path.join(output, "monitor.png"),
    Buffer.from(
      (
        await send("Page.captureScreenshot", {
          format: "png",
          captureBeyondViewport: true,
        })
      ).data,
      "base64",
    ),
  );
  await click("#stop");
  await click('[data-tab="general"]');
  await click("#preview");
  await save();
  await click("#run");
  await waitFor(
    () =>
      evaluate(
        `!document.getElementById('accepted').textContent.startsWith('0 ')`,
      ),
    "fake accepted input",
  );
  check(true, "live mode reaches fake input only");
  await click("#stop");
  await click("#preview");
  await save();
  const stale = await bootstrap();
  await input("#config-name", "浏览器草稿");
  const external = structuredClone(stale.config);
  external.name = "另一页面";
  const result = await fetch(url + "api/config", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      "X-Bloodybot2-Token": stale.token,
    },
    body: JSON.stringify({ revision: stale.revision, config: external }),
  });
  assert.equal(result.status, 200);
  await click("#save");
  await waitFor(
    () =>
      evaluate(
        `document.getElementById('message').textContent.includes('另一个页面')`,
      ),
    "revision conflict",
  );
  check(
    await evaluate(
      `document.getElementById('config-name').value==='浏览器草稿'`,
    ),
    "conflict retains unsaved draft",
  );
  // Reload without dismissing a user dialog: preserve draft via export first.
  await click('[data-tab="config"]');
  await send("Browser.setDownloadBehavior", {
    behavior: "allow",
    downloadPath: output,
  });
  await click("#export");
  await waitFor(
    async () => (await readdir(output)).includes("bloodybot2-config.json"),
    "export",
  );
  check(
    JSON.parse(
      await readFile(path.join(output, "bloodybot2-config.json"), "utf8"),
    ).name === "浏览器草稿",
    "export contains current draft",
  );
  await evaluate(`window.confirm=()=>true`);
  await click("#reload");
  await waitFor(
    () => evaluate(`document.getElementById('config-name').value==='另一页面'`),
    "reload",
  );
  check(true, "reload resolves concurrent revision");
  const imported = structuredClone(external);
  imported.name = "导入测试";
  imported.combat.rules[0].name = "<img src=x onerror=alert(1)>";
  const importPath = path.join(output, "import-test.json");
  await writeFile(importPath, JSON.stringify(imported));
  await send("DOM.enable");
  const dom = await send("DOM.getDocument");
  const fileNode = await send("DOM.querySelector", {
    nodeId: dom.root.nodeId,
    selector: "#import-file",
  });
  await send("DOM.setFileInputFiles", {
    nodeId: fileNode.nodeId,
    files: [importPath],
  });
  await waitFor(
    () => evaluate(`document.getElementById('config-name').value==='导入测试'`),
    "import",
  );
  await click('[data-tab="combat"]');
  check(
    await evaluate(
      `document.querySelector('.rule strong').textContent==='<img src=x onerror=alert(1)>' && document.querySelectorAll('#rules img').length===0`,
    ),
    "imported labels rendered as text",
  );
  await input('[data-key="name"]', "低生命防护");
  await save();
  await click(".rule:nth-child(2)");
  await writeFile(
    path.join(output, "combat-desktop.png"),
    Buffer.from(
      (
        await send("Page.captureScreenshot", {
          format: "png",
          captureBeyondViewport: true,
        })
      ).data,
      "base64",
    ),
  );
  check(
    await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),
    "desktop has no horizontal overflow",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 1100,
    height: 900,
    deviceScaleFactor: 1,
    mobile: false,
  });
  check(
    await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),
    "medium desktop has no horizontal overflow",
  );
  await send("Emulation.setDeviceMetricsOverride", {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true,
  });
  await writeFile(
    path.join(output, "combat-mobile.png"),
    Buffer.from(
      (
        await send("Page.captureScreenshot", {
          format: "png",
          captureBeyondViewport: true,
        })
      ).data,
      "base64",
    ),
  );
  check(
    await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),
    "mobile has no horizontal overflow",
  );
  await click('[data-tab="general"]');
  check(
    await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),
    "General fits narrow screens",
  );
  await click('[data-tab="follow"]');
  check(
    await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),
    "Follow fits narrow screens",
  );
  check(errors.length === 0, "no JavaScript or CSP errors");
  console.log(`All ${checks} Chrome UI checks passed. Screenshots: ${output}`);
} finally {
  if (socket?.readyState === WebSocket.OPEN) {
    try {
      await send("Browser.close");
    } catch {}
    socket.close();
  }
  chrome.kill();
}
