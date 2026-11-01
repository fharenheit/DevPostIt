import * as monaco from 'monaco-editor/editor/editor.api';
import './editor-features';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import type { EditorPreferences } from './fonts';

// 메모와 소스코드 포맷터가 같은 Monaco 기능, 테마, 옵션을 씁니다.
globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() };

// style.css의 메모지 색 토큰과 같은 값을 사용합니다.
monaco.editor.defineTheme('devpostit-paper', {
  base: 'vs', inherit: false,
  rules: [
    { token: '', foreground: '2F2A1F' },
    { token: 'keyword.md', foreground: '7A5C00', fontStyle: 'bold' },
    { token: 'string.md', foreground: '7A5C00' },
    { token: 'string.link.md', foreground: '7A5C00' },
    { token: 'variable.md', foreground: '665E4B' },
    { token: 'comment.md', foreground: '665E4B', fontStyle: 'italic' },
    { token: 'strong', fontStyle: 'bold' },
    { token: 'emphasis', fontStyle: 'italic' },
    // 코드 블록: 대비 4.5:1 이상을 유지하는 어두운 색만 사용합니다.
    { token: 'keyword', foreground: '9A2F12' },
    { token: 'string', foreground: '2B6636' },
    { token: 'number', foreground: '0B5A85' },
    { token: 'comment', foreground: '665E4B', fontStyle: 'italic' },
    { token: 'type', foreground: '6A3F99' },
    { token: 'annotation', foreground: '6A3F99' },
    { token: 'tag', foreground: '9A2F12' },
    { token: 'attribute.name', foreground: '0B5A85' },
    { token: 'attribute.value', foreground: '2B6636' },
    { token: 'delimiter', foreground: '4F4838' },
    { token: 'variable', foreground: '0B5A85' },
    { token: 'regexp', foreground: '2B6636' },
    { token: 'metatag', foreground: '6A3F99' },
    { token: 'predefined', foreground: '6A3F99' },
  ],
  colors: {
    'editor.background': '#FFFDF5',
    'editor.foreground': '#2F2A1F',
    'editorCursor.foreground': '#7A5C00',
    'editorLineNumber.foreground': '#A89E80',
    'editorLineNumber.activeForeground': '#665E4B',
    'editor.selectionBackground': '#FFE88F',
    'editor.inactiveSelectionBackground': '#F8F2DC',
    'editor.lineHighlightBackground': '#F8F2DC',
    'editor.lineHighlightBorder': '#00000000',
    'scrollbarSlider.background': '#C9BB8866',
    'scrollbarSlider.hoverBackground': '#C9BB88AA',
    'scrollbarSlider.activeBackground': '#C9BB88',
  },
});

export const sharedEditorOptions: monaco.editor.IStandaloneEditorConstructionOptions = {
  theme: 'devpostit-paper', automaticLayout: true,
  minimap: { enabled: false }, wordWrap: 'on', fontSize: 13, lineHeight: 22,
  padding: { top: 12, bottom: 12 }, scrollBeyondLastLine: false,
  lineNumbersMinChars: 3, lineDecorationsWidth: 8, renderLineHighlight: 'line', renderLineHighlightOnlyWhenFocus: false,
  folding: true, showFoldingControls: 'mouseover', matchBrackets: 'always', contextmenu: true, smoothScrolling: true, mouseWheelZoom: false,
  guides: { indentation: true }, wordBasedSuggestions: 'currentDocument',
  find: { addExtraSpaceOnTop: false, seedSearchStringFromSelection: 'selection', autoFindInSelection: 'multiline' },
  overviewRulerLanes: 0, overviewRulerBorder: false, hideCursorInOverviewRuler: true,
  scrollbar: { verticalScrollbarSize: 8, horizontalScrollbarSize: 8, useShadows: false },
  editContext: false,
  links: false,
  renderWhitespace: 'selection', tabSize: 2, quickSuggestions: false,
  unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false },
};
export const preferenceOptions = (preferences: EditorPreferences): monaco.editor.IEditorOptions => ({
  lineNumbers: preferences.lineNumbers ? 'on' : 'off',
  wordWrap: preferences.wordWrap ? 'on' : 'off',
  minimap: { enabled: preferences.minimap },
  renderWhitespace: preferences.whitespace ? 'all' : 'selection',
  stickyScroll: { enabled: preferences.stickyScroll },
});
