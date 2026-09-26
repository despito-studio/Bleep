import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { interfaceMarkup } from '../src/interface.js';
import { CHARACTER_CLASSES, DEFAULT_CLASS } from '../shared/classes.js';

test('interfaceMarkup generates all primary navigation, lobby, and HUD controls', () => {
  const icon = (name) => `<svg data-icon="${name}"></svg>`;
  const markup = interfaceMarkup(CHARACTER_CLASSES, DEFAULT_CLASS, icon);

  // Root containers
  assert.ok(markup.includes('id="viewport"'), 'viewport canvas must exist');
  assert.ok(markup.includes('id="menu"'), 'menu section must exist');
  assert.ok(markup.includes('id="lobby-screen"'), 'lobby-screen section must exist');
  assert.ok(markup.includes('id="hud"'), 'hud section must exist');
  assert.ok(markup.includes('id="overlay"'), 'overlay must exist');

  // Menu action buttons
  assert.ok(markup.includes('id="solo-button"'), 'solo-button must exist');
  assert.ok(markup.includes('id="coop-button"'), 'coop-button must exist');
  assert.ok(markup.includes('id="settings-button"'), 'settings-button must exist');
  assert.ok(markup.includes('id="dossier-button"'), 'dossier-button must exist');
  assert.ok(markup.includes('id="sound-button"'), 'sound-button must exist');
  assert.ok(markup.includes('id="fullscreen-button"'), 'fullscreen-button must exist');
  assert.ok(markup.includes('id="creative-mode-toggle"'), 'creative-mode-toggle must exist');

  // Lobby action buttons & picker
  assert.ok(markup.includes('id="lobby-room-code"'), 'lobby-room-code must exist');
  assert.ok(markup.includes('id="btn-copy-code"'), 'btn-copy-code must exist');
  assert.ok(markup.includes('id="lobby-start-btn"'), 'lobby-start-btn must exist');
  assert.ok(markup.includes('id="lobby-leave-btn"'), 'lobby-leave-btn must exist');
  assert.ok(markup.includes('id="lobby-picker-cards"'), 'lobby-picker-cards must exist');

  // HUD controls & overlays
  assert.ok(markup.includes('id="pause-button"'), 'pause-button must exist');
  assert.ok(markup.includes('id="skip-arrival"'), 'skip-arrival button must exist');
  assert.ok(markup.includes('id="radio-menu"'), 'radio-menu must exist');

  // Character classes present in both menu and lobby
  for (const clsKey of Object.keys(CHARACTER_CLASSES)) {
    assert.ok(markup.includes(`data-class="${clsKey}"`), `character class ${clsKey} must be selectable`);
  }
});

test('main.js contains closePanel definition and proper modal dismiss handlers', () => {
  const mainJs = fs.readFileSync(path.resolve('src/main.js'), 'utf8');

  // closePanel must be defined
  assert.ok(/function\s+closePanel\s*\(/.test(mainJs), 'function closePanel() must be defined');

  // close-panel click listener attached in panel()
  assert.ok(mainJs.includes("$('close-panel').onclick=closePanel"), 'close-panel button must call closePanel');

  // Overlay backdrop dismiss
  assert.ok(mainJs.includes("$('overlay').addEventListener('click'"), 'backdrop click listener must exist');

  // Coop cancel button
  assert.ok(mainJs.includes('cancel-coop-btn'), 'cancel-coop-btn must be present in showCoop');
  assert.ok(mainJs.includes("$('cancel-coop-btn').onclick=closePanel"), 'cancel-coop-btn must call closePanel');

  // Settings close button
  assert.ok(mainJs.includes('close-settings-btn'), 'close-settings-btn must be present in showSettings');
  assert.ok(mainJs.includes("$('close-settings-btn').onclick=closePanel"), 'close-settings-btn must call closePanel');

  // Dossier close button
  assert.ok(mainJs.includes('close-dossier-btn'), 'close-dossier-btn must be present in showDossier');
  assert.ok(mainJs.includes("$('close-dossier-btn').onclick=closePanel"), 'close-dossier-btn must call closePanel');

  // Co-op transition to lobby
  assert.ok(mainJs.includes('showLobby()'), 'showLobby must be invoked on room join/create');

  // Escape key closes panels and menus
  assert.ok(mainJs.includes('closePanel()'), 'Escape key must invoke closePanel when not in lobby/ending');
});

test('style.css ensures overlay z-index is elevated above HUD and lobby screens', () => {
  const styleCss = fs.readFileSync(path.resolve('src/style.css'), 'utf8');
  assert.match(styleCss, /\.overlay\s*\{[^}]*z-index:\s*70/);
});
