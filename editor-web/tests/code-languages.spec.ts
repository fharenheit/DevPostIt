import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

async function openFormatter(page: Page) {
  await page.goto('/formatter.html');
  await page.waitForFunction(() => Boolean((window as any).hunminFormatter));
}
const formatted = (page: Page) => page.evaluate(() => (window as any).hunminFormatter.getValue() as string).then(text => text.replaceAll('\r\n', '\n'));
async function formatAs(page: Page, language: string, text: string) {
  await page.getByLabel('언어').selectOption(language);
  await page.evaluate(source => (window as any).hunminFormatter.setValue(source), text);
  await page.getByRole('button', { name: '포맷', exact: true }).click();
}

const samples = [
  ['python', 'def f( a ):\n  return a', 'def f(a):\n    return a'],
  ['go', 'package main\nfunc main(){}', 'func main() {}'],
  ['sql', 'select a,b from t', 'select\n    a,\n    b\nfrom\n    t'],
  ['mysql', 'select `a` from t', 'select\n    `a`\nfrom\n    t'],
  ['pgsql', 'select a::int from t', 'select\n    a::int\nfrom\n    t'],
  ['c', 'int main(){return 0;}', 'int main() {'],
  ['cpp', 'int main(){std::cout<<1;}', 'std::cout << 1;'],
  ['csharp', 'class A{void F(){}}', 'class A\n{'],
  ['objective-c', '@interface A:NSObject\n@end', '@interface A : NSObject'],
  ['proto', 'message A{int32 b=1;}', 'int32 b = 1;'],
  ['xml', '<a><b>1</b></a>', '<a>\n    <b>1</b>\n</a>'],
  ['shell', 'if true;then echo a;fi', 'if true; then'],
  ['php', '<?php echo  1;', '<?php echo 1;'],
] as const;
for (const [language, source, expected] of samples) {
  test(`포맷터 창에서 ${language}를 포맷한다`, async ({ page }) => {
    await openFormatter(page);
    await formatAs(page, language, source);
    await expect(page.locator('#formatter-status')).toContainText('포맷했습니다');
    expect(await formatted(page)).toContain(expected);
  });
}
// 메모의 코드 정리는 들여쓰기 입력이 없고 각 도구의 기본값을 씁니다.
for (const [language, source, expected] of [
  ['python', 'def f( a ):\n  return a', 'def f(a):\n    return a'],
  ['go', 'package main\nfunc main(){}', 'func main() {}'],
  ['sql', 'select a,b from t', 'select\n  a,\n  b\nfrom\n  t'],
  ['cpp', 'int main(){std::cout<<1;}', 'std::cout << 1;'],
  ['csharp', 'class A{void F(){}}', 'class A\n{'],
  ['xml', '<a><b>1</b></a>', '<a>\n  <b>1</b>\n</a>'],
] as const) {
  test(`메모의 ${language} 코드 블록을 정리한다`, async ({ page }) => {
    await openEditor(page, `\`\`\`${language}\n${source}\n\`\`\`\n`);
    await page.locator('#source .monaco-editor').click({ position: { x: 100, y: 40 } });
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('ArrowDown');
    await page.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(page.locator('#format-status')).toHaveText('코드를 정리했습니다.');
    expect((await draft(page)).markdown.replaceAll('\r\n', '\n')).toContain(expected);
  });
}
for (const [fence, source, expected] of [['c#', 'class A{void F(){}}', 'class A\n{'], ['c++', 'int main(){}', 'int main() {'], ['py', 'a=1', 'a = 1'], ['golang', 'package a\nvar b=1', 'var b = 1'], ['bash', 'a=1;b=2', 'a=1'], ['postgresql', 'select 1', 'select\n  1']] as const) {
  test(`코드 블록의 언어 표시 ${fence}로도 정리한다`, async ({ page }) => {
    await openEditor(page, `\`\`\`${fence}\n${source}\n\`\`\`\n`);
    await page.locator('#source .monaco-editor').click({ position: { x: 100, y: 40 } });
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('ArrowDown');
    await page.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(page.locator('#format-status')).toHaveText('코드를 정리했습니다.');
    expect((await draft(page)).markdown.replaceAll('\r\n', '\n')).toContain(expected);
  });
}

for (const language of ['rust', 'scala', 'ruby', 'r']) {
  test(`정리 도구가 없는 ${language}는 색 구분만 된다고 알리고 원문을 둔다`, async ({ page }) => {
    await openFormatter(page);
    await formatAs(page, language, 'x  =  1');
    await expect(page.locator('#formatter-status')).toContainText('색 구분만');
    expect(await formatted(page)).toBe('x  =  1');
    const text = `\`\`\`${language}\nx  =  1\n\`\`\`\n`;
    await openEditor(page, text);
    await page.locator('#source .monaco-editor').click({ position: { x: 100, y: 40 } });
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('ArrowDown');
    await page.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(page.locator('#format-status')).toContainText('색 구분만');
    expect((await draft(page)).markdown).toBe(text);
  });
}

test('메모와 포맷터의 언어 목록에 요청한 언어가 있고 Monaco에 등록되어 있다', async ({ page }) => {
  const wanted = ['csharp', 'python', 'go', 'sql', 'xml', 'cpp', 'c', 'rust', 'scala', 'ruby', 'r'];
  await openEditor(page);
  const memo = await page.locator('#code-language option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
  expect(memo).toEqual(expect.arrayContaining([...wanted, 'markdown', 'mdx', 'java', 'kotlin', 'swift', 'dart']));
  expect(memo[0]).toBe('');
  await openFormatter(page);
  const select = page.locator('#formatter-language');
  const values = await select.locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
  expect(values).toEqual(expect.arrayContaining(wanted));
  expect(values).not.toContain('markdown');
  expect(values).not.toContain('mdx');
  expect(new Set(values).size).toBe(values.length);
  for (const value of values) {
    await select.selectOption(value);
    const language = await page.evaluate(() => (window as any).hunminFormatter.getLanguage());
    expect(language, value).not.toBe('plaintext');
  }
  await select.selectOption('rust');
  expect(await page.evaluate(() => (window as any).hunminFormatter.getLanguage())).toBe('rust');
  await expect(select.locator('option[value="rust"]')).toContainText('색 구분만');
  await expect(select.locator('option[value="python"]')).not.toContainText('색 구분만');
});

for (const [fence, code] of [['c#', 'class A { int b = 1; }'], ['csharp', 'class A { int b = 1; }'], ['python', 'def f(): return 1'], ['go', 'func main() { return }'], ['sql', 'SELECT a FROM t'], ['xml', '<a b="1"></a>'], ['c++', 'int main() { return 0; }'], ['c', 'int main() { return 0; }'], ['rust', 'fn main() { let a = 1; }'], ['scala', 'object A { val b = 1 }'], ['ruby', 'def a; end'], ['r', 'a <- function() { 1 }']] as const) {
  test(`미리보기에서 ${fence} 코드 블록에 색을 입힌다`, async ({ page }) => {
    await openEditor(page, `\`\`\`${fence}\n${code}\n\`\`\`\n`);
    await mode(page, 'preview');
    await expect.poll(() => page.locator('#preview pre code span[class*="mtk"]').evaluateAll(items => new Set(items.map(item => item.className)).size)).toBeGreaterThan(1);
  });
}

test('긴 언어 이름이 있어도 메모 하단 도구가 좁은 창을 넘지 않는다', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 480 });
  await openEditor(page);
  const select = (await page.locator('#code-language').boundingBox())!;
  const button = (await page.locator('#format-code').boundingBox())!;
  expect(select.x + select.width).toBeLessThanOrEqual(320);
  expect(button.x + button.width).toBeLessThanOrEqual(320);
});
