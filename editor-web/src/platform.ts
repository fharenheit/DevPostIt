// 빌드 대상(EDITOR_TARGET)에 따라 달라지는 값입니다. 테스트처럼 Vite 밖에서 읽을 때는 Windows로 봅니다.
export type EditorTarget = 'windows' | 'macos';
export const editorTarget: EditorTarget = import.meta.env?.VITE_EDITOR_TARGET === 'macos' ? 'macos' : 'windows';

export function isPrimary(event: { ctrlKey: boolean; metaKey: boolean }, target: EditorTarget = editorTarget): boolean {
  return target === 'macos' ? event.metaKey : event.ctrlKey;
}

export function noteAssetUrl(path: string, target: EditorTarget = editorTarget): string {
  return `${target === 'macos' ? 'devpostit-assets://notes' : 'https://devpostit-assets.local'}/${path}`;
}
