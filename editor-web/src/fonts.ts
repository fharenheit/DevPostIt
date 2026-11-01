// 한글은 D2의 폭(1em)을 라틴 글자 폭의 2배에 맞춘 별칭으로 표시해 코드 정렬을 유지합니다.
export const fonts = {
  d2: '"D2 Ligature", "JetBrains Mono", Consolas, monospace',
  jetbrains: '"JetBrains Mono", "D2 Hangul 120", "D2 Ligature", Consolas, monospace',
  system: 'Consolas, "D2 Hangul 110", "D2 Ligature", monospace',
} as const;
export type FontKey = keyof typeof fonts | `custom:${string}`;
export type EditorPreferences = { lineNumbers: boolean; wordWrap: boolean; minimap: boolean; whitespace: boolean; bracketColors: boolean; stickyScroll: boolean };
export const defaultEditorPreferences: EditorPreferences = { lineNumbers: true, wordWrap: true, minimap: false, whitespace: false, bracketColors: false, stickyScroll: false };
export type FontSettings = { font: FontKey; ligatures: boolean; size: number; editor: EditorPreferences; editorSize: number | null; lineHeight: number; bodyFont: string };
export const minTextSize = 10, maxTextSize = 24, defaultTextSize = 15, minEditorSize = 8, maxEditorSize = 24, minLineHeight = 1.2, maxLineHeight = 2.2, defaultLineHeight = 1.7;
export const preferencesKey = 'devpostit.editor.preferences.v1';
const defaultBodyStack = '"Segoe UI", "Malgun Gothic", sans-serif';
// 글꼴 이름은 CSS에 들어가므로 따옴표 등을 허용하지 않습니다.
const safeFontName = /^[\p{L}\p{N} ._-]{1,100}$/u;
export const isSafeFontName = (name: unknown): name is string => typeof name === 'string' && safeFontName.test(name);

export function defaultFontSettings(): FontSettings {
  return { font: 'd2', ligatures: true, size: defaultTextSize, editor: { ...defaultEditorPreferences }, editorSize: null, lineHeight: defaultLineHeight, bodyFont: '' };
}
function isFontKey(value: unknown): value is FontKey {
  if (typeof value !== 'string') return false;
  return value.startsWith('custom:') ? isSafeFontName(value.slice('custom:'.length)) : Object.hasOwn(fonts, value);
}
function readEditorPreferences(value: unknown): EditorPreferences {
  const saved = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const result = { ...defaultEditorPreferences };
  for (const name of Object.keys(result) as (keyof EditorPreferences)[]) if (typeof saved[name] === 'boolean') result[name] = saved[name];
  return result;
}

export function readFontSettings(): FontSettings {
  try {
    const value = JSON.parse(localStorage.getItem(preferencesKey) ?? localStorage.getItem('hunmin.editor.fonts.v1') ?? 'null');
    if (value && isFontKey(value.font) && typeof value.ligatures === 'boolean') {
      const size = Number.isInteger(value.size) && value.size >= minTextSize && value.size <= maxTextSize ? value.size : defaultTextSize;
      const editorSize = Number.isInteger(value.editorSize) && value.editorSize >= minEditorSize && value.editorSize <= maxEditorSize ? value.editorSize : null;
      const lineHeight = typeof value.lineHeight === 'number' && value.lineHeight >= minLineHeight && value.lineHeight <= maxLineHeight ? value.lineHeight : defaultLineHeight;
      const bodyFont = isSafeFontName(value.bodyFont) ? value.bodyFont : '';
      return { font: value.font, ligatures: value.ligatures, size, editor: readEditorPreferences(value.editor), editorSize, lineHeight, bodyFont };
    }
  } catch { /* 저장 설정이 없거나 손상되었으면 기본값으로 복구합니다. */ }
  return defaultFontSettings();
}

export function saveFontSettings(settings: FontSettings): void {
  try { localStorage.setItem(preferencesKey, JSON.stringify(settings)); }
  catch { /* 설정 저장 제한이 본문 편집을 막지 않게 합니다. */ }
}
// 편집기 크기를 따로 정하지 않으면 전체 크기보다 2px 작게 따라갑니다.
export const editorTextSize = (settings: FontSettings) => settings.editorSize ?? Math.max(minEditorSize, settings.size - 2);
export const fontStack = (font: FontKey) => font.startsWith('custom:') ? `"${font.slice('custom:'.length)}", "D2 Ligature", Consolas, monospace` : fonts[font as keyof typeof fonts];
export const bodyStack = (name: string) => name ? `"${name}", ${defaultBodyStack}` : defaultBodyStack;
// Consolas에는 합자가 없습니다. 설치된 글꼴은 알 수 없어 켤 수 있게 둡니다.
export const supportsLigatures = (font: FontKey) => font !== 'system';
export function applyTypography(settings: FontSettings): void {
  const root = document.documentElement.style;
  root.setProperty('--code-font', fontStack(settings.font));
  root.setProperty('--code-ligatures', settings.ligatures ? 'normal' : 'none');
  root.setProperty('--body-font', bodyStack(settings.bodyFont));
  root.setProperty('--text-line-height', String(settings.lineHeight));
}
