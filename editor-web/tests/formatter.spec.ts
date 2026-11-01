import { test, expect, type Page } from '@playwright/test';

declare global { interface Window { hunminFormatter: { getValue(): string; setValue(text: string): void; getLanguage(): string } } }

async function open(page: Page) {
  await page.goto('/formatter.html');
  await page.waitForFunction(() => Boolean(window.hunminFormatter));
}
const rawValue = (page: Page) => page.evaluate(() => window.hunminFormatter.getValue());
// 줄바꿈이 없는 입력의 줄바꿈 형식은 운영체제 기본값을 따르므로 내용만 비교합니다.
const value = async (page: Page) => (await rawValue(page)).replaceAll('\r\n', '\n');
async function formatAs(page: Page, language: string, text: string) {
  await page.getByLabel('언어').selectOption(language);
  await page.evaluate(source => window.hunminFormatter.setValue(source), text);
  await page.getByRole('button', { name: '포맷', exact: true }).click();
}

for (const [language, source, expected] of [
  ['json', '{"a":1,"b":[2,3]}', '{ "a": 1, "b": [2, 3] }\n'],
  ['java', 'class Main{public static void main(String[] args){System.out.println("한글");}}', 'class Main {\n\n  public static void main(String[] args) {\n    System.out.println("한글");\n  }\n}\n'],
  ['js', 'const a={b:1};', 'const a = { b: 1 };\n'],
  ['ts', 'const a:number=1;', 'const a: number = 1;\n'],
  ['yaml', 'a: [1,2]', 'a: [1, 2]\n'],
  ['css', 'a{color:red}', 'a {\n  color: red;\n}\n'],
] as const) {
  test(`${language} 소스 전체를 포맷한다`, async ({ page }) => {
    await open(page);
    await formatAs(page, language, source);
    await expect.poll(() => value(page)).toBe(expected);
    await expect(page.locator('#formatter-status')).toContainText('포맷했습니다');
  });
}

test('포맷할 수 있는 언어만 고를 수 있고 Markdown은 없다', async ({ page }) => {
  await open(page);
  const values = await page.locator('#formatter-language option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
  expect(values).toEqual(['angular', 'css', 'flow', 'graphql', 'handlebars', 'html', 'java', 'js', 'json', 'json5', 'jsx', 'less', 'scss', 'tsx', 'ts', 'vue', 'yaml']);
});

test('메모 기능 없이 Monaco와 포맷 도구만 표시한다', async ({ page }) => {
  await open(page);
  await expect(page.locator('#formatter-code .monaco-editor')).toBeVisible();
  for (const selector of ['.format-toolbar', '#preview', '#preview-mode', '#capture', '#save-pdf', '#new-diagram', '#note-images']) await expect(page.locator(selector)).toHaveCount(0);
  await expect(page.locator('main#formatter button:visible')).toHaveCount(1);
});

test('언어를 고르면 그 언어로 색을 구분한다', async ({ page }) => {
  await open(page);
  await page.getByLabel('언어').selectOption('java');
  expect(await page.evaluate(() => window.hunminFormatter.getLanguage())).toBe('java');
  await page.evaluate(() => window.hunminFormatter.setValue('public class Main {}'));
  await expect(page.locator('#formatter-code .view-line span span').first()).toHaveClass(/mtk/);
  await expect.poll(() => page.locator('#formatter-code .view-line span span').evaluateAll(items => new Set(items.map(item => item.className)).size)).toBeGreaterThan(1);
  await page.getByLabel('언어').selectOption('vue');
  expect(await page.evaluate(() => window.hunminFormatter.getLanguage())).toBe('html');
});

test('문법 오류면 원문을 그대로 두고 이유를 알린다', async ({ page }) => {
  await open(page);
  await formatAs(page, 'json', '{"a":');
  await expect(page.locator('#formatter-status')).toContainText('포맷하지 못했습니다');
  expect(await value(page)).toBe('{"a":');
});

test('빈 편집기는 포맷하지 않는다', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: '포맷', exact: true }).click();
  await expect(page.locator('#formatter-status')).toContainText('소스코드를 입력');
});

test('포맷은 한 번의 실행 취소로 되돌리고 단축키로도 실행한다', async ({ page }) => {
  await open(page);
  await page.getByLabel('언어').selectOption('json');
  await page.locator('#formatter-code .monaco-editor').click();
  await page.keyboard.insertText('{"a":1}');
  await page.keyboard.press('Control+Alt+f');
  await expect.poll(() => value(page)).toBe('{ "a": 1 }\n');
  await page.keyboard.press('Control+z');
  await expect.poll(() => value(page)).toBe('{"a":1}');
  await page.keyboard.press('Shift+Alt+f');
  await expect.poll(() => value(page)).toBe('{ "a": 1 }\n');
});

test('입력의 줄바꿈 형식을 유지한다', async ({ page }) => {
  await open(page);
  await formatAs(page, 'css', 'a{color:red}\r\nb{color:blue}\r\n');
  await expect.poll(() => rawValue(page)).toBe('a {\r\n  color: red;\r\n}\r\nb {\r\n  color: blue;\r\n}\r\n');
  await formatAs(page, 'css', 'a{color:red}\nb{color:blue}\n');
  await expect.poll(() => rawValue(page)).toBe('a {\n  color: red;\n}\nb {\n  color: blue;\n}\n');
});

test('포맷 중에 입력이 바뀌면 입력을 덮어쓰지 않는다', async ({ page }) => {
  await open(page);
  await page.getByLabel('언어').selectOption('java');
  await page.evaluate(() => {
    window.hunminFormatter.setValue('class A{}');
    (document.querySelector('#formatter-format') as HTMLButtonElement).click();
    window.hunminFormatter.setValue('새 입력');
  });
  await expect(page.locator('#formatter-status')).toContainText('입력이 바뀌어');
  expect(await value(page)).toBe('새 입력');
});

test('찾기, 명령 팔레트, 여러 커서 등 Monaco 기능을 쓴다', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.hunminFormatter.setValue('alpha beta alpha'));
  await page.locator('#formatter-code .monaco-editor').click();
  await page.keyboard.press('Control+f');
  await expect(page.locator('#formatter-code .find-widget.visible')).toBeVisible();
  await page.keyboard.press('Escape');
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Control+d');
  await page.keyboard.press('Control+d');
  await page.keyboard.insertText('x');
  await expect.poll(() => value(page)).toBe('x beta x');
  await page.keyboard.press('F1');
  await expect(page.locator('.quick-input-widget')).toBeVisible();
});

test('고른 언어와 글꼴 설정을 따른다', async ({ page }) => {
  await open(page);
  await page.getByLabel('언어').selectOption('java');
  await page.evaluate(() => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify({ font: 'system', ligatures: false, size: 20, editor: { lineNumbers: false, minimap: true } })));
  await page.reload();
  await page.waitForFunction(() => Boolean(window.hunminFormatter));
  await expect(page.getByLabel('언어')).toHaveValue('java');
  await expect(page.locator('#formatter-code .view-lines')).toHaveCSS('font-size', '18px');
  await expect(page.locator('#formatter-code .margin .line-numbers')).toHaveCount(0);
  await expect(page.locator('#formatter-code .minimap canvas').first()).toBeVisible();
});

test('좁은 창에서도 도구가 넘치지 않고 편집기가 남은 공간을 채운다', async ({ page }) => {
  await page.setViewportSize({ width: 420, height: 320 });
  await open(page);
  const scroll = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }));
  expect(scroll.width).toBeLessThanOrEqual(420);
  expect(scroll.height).toBeLessThanOrEqual(320);
  const editor = (await page.locator('#formatter-code').boundingBox())!;
  expect(editor.height).toBeGreaterThan(200);
  for (const selector of ['#formatter-language', '#formatter-format']) {
    const box = (await page.locator(selector).boundingBox())!;
    expect(box.x + box.width).toBeLessThanOrEqual(420);
  }
});
