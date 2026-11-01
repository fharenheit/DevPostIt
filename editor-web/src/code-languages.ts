import * as monaco from 'monaco-editor/editor/editor.api';
// Monaco가 제공하는 모든 언어 정의를 등록합니다. 토크나이저는 해당 언어를 처음 쓸 때 불러옵니다.
import 'monaco-editor/languages/definitions/register.all';

// Monaco의 JSON은 작업자가 필요한 언어 서비스에 묶여 있어 색 구분만 하는 토크나이저를 따로 둡니다.
monaco.languages.register({ id: 'json', extensions: ['.json'], aliases: ['JSON', 'json', 'jsonc'] });
monaco.languages.setMonarchTokensProvider('json', {
  tokenizer: {
    root: [
      [/"(?:[^"\\]|\\.)*"(?=\s*:)/, 'attribute.name'],
      [/"(?:[^"\\]|\\.)*"/, 'string'],
      [/-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/, 'number'],
      [/\b(?:true|false|null)\b/, 'keyword'],
      [/\/\/.*$/, 'comment'],
      [/[{}[\],:]/, 'delimiter'],
    ],
  },
});
// Markdown에서 자주 쓰는 언어 이름을 추가합니다. 소스 편집의 코드 블록도 이 이름으로 언어를 찾습니다.
for (const [id, aliases] of Object.entries({
  shell: ['bash', 'zsh', 'shell'],
  csharp: ['cs'],
  python: ['python', 'py'],
  powershell: ['ps1', 'pwsh'],
  cpp: ['cc', 'cxx', 'hpp'],
  go: ['golang'],
  rust: ['rs'],
  kotlin: ['kt'],
  ruby: ['rb'],
  javascript: ['jsx'],
  typescript: ['tsx'],
})) monaco.languages.register({ id, aliases });

export function findLanguageId(name: string): string | undefined {
  const wanted = name.toLowerCase();
  return monaco.languages.getLanguages().find(language => language.id === wanted || language.aliases?.some(alias => alias.toLowerCase() === wanted))?.id;
}
