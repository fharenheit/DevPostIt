import { applyTypography, defaultEditorPreferences, defaultFontSettings, editorTextSize, isSafeFontName, maxEditorSize, maxLineHeight, maxTextSize, minEditorSize, minLineHeight, minTextSize, previewTextSize, readFontSettings, saveFontSettings, supportsLigatures, type EditorPreferences, type FontKey } from './fonts';
import './style.css';

// 설정 창 전용 페이지입니다. 저장하면 같은 프로필의 열린 메모가 storage 이벤트로 반영합니다.
const fontSelect = document.querySelector<HTMLSelectElement>('#font')!;
const bodySelect = document.querySelector<HTMLSelectElement>('#body-font')!;
const ligatures = document.querySelector<HTMLInputElement>('#ligatures')!;
const button = (id: string) => document.querySelector<HTMLButtonElement>(id)!;
const editorOptions = Object.keys(defaultEditorPreferences) as (keyof EditorPreferences)[];
const editorOption = (name: keyof EditorPreferences) => document.querySelector<HTMLInputElement>(`#opt-${name}`)!;
let settings = readFontSettings();

function addOption(select: HTMLSelectElement, value: string, label: string): void {
  if (Array.from(select.options).some(option => option.value === value)) return;
  select.append(new Option(label, value));
}
function show(): void {
  settings = readFontSettings();
  if (settings.font.startsWith('custom:')) addOption(fontSelect, settings.font, settings.font.slice('custom:'.length));
  if (settings.bodyFont) addOption(bodySelect, settings.bodyFont, settings.bodyFont);
  fontSelect.value = settings.font;
  bodySelect.value = settings.bodyFont;
  ligatures.checked = settings.ligatures;
  for (const name of editorOptions) editorOption(name).checked = settings.editor[name];
  render();
}
function render(): void {
  const editorSize = editorTextSize(settings);
  document.querySelector('#text-size')!.textContent = String(settings.size);
  document.querySelector('#editor-size')!.textContent = String(editorSize);
  const previewSize = previewTextSize(settings);
  document.querySelector('#preview-size')!.textContent = String(previewSize);
  button('#preview-smaller').disabled = previewSize <= minEditorSize;
  button('#preview-larger').disabled = previewSize >= maxEditorSize;
  document.querySelector('#line-height')!.textContent = settings.lineHeight.toFixed(1);
  button('#smaller').disabled = settings.size <= minTextSize;
  button('#larger').disabled = settings.size >= maxTextSize;
  button('#editor-smaller').disabled = editorSize <= minEditorSize;
  button('#editor-larger').disabled = editorSize >= maxEditorSize;
  button('#line-smaller').disabled = settings.lineHeight <= minLineHeight;
  button('#line-larger').disabled = settings.lineHeight >= maxLineHeight;
  ligatures.disabled = !supportsLigatures(settings.font);
  ligatures.title = ligatures.disabled ? 'Consolas에는 합자가 없습니다.' : '';
  applyTypography(settings);
}
function save(): void {
  saveFontSettings(settings);
  render();
}
function readControls(): void {
  settings = {
    ...settings,
    font: fontSelect.value as FontKey,
    bodyFont: bodySelect.value,
    ligatures: ligatures.checked,
    editor: Object.fromEntries(editorOptions.map(name => [name, editorOption(name).checked])) as EditorPreferences,
  };
  save();
}
const stepLineHeight = (delta: number) => Math.round(Math.min(maxLineHeight, Math.max(minLineHeight, settings.lineHeight + delta)) * 10) / 10;
fontSelect.addEventListener('change', readControls);
bodySelect.addEventListener('change', readControls);
ligatures.addEventListener('change', readControls);
for (const name of editorOptions) editorOption(name).addEventListener('change', readControls);
button('#smaller').addEventListener('click', () => { settings = { ...settings, size: Math.max(minTextSize, settings.size - 1) }; save(); });
button('#larger').addEventListener('click', () => { settings = { ...settings, size: Math.min(maxTextSize, settings.size + 1) }; save(); });
button('#editor-smaller').addEventListener('click', () => { settings = { ...settings, editorSize: Math.max(minEditorSize, editorTextSize(settings) - 1) }; save(); });
button('#editor-larger').addEventListener('click', () => { settings = { ...settings, editorSize: Math.min(maxEditorSize, editorTextSize(settings) + 1) }; save(); });
button('#preview-smaller').addEventListener('click', () => { settings = { ...settings, previewSize: Math.max(minEditorSize, previewTextSize(settings) - 1) }; save(); });
button('#preview-larger').addEventListener('click', () => { settings = { ...settings, previewSize: Math.min(maxEditorSize, previewTextSize(settings) + 1) }; save(); });
button('#line-smaller').addEventListener('click', () => { settings = { ...settings, lineHeight: stepLineHeight(-0.1) }; save(); });
button('#line-larger').addEventListener('click', () => { settings = { ...settings, lineHeight: stepLineHeight(0.1) }; save(); });
button('#font-reset').addEventListener('click', () => { saveFontSettings(defaultFontSettings()); show(); });
window.addEventListener('storage', show);
show();

// 경로는 보내지 않습니다. 앱이 자신의 데이터 폴더만 다룹니다. 브라우저 단독 실행에서는 표시하지 않습니다.
type Host = { postMessage(message: unknown): void; addEventListener(type: 'message', handler: (event: { data: unknown }) => void): void };
const host = window.chrome?.webview as unknown as Host | undefined;
if (host) {
  const path = document.querySelector<HTMLElement>('#data-folder-path')!;
  const change = document.querySelector<HTMLButtonElement>('#change-data-folder')!;
  const startInTray = document.querySelector<HTMLInputElement>('#start-in-tray')!;
  document.querySelector<HTMLElement>('#data-folder-row')!.hidden = false;
  document.querySelector<HTMLElement>('#start-in-tray-row')!.hidden = false;
  document.querySelector('#open-data-folder')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.openDataFolder' }));
  change.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.changeDataFolder' }));
  const autoUpdate = document.querySelector<HTMLInputElement>('#auto-update')!;
  const status = document.querySelector<HTMLElement>('#settings-status')!;
  const captureDelay = document.querySelector<HTMLSelectElement>('#capture-delay')!;
  captureDelay.addEventListener('change', () => host.postMessage({ protocolVersion: 1, type: 'settings.captureDelay', value: Number(captureDelay.value) }));
  for (const row of ['#backup-row', '#update-row', '#capture-delay-row']) document.querySelector<HTMLElement>(row)!.hidden = false;
  document.querySelector('#backup')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.backup' }));
  document.querySelector('#restore')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.restore' }));
  document.querySelector('#check-update')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.checkUpdate' }));
  autoUpdate.addEventListener('change', () => host.postMessage({ protocolVersion: 1, type: 'settings.autoUpdate', value: autoUpdate.checked }));
  startInTray.addEventListener('change', () => host.postMessage({ protocolVersion: 1, type: 'settings.startInTray', value: startInTray.checked }));
  document.querySelector<HTMLElement>('#body-font-row')!.hidden = false;
  host.addEventListener('message', event => {
    const fonts = event.data as { type?: unknown; monospace?: unknown; all?: unknown } | null;
    if (fonts?.type === 'settings.fonts') {
      const names = (value: unknown) => (Array.isArray(value) ? value : []).filter(isSafeFontName);
      for (const name of names(fonts.monospace)) addOption(fontSelect, `custom:${name}`, name);
      for (const name of names(fonts.all)) addOption(bodySelect, name, name);
      fontSelect.value = settings.font;
      bodySelect.value = settings.bodyFont;
      return;
    }
    const data = event.data as { type?: unknown; message?: unknown; dataFolder?: unknown; pendingFolder?: unknown; startInTray?: unknown; canChangeFolder?: unknown; autoUpdate?: unknown; canUpdate?: unknown; version?: unknown; captureDelay?: unknown } | null;
    if (data?.type === 'settings.status' && typeof data.message === 'string') {
      status.hidden = false;
      status.textContent = data.message;
      return;
    }
    if (data?.type !== 'settings.state' || typeof data.dataFolder !== 'string') return;
    path.hidden = false;
    path.textContent = typeof data.pendingFolder === 'string' ? `다음 실행부터 사용: ${data.pendingFolder}` : data.dataFolder;
    startInTray.checked = data.startInTray === true;
    change.hidden = data.canChangeFolder !== true;
    autoUpdate.checked = data.autoUpdate !== false;
    if (typeof data.captureDelay === 'number') captureDelay.value = String(data.captureDelay);
    document.querySelector<HTMLElement>('#auto-update-row')!.hidden = data.canUpdate !== true;
    if (typeof data.version === 'string') document.querySelector('#app-version')!.textContent = `버전 ${data.version}`;
  });
}
