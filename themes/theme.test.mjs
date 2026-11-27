import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const prefix = 'codex-theme-v1:';
const presets = [
  { name: 'neo-light', variant: 'light' },
  { name: 'evo-dark', variant: 'dark' },
];

function luminance(hex) {
  const [r, g, b] = hex.slice(1).match(/../g).map(pair => {
    const value = Number.parseInt(pair, 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrastRatio(a, b) {
  const left = luminance(a);
  const right = luminance(b);
  return (Math.max(left, right) + 0.05) / (Math.min(left, right) + 0.05);
}

for (const { name, variant } of presets) {
  test(`${name} matches its source and desktop import slot`, async () => {
    const [source, share] = await Promise.all(
      ['json', 'txt'].map(extension => readFile(new URL(`./${name}.${extension}`, import.meta.url), 'utf8')),
    );
    const preset = JSON.parse(source);
    assert.equal(share.trim(), prefix + JSON.stringify(preset));
    assert.deepEqual(JSON.parse(share.trim().slice(prefix.length)), preset);
    assert.equal(preset.variant, variant);
    assert.equal(preset.codeThemeId, 'codex');
    const { theme } = preset;
    const slot = variant === 'light' ? 'neo' : 'evo';
    const scene = JSON.parse(await readFile(new URL(`../scenes/${slot}.json`, import.meta.url), 'utf8'));
    assert.equal(scene.name, `Vedal AI · ${slot === 'neo' ? '月之亮面' : '月之暗面'}`);
    assert.equal(theme.accent, scene.colors.accent.toLowerCase());
    assert.equal(theme.ink, scene.colors.text.toLowerCase());
    assert.ok(contrastRatio(scene.colors.muted, scene.colors.panel) >= 4.5);
    // The 26.1120 composer paints its idle waveform with the scene's muted ink.
    assert.ok(contrastRatio(scene.colors.muted, scene.colors.accent) >= 3, 'visible voice waveform');
    assert.ok(contrastRatio(scene.colors.accent, variant === 'light' ? '#ffffff' : '#000000') >= 4.5);
    assert.equal(theme.accentSource, 'custom');
    assert.deepEqual(theme.fonts, { code: null, ui: null });
    assert.equal(theme.opaqueWindows, true);
    assert.ok(Number.isInteger(theme.contrast) && theme.contrast >= 0 && theme.contrast <= 100);
    for (const color of [theme.surface, theme.ink, theme.accent, theme.semanticColors.diffAdded,
      theme.semanticColors.diffRemoved, theme.semanticColors.skill]) {
      assert.match(color, /^#[\da-f]{6}$/i);
    }
    for (const color of [theme.ink, theme.accent, ...Object.values(theme.semanticColors)]) {
      assert.ok(contrastRatio(color, theme.surface) >= 4.5, `${color} on ${theme.surface}`);
    }
  });
}
