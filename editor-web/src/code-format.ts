import MarkdownIt from 'markdown-it';
import { format } from 'prettier/standalone';
import type { Plugin } from 'prettier';

export class CodeBlockError extends Error {}
const markdown = new MarkdownIt({ html: false });
const aliases: Record<string, string> = {
  json: 'json', jsonc: 'json', json5: 'json5', yaml: 'yaml', yml: 'yaml', java: 'java',
  javascript: 'babel', js: 'babel', jsx: 'babel', mjs: 'babel', cjs: 'babel', flow: 'flow',
  typescript: 'typescript', ts: 'typescript', tsx: 'typescript', mts: 'typescript', cts: 'typescript',
  html: 'html', vue: 'vue', angular: 'angular', handlebars: 'glimmer', hbs: 'glimmer',
  css: 'css', scss: 'scss', less: 'less', graphql: 'graphql', gql: 'graphql', markdown: 'markdown', md: 'markdown', mdx: 'mdx',
  xml: 'xml', svg: 'xml', php: 'php', python: 'python', py: 'python', go: 'go', golang: 'go', shell: 'shell', sh: 'shell', bash: 'shell', zsh: 'shell',
  sql: 'sql:sql', mysql: 'sql:mysql', pgsql: 'sql:postgresql', postgresql: 'sql:postgresql', postgres: 'sql:postgresql', redshift: 'sql:redshift',
  c: 'clang:main.c', h: 'clang:main.c', cpp: 'clang:main.cc', 'c++': 'clang:main.cc', cc: 'clang:main.cc', cxx: 'clang:main.cc', hpp: 'clang:main.cc',
  csharp: 'clang:main.cs', cs: 'clang:main.cs', 'c#': 'clang:main.cs', 'objective-c': 'clang:main.m', objc: 'clang:main.m', proto: 'clang:main.proto', protobuf: 'clang:main.proto',
};
// 언어 목록입니다. monaco는 색 구분에 쓰는 언어입니다. Monaco는 색 구분만 제공하므로 정리 도구가 없는 언어는 색 구분만 됩니다.
const language = (value: string, label: string, monaco = value) => ({ value, label, monaco });
export const sourceLanguages = [
  language('abap', 'ABAP'), language('apex', 'Apex'), language('angular', 'Angular', 'html'), language('postiats', 'ATS'), language('azcli', 'Azure CLI'), language('bat', 'Batch'),
  language('bicep', 'Bicep'), language('c', 'C'), language('csharp', 'C#'), language('cpp', 'C++'), language('cameligo', 'CameLIGO'), language('clojure', 'Clojure'),
  language('coffeescript', 'CoffeeScript'), language('csp', 'CSP'), language('css', 'CSS'), language('cypher', 'Cypher'), language('dart', 'Dart'), language('msdax', 'DAX'),
  language('dockerfile', 'Dockerfile'), language('ecl', 'ECL'), language('elixir', 'Elixir'), language('fsharp', 'F#'), language('flow', 'Flow', 'javascript'), language('flow9', 'Flow9'),
  language('freemarker2', 'FreeMarker'), language('go', 'Go'), language('graphql', 'GraphQL'), language('handlebars', 'Handlebars'), language('hcl', 'HCL'), language('html', 'HTML'),
  language('ini', 'INI'), language('java', 'Java'), language('js', 'JavaScript', 'javascript'), language('json', 'JSON'), language('json5', 'JSON5', 'json'), language('jsx', 'JSX', 'javascript'),
  language('julia', 'Julia'), language('kotlin', 'Kotlin'), language('less', 'Less'), language('lexon', 'Lexon'), language('liquid', 'Liquid'), language('lua', 'Lua'),
  language('m3', 'M3'), language('mips', 'MIPS'), language('mysql', 'MySQL'), language('objective-c', 'Objective-C'), language('pascal', 'Pascal'), language('pascaligo', 'PascaLIGO'),
  language('perl', 'Perl'), language('php', 'PHP'), language('pla', 'PLA'), language('pgsql', 'PostgreSQL'), language('powerquery', 'Power Query'), language('powershell', 'PowerShell'),
  language('proto', 'Protobuf'), language('pug', 'Pug'), language('python', 'Python'), language('qsharp', 'Q#'), language('r', 'R'), language('razor', 'Razor'),
  language('restructuredtext', 'reStructuredText'), language('redis', 'Redis'), language('redshift', 'Redshift'), language('ruby', 'Ruby'), language('rust', 'Rust'), language('scala', 'Scala'),
  language('scheme', 'Scheme'), language('scss', 'SCSS'), language('shell', 'Shell'), language('sb', 'Small Basic'), language('sol', 'Solidity'), language('aes', 'Sophia'),
  language('sparql', 'SPARQL'), language('sql', 'SQL'), language('st', 'Structured Text'), language('swift', 'Swift'), language('systemverilog', 'SystemVerilog'), language('tcl', 'Tcl'),
  language('tsx', 'TSX', 'typescript'), language('twig', 'Twig'), language('ts', 'TypeScript', 'typescript'), language('typespec', 'TypeSpec'), language('verilog', 'Verilog'), language('vb', 'Visual Basic'),
  language('vue', 'Vue', 'html'), language('wgsl', 'WGSL'), language('xml', 'XML'), language('yaml', 'YAML'),
];
const canFormat = (value: string) => Object.hasOwn(aliases, value);
export const languageLabel = (item: { value: string; label: string }) => canFormat(item.value) ? item.label : `${item.label} (색 구분만)`;
const noFormatter = '이 언어는 색 구분만 지원합니다. 정리 도구가 없습니다.';
// wasm 도구는 해당 언어를 처음 정리할 때 불러옵니다.
function once<T extends { default(): Promise<unknown> }>(load: () => Promise<T>): () => Promise<T> {
  let module: Promise<T> | undefined;
  return () => module ??= load().then(async loaded => { await loaded.default(); return loaded; });
}
const ruff = once(() => import('@wasm-fmt/ruff_fmt/vite'));
const gofmt = once(() => import('@wasm-fmt/gofmt/vite'));
const shfmt = once(() => import('@wasm-fmt/shfmt/vite'));
const clang = once(() => import('@wasm-fmt/clang-format/vite'));
const clangStyle = (file: string) => file.endsWith('.cs') ? '{BasedOnStyle: Microsoft}' : '{BasedOnStyle: Google, IndentWidth: 4, ColumnLimit: 100}';
async function plugins(parser: string): Promise<Plugin[]> {
  if (parser === 'java') {
    const { Parser } = await import('web-tree-sitter');
    const { default: wasm } = await import('web-tree-sitter/web-tree-sitter.wasm?url');
    await Parser.init({ locateFile: () => wasm });
    return [(await import('prettier-plugin-java')).default];
  }
  if (parser === 'xml') return [(await import('@prettier/plugin-xml')).default as unknown as Plugin];
  if (parser === 'php') return [(await import('@prettier/plugin-php/standalone')).default as unknown as Plugin];
  if (parser === 'yaml') return [await import('prettier/plugins/yaml')];
  if (parser === 'html') return [await import('prettier/plugins/html')];
  if (parser === 'css' || parser === 'scss' || parser === 'less') return [await import('prettier/plugins/postcss')];
  if (parser === 'graphql') return [await import('prettier/plugins/graphql')];
  if (parser === 'markdown' || parser === 'mdx') return [await import('prettier/plugins/markdown')];
  if (parser === 'glimmer') return [await import('prettier/plugins/glimmer')];
  if (parser === 'flow') return [await import('prettier/plugins/flow'), await import('prettier/plugins/estree')];
  if (parser === 'vue' || parser === 'angular') return [await import('prettier/plugins/html'), await import('prettier/plugins/angular'), await import('prettier/plugins/babel'), await import('prettier/plugins/estree'), await import('prettier/plugins/postcss')];
  return [parser === 'typescript' ? await import('prettier/plugins/typescript') : await import('prettier/plugins/babel'), await import('prettier/plugins/estree')];
}
async function formatWith(source: string, parser: string): Promise<string> {
  if (parser === 'python') return (await ruff()).format(source);
  if (parser === 'go') return (await gofmt()).format(source);
  if (parser === 'shell') return (await shfmt()).format(source);
  if (parser.startsWith('clang:')) { const file = parser.slice('clang:'.length); return (await clang()).format(source, file, clangStyle(file)); }
  if (parser.startsWith('sql:')) {
    const { format: formatSql } = await import('sql-formatter');
    return formatSql(source, { language: parser.slice('sql:'.length) as 'sql', tabWidth: 2 });
  }
  return format(source, { parser, plugins: await plugins(parser), tabWidth: 2, printWidth: 100, endOfLine: 'lf', ...(parser === 'xml' ? { xmlWhitespaceSensitivity: 'ignore' } : parser === 'php' ? { phpVersion: '8.4' } : {}) });
}
export async function formatSource(source: string, language: string): Promise<string> {
  const parser = aliases[language];
  if (!parser) throw new CodeBlockError(sourceLanguages.some(item => item.value === language) ? noFormatter : '지원하지 않는 언어입니다.');
  return formatWith(source, parser);
}
export async function formatBlock(source: string, line: number, language?: string): Promise<{ start: number; end: number; startLine: number; endLine: number; text: string }> {
  const token = markdown.parse(source, {}).find(token => token.type === 'fence' && token.map && line >= token.map[0] && line < token.map[1]);
  if (!token?.map || token.level !== 0) throw new CodeBlockError('최상위 코드 블록 안에 커서를 놓아 주세요.');
  const lines = source.split('\n');
  const [first, last] = token.map;
  const opening = /^( {0,3})(`{3,}|~{3,})/.exec(lines[first]);
  const closing = opening && new RegExp(`^ {0,3}${opening[2][0]}{${opening[2].length},}\\s*$`).test(lines[last - 1]);
  if (!opening || !closing || last - first < 2) throw new CodeBlockError('코드 블록의 닫는 울타리가 필요합니다.');
  const name = (language || token.info.trim().split(/\s/)[0]).toLowerCase();
  const parser = aliases[name];
  if (!parser) throw new CodeBlockError(sourceLanguages.some(item => item.value === name) ? noFormatter : '지원하는 코드 언어를 선택하거나 블록에 표시해 주세요.');
  const result = await formatWith(token.content, parser);
  const eol = lines[first].endsWith('\r') ? '\r\n' : '\n';
  const text = result.trimEnd().split('\n').map(line => opening[1] + line).join(eol) + eol;
  const offset = (line: number) => lines.slice(0, line).reduce((count, text) => count + text.length + 1, 0);
  return { start: offset(first + 1), end: offset(last - 1), startLine: first + 2, endLine: last, text };
}
