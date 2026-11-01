import * as monaco from 'monaco-editor/editor/editor.api';
import { preferenceOptions, sharedEditorOptions } from './monaco-setup';
import './code-languages';
import { formatSource, sourceLanguages } from './code-format';
import { applyTypography, editorTextSize, fontStack, readFontSettings } from './fonts';
import './style.css';

// 메모와 별개인 소스코드 포맷 창입니다. 입력은 저장하지 않으며 앱과 메시지를 주고받지 않습니다.
const languageKey = 'devpostit.formatter.language.v1';
const select = document.querySelector<HTMLSelectElement>('#formatter-language')!;
const button = document.querySelector<HTMLButtonElement>('#formatter-format')!;
const status = document.querySelector<HTMLElement>('#formatter-status')!;
const container = document.querySelector<HTMLElement>('#formatter-code')!;
for (const language of sourceLanguages) select.append(new Option(language.label, language.value));
function savedLanguage(): string {
  try { return localStorage.getItem(languageKey) ?? 'json'; }
  catch { return 'json'; }
}
select.value = savedLanguage();
if (!select.value) select.value = 'json';
const monacoLanguage = () => sourceLanguages.find(language => language.value === select.value)!.monaco;

const view = monaco.editor.create(container, { ...sharedEditorOptions, language: monacoLanguage(), ariaLabel: '소스코드' });
const model = view.getModel()!;
select.addEventListener('change', () => {
  monaco.editor.setModelLanguage(model, monacoLanguage());
  status.textContent = '';
  try { localStorage.setItem(languageKey, select.value); }
  catch { /* 저장하지 못해도 포맷은 할 수 있습니다. */ }
});

let composing = false;
container.addEventListener('compositionstart', () => { composing = true; });
container.addEventListener('compositionend', () => { composing = false; });
let formatting = false;
// 포맷 중에 입력이 바뀌면 그 입력을 덮어쓰지 않습니다. 실패하면 원문을 그대로 둡니다.
async function formatCode(): Promise<void> {
  if (formatting) return;
  if (composing) { status.textContent = '글자 조합을 마친 뒤 실행해 주세요.'; return; }
  const source = model.getValue(monaco.editor.EndOfLinePreference.LF);
  if (!source.trim()) { status.textContent = '소스코드를 입력해 주세요.'; return; }
  const version = model.getVersionId();
  const language = select.value;
  formatting = button.disabled = true;
  status.textContent = '포맷하는 중입니다…';
  try {
    const result = await formatSource(source, language);
    if (model.getVersionId() !== version || select.value !== language) { status.textContent = '입력이 바뀌어 포맷을 취소했습니다.'; return; }
    view.pushUndoStop();
    view.executeEdits('format-source', [{ range: model.getFullModelRange(), text: result }]);
    view.pushUndoStop();
    status.textContent = '포맷했습니다.';
  } catch (error) {
    status.textContent = `포맷하지 못했습니다. 언어와 문법을 확인해 주세요. ${String((error as Error)?.message ?? error).split('\n')[0].slice(0, 200)}`;
  } finally {
    formatting = button.disabled = false;
    view.focus();
  }
}
button.addEventListener('click', () => void formatCode());
view.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Alt | monaco.KeyCode.KeyF, () => void formatCode());
view.addCommand(monaco.KeyMod.Shift | monaco.KeyMod.Alt | monaco.KeyCode.KeyF, () => void formatCode());

function applySettings(): void {
  const settings = readFontSettings();
  applyTypography(settings);
  const size = editorTextSize(settings);
  view.updateOptions({ ...preferenceOptions(settings.editor), fontSize: size, lineHeight: Math.round(size * settings.lineHeight), fontFamily: fontStack(settings.font), fontLigatures: settings.ligatures });
  model.updateOptions({ bracketColorizationOptions: { enabled: settings.editor.bracketColors, independentColorPoolPerBracketType: false } });
  monaco.editor.remeasureFonts();
}
window.addEventListener('storage', applySettings);
applySettings();
void document.fonts.ready.then(() => monaco.editor.remeasureFonts());
view.focus();

Object.assign(window, {
  hunminFormatter: {
    getValue: () => model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true),
    setValue: (text: string) => model.setValue(text),
    getLanguage: () => model.getLanguageId(),
  },
});
