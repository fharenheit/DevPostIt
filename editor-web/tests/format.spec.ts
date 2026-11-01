import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft } from './helpers';

async function atLine(page: Page, line: number) {
  await page.locator('#source .monaco-editor').click({ position: { x: 100, y: 40 } });
  await page.keyboard.press('Control+Home');
  for (let i = 1; i < line; i++) await page.keyboard.press('ArrowDown');
}

for (const [lang, code, expected] of [
  ['json', '{"a":1,"b":[2,3]}', '{ "a": 1, "b": [2, 3] }'],
  ['yaml', 'a: [1,2]\nb: {c: 3,d: 4}', 'a: [1, 2]\nb: { c: 3, d: 4 }'],
  ['java', 'class Main{public static void main(String[] args){System.out.println("한글");}}', 'class Main {\n\n  public static void main(String[] args) {\n    System.out.println("한글");\n  }\n}'],
  ['js', 'const a={b:1};', 'const a = { b: 1 };'],
  ['ts', 'const a:number=1;', 'const a: number = 1;'],
  ['html', '<div><span>메모</span></div>', '<div><span>메모</span></div>'],
  ['css', 'a{color:red;}', 'a {\n  color: red;\n}'],
]) test(`${lang} 코드만 정리하고 앞뒤 Markdown 보존`, async ({ page }) => {
  const prefix = `# 원문\n\n\`\`\`${lang}\n`;
  const suffix = '\n```\n\n뒤 원문\n';
  await openEditor(page, prefix + code + suffix);
  await atLine(page, 4);
  await page.getByRole('button', { name: '코드 정리', exact: true }).click();
  await expect(page.locator('#format-status')).toContainText('정리했습니다');
  expect((await draft(page)).markdown).toBe(prefix + expected + suffix);
  if (expected !== code) {
    await atLine(page, 4);
    await page.keyboard.press('Control+z');
    expect((await draft(page)).markdown).toBe(prefix + code + suffix);
  }
});

for (const [name, body] of [
  ['문법 오류', '```json\n{"a":}\n```'],
  ['미지원 언어', '```python\nx=1\n```'],
  ['미닫힌 블록', '```json\n{"a":1}'],
  ['인용 속 블록', '> ```json\n> {"a":1}\n> ```'],
]) test(`${name}: 정리 실패 시 원문 유지`, async ({ page }) => {
  await openEditor(page, body);
  await atLine(page, 2);
  await page.getByRole('button', { name: '코드 정리', exact: true }).click();
  await expect(page.locator('#format-status')).not.toBeEmpty();
  if (name === '인용 속 블록') await expect(page.locator('#format-status')).toContainText('최상위');
  if (name === '미지원 언어') await expect(page.locator('#format-status')).toContainText('선택');
  expect((await draft(page)).markdown).toBe(body);
});

test('언어 없는 틸드 블록을 선택 언어로 정리하고 CRLF 유지', async ({ page }) => {
  await openEditor(page, '앞\r\n~~~\r\n{"a":1}\r\n~~~\r\n뒤');
  await atLine(page, 3);
  await page.getByLabel('코드 언어').selectOption('json');
  await page.getByRole('button', { name: '코드 정리', exact: true }).click();
  await expect(page.locator('#format-status')).toContainText('정리했습니다');
  expect((await draft(page)).markdown).toBe('앞\r\n~~~\r\n{ "a": 1 }\r\n~~~\r\n뒤');
});

test('비동기 정리 중 메모를 전환하면 계산 결과를 적용하지 않는다', async ({ page }) => {
  await openEditor(page, '```java\nclass A{}\n```');
  await atLine(page, 2);
  await page.evaluate(async () => {
    const pending = window.hunminEditor.formatCodeBlock();
    window.hunminEditor.load('other', '다른 메모', 20);
    await pending;
  });
  expect((await draft(page)).markdown).toBe('다른 메모');
});

test('혼합 줄바꿈의 앞뒤 원문을 정리와 실행 취소에서 정확히 보존한다', async ({ page }) => {
  const original = '앞\r\n```json\n{"a":1}\n```\n뒤\n';
  await openEditor(page, original);
  await atLine(page, 3);
  await page.getByRole('button', { name: '코드 정리', exact: true }).click();
  await expect(page.locator('#format-status')).toContainText('정리했습니다');
  expect((await draft(page)).markdown).toBe('앞\r\n```json\n{ "a": 1 }\n```\n뒤\n');
  await atLine(page, 3);
  await page.keyboard.press('Control+z');
  expect((await draft(page)).markdown).toBe(original);
});

const languageOptions: [string, string, string][] = [
  ['json', '{"a":1}', '{ "a": 1 }'],
  ['json5', '{a:1}', '{ a: 1 }'],
  ['yaml', 'a: [1,2]', 'a: [1, 2]'],
  ['java', 'class A{}', 'class A {}'],
  ['js', 'const a={b:1};', 'const a = { b: 1 };'],
  ['jsx', 'const a=<div className="x"/>;', 'const a = <div className="x" />;'],
  ['flow', 'const a:number=1;', 'const a: number = 1;'],
  ['ts', 'const a:number=1;', 'const a: number = 1;'],
  ['tsx', 'const a:number=<b/>;', 'const a: number = <b />;'],
  ['html', '<div  class="a"></div>', '<div class="a"></div>'],
  ['vue', '<template><div/></template>', '<template><div /></template>'],
  ['angular', '<div  *ngIf="a"></div>', '<div *ngIf="a"></div>'],
  ['handlebars', '<div  class="a"></div>', '<div class="a"></div>'],
  ['css', 'a{color:red;}', 'a {\n  color: red;\n}'],
  ['scss', 'a{b{color:red;}}', 'a {\n  b {\n    color: red;\n  }\n}'],
  ['less', '@c:red;a{color:@c;}', '@c: red;\na {\n  color: @c;\n}'],
  ['graphql', 'query{a{b}}', 'query {\n  a {\n    b\n  }\n}'],
  ['markdown', '*a*', '_a_'],
  ['mdx', '*a*', '_a_'],
];

test('코드 언어 목록은 실제로 정리할 수 있는 언어만 보여 준다', async ({ page }) => {
  await openEditor(page);
  const values = await page.locator('#code-language option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
  expect(values[0]).toBe('');
  expect(values.slice(1).sort()).toEqual(languageOptions.map(([value]) => value).sort());
});

for (const [language, code, expected] of languageOptions) {
  test(`언어 표시가 없는 블록을 목록의 ${language}로 정리한다`, async ({ page }) => {
    const prefix = '```\n', suffix = '\n```\n';
    await openEditor(page, prefix + code + suffix);
    await atLine(page, 2);
    await page.getByLabel('코드 언어').selectOption(language);
    await page.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(page.locator('#format-status')).toContainText('정리했습니다');
    expect((await draft(page)).markdown).toBe(prefix + expected + suffix);
  });
}

test('블록에 적은 새 언어 이름으로도 정리한다', async ({ page }) => {
  const prefix = '```scss\n', suffix = '\n```\n';
  await openEditor(page, prefix + 'a{b{color:red;}}' + suffix);
  await atLine(page, 2);
  await page.getByRole('button', { name: '코드 정리', exact: true }).click();
  await expect(page.locator('#format-status')).toContainText('정리했습니다');
  expect((await draft(page)).markdown).toBe(prefix + 'a {\n  b {\n    color: red;\n  }\n}' + suffix);
});
