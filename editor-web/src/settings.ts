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

// 경로는 보내지 않습니다. 앱이 자신의 데이터 폴더만 엽니다. 브라우저 단독 실행에서는 표시하지 않습니다.
const host = window.chrome?.webview as unknown as { postMessage(message: unknown): void } | undefined;
if (host) {
  document.querySelector<HTMLElement>('#data-folder-row')!.hidden = false;
  document.querySelector('#open-data-folder')!.addEventListener('click', () => host.postMessage({ protocolVersion: 1, type: 'settings.openDataFolder' }));
}
