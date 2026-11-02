import * as monaco from 'monaco-editor/editor/editor.api';
import { preferenceOptions, sharedEditorOptions } from './monaco-setup';
import './code-languages';
import { CodeBlockError, formatSource, languageLabel, sourceLanguages } from './code-format';
import { applyTypography, editorTextSize, fontStack, readFontSettings } from './fonts';
import './style.css';

// 메모와 별개인 소스코드 포맷 창입니다. 입력은 저장하지 않으며 앱과 메시지를 주고받지 않습니다.
const languageKey = 'devpostit.formatter.language.v1';
const indentKey = 'devpostit.formatter.indent.v1';
const defaultIndent = 4, maxIndent = 16;
const select = document.querySelector<HTMLSelectElement>('#formatter-language')!;
const button = document.querySelector<HTMLButtonElement>('#formatter-format')!;
const status = document.querySelector<HTMLElement>('#formatter-status')!;
const container = document.querySelector<HTMLElement>('#formatter-code')!;
const indentInput = document.querySelector<HTMLInputElement>('#formatter-indent')!;
for (const language of sourceLanguages) select.append(new Option(languageLabel(language), language.value));
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

// 간격은 1~${maxIndent}의 숫자만 받습니다. 범위를 벗어나면 null입니다.
function readIndent(text: string | null): number | null {
  if (!text || !/^\d+$/.test(text)) return null;
  const value = Number(text);
  return value >= 1 && value <= maxIndent ? value : null;
}
function savedIndent(): number {
  try { return readIndent(localStorage.getItem(indentKey)) ?? defaultIndent; }
  catch { return defaultIndent; }
}
function useIndent(): void {
  const indent = readIndent(indentInput.value);
  if (indent === null) return;
  model.updateOptions({ tabSize: indent, indentSize: indent, insertSpaces: true });
  try { localStorage.setItem(indentKey, String(indent)); }
  catch { /* 저장하지 못해도 포맷은 할 수 있습니다. */ }
}
function keepDigits(): void {
  const digits = indentInput.value.replace(/\D/g, '');
  if (digits !== indentInput.value) indentInput.value = digits;
  useIndent();
}
indentInput.value = String(savedIndent());
useIndent();
indentInput.addEventListener('input', event => { if (!(event as InputEvent).isComposing) keepDigits(); });
indentInput.addEventListener('compositionend', keepDigits);
indentInput.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); void formatCode(); } });

let composing = false;
container.addEventListener('compositionstart', () => { composing = true; });
container.addEventListener('compositionend', () => { composing = false; });
let formatting = false;
// 포맷 중에 입력이 바뀌면 그 입력을 덮어쓰지 않습니다. 실패하면 원문을 그대로 둡니다.
async function formatCode(): Promise<void> {
  if (formatting) return;
  if (composing) { status.textContent = '글자 조합을 마친 뒤 실행해 주세요.'; return; }
  const indent = readIndent(indentInput.value);
  if (indent === null) { status.textContent = `들여쓰기 간격은 1~${maxIndent} 사이의 숫자로 입력해 주세요.`; indentInput.focus(); return; }
  const source = model.getValue(monaco.editor.EndOfLinePreference.LF);
  if (!source.trim()) { status.textContent = '소스코드를 입력해 주세요.'; return; }
  const version = model.getVersionId();
  const language = select.value;
  formatting = button.disabled = true;
  status.textContent = '포맷하는 중입니다…';
  try {
    const result = await formatSource(source, language, indent);
    if (model.getVersionId() !== version || select.value !== language) { status.textContent = '입력이 바뀌어 포맷을 취소했습니다.'; return; }
    view.pushUndoStop();
    view.executeEdits('format-source', [{ range: model.getFullModelRange(), text: result }]);
    view.pushUndoStop();
    status.textContent = '포맷했습니다.';
  } catch (error) {
    status.textContent = error instanceof CodeBlockError ? error.message : `포맷하지 못했습니다. 언어와 문법을 확인해 주세요. ${String((error as Error)?.message ?? error).split('\n')[0].slice(0, 200)}`;
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
    getTabSize: () => model.getOptions().tabSize,
  },
});
