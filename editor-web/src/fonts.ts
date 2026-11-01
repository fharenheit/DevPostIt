export const fonts = {
  d2: '"D2 Ligature", "JetBrains Mono", Consolas, monospace',
  jetbrains: '"JetBrains Mono", "D2 Ligature", Consolas, monospace',
  system: 'Consolas, "D2 Ligature", monospace',
} as const;
export type FontKey = keyof typeof fonts;
export type FontSettings = { font: FontKey; ligatures: boolean };
const key = 'hunmin.editor.fonts.v1';

export function readFontSettings(): FontSettings {
  try {
    const value = JSON.parse(localStorage.getItem(key) ?? 'null');
    if (value && Object.hasOwn(fonts, value.font) && typeof value.ligatures === 'boolean') return value;
  } catch { /* 저장 설정이 없거나 손상되었으면 기본값으로 복구합니다. */ }
  return { font: 'd2', ligatures: true };
}

export function saveFontSettings(settings: FontSettings): void {
  try { localStorage.setItem(key, JSON.stringify(settings)); }
  catch { /* 설정 저장 제한이 본문 편집을 막지 않게 합니다. */ }
}
