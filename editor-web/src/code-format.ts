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
};
async function plugins(parser: string): Promise<Plugin[]> {
  if (parser === 'java') {
    const { Parser } = await import('web-tree-sitter');
    const { default: wasm } = await import('web-tree-sitter/web-tree-sitter.wasm?url');
    await Parser.init({ locateFile: () => wasm });
    return [(await import('prettier-plugin-java')).default];
  }
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
export async function formatBlock(source: string, line: number, language?: string): Promise<{ start: number; end: number; startLine: number; endLine: number; text: string }> {
  const token = markdown.parse(source, {}).find(token => token.type === 'fence' && token.map && line >= token.map[0] && line < token.map[1]);
  if (!token?.map || token.level !== 0) throw new CodeBlockError('최상위 코드 블록 안에 커서를 놓아 주세요.');
  const lines = source.split('\n');
  const [first, last] = token.map;
  const opening = /^( {0,3})(`{3,}|~{3,})/.exec(lines[first]);
  const closing = opening && new RegExp(`^ {0,3}${opening[2][0]}{${opening[2].length},}\\s*$`).test(lines[last - 1]);
  if (!opening || !closing || last - first < 2) throw new CodeBlockError('코드 블록의 닫는 울타리가 필요합니다.');
  const parser = aliases[(language || token.info.trim().split(/\s/)[0]).toLowerCase()];
  if (!parser) throw new CodeBlockError('지원하는 코드 언어를 선택하거나 블록에 표시해 주세요.');
  const result = await format(token.content, { parser, plugins: await plugins(parser), tabWidth: 2, printWidth: 100, endOfLine: 'lf' });
  const eol = lines[first].endsWith('\r') ? '\r\n' : '\n';
  const text = result.trimEnd().split('\n').map(line => opening[1] + line).join(eol) + eol;
  const offset = (line: number) => lines.slice(0, line).reduce((count, text) => count + text.length + 1, 0);
  return { start: offset(first + 1), end: offset(last - 1), startLine: first + 2, endLine: last, text };
}
