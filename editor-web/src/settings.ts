import { readFontSettings, saveFontSettings, type FontKey } from './fonts';
import './style.css';

// 설정 창 전용 페이지입니다. 저장하면 같은 프로필의 열린 메모가 storage 이벤트로 반영합니다.
const fontSelect = document.querySelector<HTMLSelectElement>('#font')!;
const ligatures = document.querySelector<HTMLInputElement>('#ligatures')!;
const smaller = document.querySelector<HTMLButtonElement>('#smaller')!;
const larger = document.querySelector<HTMLButtonElement>('#larger')!;
let textSize = 15;

function show(): void {
  const settings = readFontSettings();
  textSize = settings.size;
  fontSelect.value = settings.font;
  ligatures.checked = settings.ligatures;
  render();
}
function render(): void {
  document.querySelector('#text-size')!.textContent = String(textSize);
  smaller.disabled = textSize <= 10;
  larger.disabled = textSize >= 24;
}
function save(): void {
  saveFontSettings({ font: fontSelect.value as FontKey, ligatures: ligatures.checked, size: textSize });
  render();
}
fontSelect.addEventListener('change', save);
ligatures.addEventListener('change', save);
smaller.addEventListener('click', () => { textSize = Math.max(10, textSize - 1); save(); });
larger.addEventListener('click', () => { textSize = Math.min(24, textSize + 1); save(); });
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
  for (const row of ['#backup-row', '#update-row']) document.querySelector<HTMLElement>(row)!.hidden = false;
  document.querySelector('#backup')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.backup' }));
  document.querySelector('#restore')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.restore' }));
  document.querySelector('#check-update')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.checkUpdate' }));
  autoUpdate.addEventListener('change', () => host.postMessage({ protocolVersion: 1, type: 'settings.autoUpdate', value: autoUpdate.checked }));
  startInTray.addEventListener('change', () => host.postMessage({ protocolVersion: 1, type: 'settings.startInTray', value: startInTray.checked }));
  host.addEventListener('message', event => {
    const data = event.data as { type?: unknown; message?: unknown; dataFolder?: unknown; pendingFolder?: unknown; startInTray?: unknown; canChangeFolder?: unknown; autoUpdate?: unknown; canUpdate?: unknown; version?: unknown } | null;
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
    document.querySelector<HTMLElement>('#auto-update-row')!.hidden = data.canUpdate !== true;
    if (typeof data.version === 'string') document.querySelector('#app-version')!.textContent = `버전 ${data.version}`;
  });
}
