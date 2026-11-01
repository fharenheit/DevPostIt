import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/bat/register';
import 'monaco-editor/languages/definitions/cpp/register';
import 'monaco-editor/languages/definitions/csharp/register';
import 'monaco-editor/languages/definitions/css/register';
import 'monaco-editor/languages/definitions/dockerfile/register';
import 'monaco-editor/languages/definitions/go/register';
import 'monaco-editor/languages/definitions/html/register';
import 'monaco-editor/languages/definitions/ini/register';
import 'monaco-editor/languages/definitions/java/register';
import 'monaco-editor/languages/definitions/javascript/register';
import 'monaco-editor/languages/definitions/kotlin/register';
import 'monaco-editor/languages/definitions/powershell/register';
import 'monaco-editor/languages/definitions/python/register';
import 'monaco-editor/languages/definitions/rust/register';
import 'monaco-editor/languages/definitions/shell/register';
import 'monaco-editor/languages/definitions/sql/register';
import 'monaco-editor/languages/definitions/typescript/register';
import 'monaco-editor/languages/definitions/xml/register';
import 'monaco-editor/languages/definitions/yaml/register';

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
monaco.languages.register({ id: 'shell', aliases: ['bash', 'zsh', 'shell'] });
monaco.languages.register({ id: 'csharp', aliases: ['cs'] });
monaco.languages.register({ id: 'python', aliases: ['python', 'py'] });
monaco.languages.register({ id: 'powershell', aliases: ['ps1', 'pwsh'] });

export function findLanguageId(name: string): string | undefined {
  const wanted = name.toLowerCase();
  return monaco.languages.getLanguages().find(language => language.id === wanted || language.aliases?.some(alias => alias.toLowerCase() === wanted))?.id;
}
