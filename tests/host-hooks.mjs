import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';

const installRoot = path.resolve(process.argv[2] ?? 'out/app');
const scratch = mkdtempSync(path.join(path.resolve(process.argv[3] ?? tmpdir()), 'twinshift-host-'));
const profile = path.join(scratch, 'codex');
mkdirSync(profile);
const env = { ...process.env, CODEX_HOME: profile, TWINSHIFT_DATA_DIR: path.join(scratch, 'app-data') };
const cli = process.env.TWINSHIFT_CODEX_EXE ?? 'codex.exe';
const id = 'twin-shift-events@twin-shift-local';
function run(...args) {
  return execFileSync(cli, args, { cwd: scratch, env, encoding: 'utf8', windowsHide: true, timeout: 30000 });
}
run('plugin', 'marketplace', 'add', path.join(installRoot, 'marketplace'));
try {
  run('plugin', 'add', id);
  const proc = spawn(cli, ['app-server', '--stdio'], { cwd: scratch, env, stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true });
  const waiting = new Map();
  const lines = createInterface({ input: proc.stdout });
  lines.on('line', line => {
    const msg = JSON.parse(line);
    if (waiting.has(msg.id)) waiting.get(msg.id)(msg);
  });
  function send(obj) { proc.stdin.write(JSON.stringify(obj) + '\n'); }
  function request(id, method, params) {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(method + ' timed out')), 20000);
      waiting.set(id, msg => {
        clearTimeout(timeout);
        waiting.delete(id);
        if (msg.error) reject(new Error(JSON.stringify(msg.error)));
        else resolve(msg.result);
      });
      send({ id, method, params });
    });
  }
  try {
    await request(1, 'initialize', { clientInfo: { name: 'twinshift_checks', title: 'Twin Shift checks', version: '1.1.0-beta.2' }, capabilities: { experimentalApi: true } });
    send({ method: 'initialized' });
    const result = await request(2, 'hooks/list', { cwds: [scratch] });
    const entries = result.data.flatMap(row => {
      assert.deepEqual(row.errors, []);
      return row.hooks.filter(hook => hook.pluginId === id);
    });
    assert.deepEqual(entries.map(h => h.eventName).sort(), ['interrupt', 'permissionRequest', 'postToolUse', 'preToolUse', 'stop']);
    console.log('Codex discovered', entries.map(h => h.eventName + '=' + h.trustStatus).join(', '));
  } finally {
    lines.close();
    proc.stdin.end();
    proc.kill();
  }
} finally {
  try { run('plugin', 'remove', id); } finally { run('plugin', 'marketplace', 'remove', 'twin-shift-local'); }
}
