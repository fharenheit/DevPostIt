export const fonts = {
  d2: '"D2 Ligature", "JetBrains Mono", Consolas, monospace',
  jetbrains: '"JetBrains Mono", "D2 Ligature", Consolas, monospace',
  system: 'Consolas, "D2 Ligature", monospace',
} as const;
export type FontKey = keyof typeof fonts;
export type EditorPreferences = { lineNumbers: boolean; wordWrap: boolean; minimap: boolean; whitespace: boolean; bracketColors: boolean; stickyScroll: boolean };
export const defaultEditorPreferences: EditorPreferences = { lineNumbers: true, wordWrap: true, minimap: false, whitespace: false, bracketColors: false, stickyScroll: false };
export type FontSettings = { font: FontKey; ligatures: boolean; size: number; editor: EditorPreferences };
function readEditorPreferences(value: unknown): EditorPreferences {
  const saved = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const result = { ...defaultEditorPreferences };
  for (const name of Object.keys(result) as (keyof EditorPreferences)[]) if (typeof saved[name] === 'boolean') result[name] = saved[name];
  return result;
}
const key = 'devpostit.editor.preferences.v1';

export function readFontSettings(): FontSettings {
  try {
    const value = JSON.parse(localStorage.getItem(key) ?? localStorage.getItem('hunmin.editor.fonts.v1') ?? 'null');
    if (value && Object.hasOwn(fonts, value.font) && typeof value.ligatures === 'boolean') {
      const size = Number.isInteger(value.size) && value.size >= 10 && value.size <= 24 ? value.size : 15;
      return { font: value.font, ligatures: value.ligatures, size, editor: readEditorPreferences(value.editor) };
    }
  } catch { /* 저장 설정이 없거나 손상되었으면 기본값으로 복구합니다. */ }
  return { font: 'd2', ligatures: true, size: 15, editor: { ...defaultEditorPreferences } };
}

export function saveFontSettings(settings: FontSettings): void {
  try { localStorage.setItem(key, JSON.stringify(settings)); }
  catch { /* 설정 저장 제한이 본문 편집을 막지 않게 합니다. */ }
}
