import { test, expect, type Page } from '@playwright/test';
import { openEditor, mode, openSettings, draft, appPrimary } from './helpers';

const css = (page: Page, selector: string, property: string) => page.locator(selector).first().evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property);
const preferences = { font: 'd2', ligatures: true, size: 15 };
const seed = (page: Page, value: object) => page.addInitScript(item => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify(item)), value);

test('미리보기 코드도 선택한 편집 글꼴과 합자 설정을 따른다', async ({ page }) => {
  await openEditor(page, '```ts\nconst a = 1;\n```\n');
  await mode(page, 'preview');
  expect(await css(page, '#preview code', 'font-family')).toMatch(/^"?D2 Ligature"?/);
  const settings = await openSettings(page);
  await settings.getByLabel('편집 글꼴').selectOption('jetbrains');
  await expect.poll(() => css(page, '#preview code', 'font-family')).toMatch(/^"?JetBrains Mono"?/);
  await settings.getByLabel('합자 사용').uncheck();
  await expect.poll(() => css(page, '#preview code', 'font-variant-ligatures')).toBe('none');
});

test('굵은 글꼴을 포함하고 편집기 제목이 실제로 굵게 표시된다', async ({ page }) => {
  await openEditor(page, '# 제목 Bold\n\n본문 regular');
  await page.evaluate(async () => { await document.fonts.load('700 16px "D2 Ligature"', '가A'); await document.fonts.load('700 16px "JetBrains Mono"', 'A'); });
  const faces = await page.evaluate(() => [...document.fonts].filter(font => font.status === 'loaded' && font.weight === '700').map(font => font.family));
  expect(faces.join()).toContain('D2 Ligature');
  expect(faces.join()).toContain('JetBrains Mono');
  await expect(page.locator('#source .view-line span.mtkb').first()).toHaveCSS('font-weight', '700');
  await expect(page.locator('html')).toHaveCSS('font-synthesis-weight', 'none');
  await expect(page.locator('html')).toHaveCSS('font-synthesis-style', 'auto');
});

test('JetBrains Mono와 Consolas에서 한글 폭이 라틴 글자의 2배에 맞는다', async ({ page }) => {
  await openEditor(page);
  // Consolas는 Windows 글꼴입니다.
  for (const [stack, face] of [['"JetBrains Mono", "D2 Hangul 120", monospace', 'D2 Hangul 120'], ...(process.platform === 'darwin' ? [] : [['Consolas, "D2 Hangul 110", monospace', 'D2 Hangul 110'], ['"D2 Ligature", monospace', 'D2 Ligature']])]) {
    const ratio = await page.evaluate(async ({ stack, face }) => {
      await document.fonts.load(`100px "${face}"`, '가');
      await document.fonts.load('100px "JetBrains Mono"', 'a');
      const context = document.createElement('canvas').getContext('2d')!;
      context.font = `100px ${stack}`;
      return context.measureText('가').width / context.measureText('a').width;
    }, { stack, face });
    expect(ratio, stack).toBeCloseTo(2, 1);
  }
});

test('편집기 크기를 전체 크기와 따로 정하고 초기화할 수 있다', async ({ page }) => {
  await openEditor(page);
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '편집기 글자 크게', exact: true }).click();
  await expect(settings.getByLabel('편집기 글자 크기 값')).toHaveText('14');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '14px');
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('16px');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '14px');
  await settings.getByRole('button', { name: '기본값으로', exact: true }).click();
  await expect(settings.getByLabel('기준 글자 크기')).toHaveText('15');
  await expect(settings.getByLabel('편집기 글자 크기 값')).toHaveText('13');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  expect((await draft(page)).dirty).toBe(false);
});

test('줄 간격을 편집기와 미리보기에 함께 적용한다', async ({ page }) => {
  await openEditor(page, '본문');
  const before = await page.locator('#source .view-line').first().evaluate(element => element.getBoundingClientRect().height);
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '줄 간격 넓게', exact: true }).click();
  await settings.getByRole('button', { name: '줄 간격 넓게', exact: true }).click();
  await expect(settings.getByLabel('줄 간격 값')).toHaveText('1.9');
  await expect.poll(() => page.locator('#source .view-line').first().evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThan(before);
  await mode(page, 'preview');
  const value = await page.locator('#preview').evaluate(element => parseFloat(getComputedStyle(element).lineHeight) / parseFloat(getComputedStyle(element).fontSize));
  expect(value).toBeCloseTo(1.9, 1);
  for (let index = 0; index < 20; index++) if (await settings.getByRole('button', { name: '줄 간격 넓게', exact: true }).isEnabled()) await settings.getByRole('button', { name: '줄 간격 넓게', exact: true }).click();
  await expect(settings.getByLabel('줄 간격 값')).toHaveText('2.2');
});

for (const [keys, expected] of [['=', '16px'], ['-', '14px']] as const) {
  test(`Ctrl+${keys}로 글자 크기를 바꾸고 저장한다`, async ({ page }) => {
    await openEditor(page, '본문');
    await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
    await page.keyboard.press(`${appPrimary}+${keys}`);
    await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe(expected);
    expect((await draft(page)).markdown).toBe('본문');
    await page.reload();
    await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe(expected);
  });
}

test('Ctrl+0은 전체 크기를 기본값으로 되돌리고 범위를 넘지 않는다', async ({ page }) => {
  await seed(page, { ...preferences, size: 24 });
  await openEditor(page, '본문');
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press(`${appPrimary}+=`);
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('24px');
  await page.keyboard.press(`${appPrimary}+0`);
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('15px');
});

test('F1에 창마다 어긋나는 글꼴 확대 명령이 없다', async ({ page }) => {
  await openEditor(page, '본문');
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press('F1');
  await page.keyboard.insertText('Font Zoom');
  await page.waitForTimeout(300);
  await expect(page.locator('.quick-input-list .monaco-list-row')).toHaveCount(0);
});

test('합자가 없는 Consolas에서는 합자 설정을 끈다', async ({ page }) => {
  await page.goto('/settings.html');
  await page.getByLabel('편집 글꼴').selectOption('system');
  await expect(page.getByLabel('합자 사용')).toBeDisabled();
  await page.getByLabel('편집 글꼴').selectOption('jetbrains');
  await expect(page.getByLabel('합자 사용')).toBeEnabled();
});

test('설정 창 예시가 선택한 글꼴을 따른다', async ({ page }) => {
  await page.goto('/settings.html');
  await expect(page.locator('#font-sample')).toContainText('=>');
  expect(await css(page, '#font-sample', 'font-family')).toMatch(/^"?D2 Ligature"?/);
  await page.getByLabel('편집 글꼴').selectOption('jetbrains');
  expect(await css(page, '#font-sample', 'font-family')).toMatch(/^"?JetBrains Mono"?/);
  await expect(page.locator('#font-sample')).toHaveCSS('font-size', '14px');
});

async function hostedSettings(page: Page) {
  await page.addInitScript(() => {
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, { chrome: { webview: { postMessage: () => undefined, addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } }, fromHost: (message: unknown) => receive?.({ data: message }) });
  });
  await page.goto('/settings.html');
}

test('앱이 보낸 설치 글꼴을 고르면 메모와 본문에 적용하고 위험한 이름은 버린다', async ({ page }) => {
  await openEditor(page, '```ts\nconst a = 1;\n```\n');
  const settings = await page.context().newPage();
  await hostedSettings(settings);
  await settings.evaluate(() => (window as any).fromHost({ type: 'settings.fonts', monospace: ['Cascadia Code', 'bad"; color:red'], all: ['Cascadia Code', '맑은 고딕', 'x</style>'] }));
  await expect(settings.getByLabel('편집 글꼴').locator('option[value="custom:Cascadia Code"]')).toHaveCount(1);
  await expect(settings.locator('#font option')).toHaveCount(4);
  await expect(settings.locator('#body-font option')).toHaveCount(3);
  await settings.getByLabel('편집 글꼴').selectOption('custom:Cascadia Code');
  await settings.getByLabel('본문 글꼴').selectOption('맑은 고딕');
  await expect.poll(() => css(page, '.monaco-editor .view-lines', 'font-family')).toMatch(/^"?Cascadia Code"?/);
  await expect.poll(() => css(page, 'h1', 'font-family')).toMatch(/^"?맑은 고딕"?/);
  await settings.reload();
  await expect(settings.getByLabel('편집 글꼴')).toHaveValue('custom:Cascadia Code');
});

test('저장된 글꼴 설정의 잘못된 값은 기본값으로 읽는다', async ({ page }) => {
  await seed(page, { font: 'custom:x"; color:red', ligatures: true, size: 15 });
  await openEditor(page);
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-family', /^"?D2 Ligature"?/);
  await page.evaluate(() => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify({ font: 'd2', ligatures: true, size: 15, editorSize: 99, lineHeight: 50, bodyFont: 'a"b' })));
  await page.reload();
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  expect(await page.evaluate(() => document.documentElement.style.getPropertyValue('--text-line-height'))).toBe('1.7');
});

test('인쇄 글자는 설정 크기와 관계없이 일정하다', async ({ page }) => {
  await seed(page, { ...preferences, size: 24 });
  await openEditor(page, '본문');
  await mode(page, 'preview');
  await page.emulateMedia({ media: 'print' });
  await expect(page.locator('html')).toHaveCSS('font-size', /^14\.\d+px$/);
});

const id = '018f5df0-7e6c-7ad0-a8c1-9b67490da11b';
test('복원 때 앱이 보낸 글꼴 설정을 적용하고 손상된 값은 버린다', async ({ page }) => {
  await page.addInitScript(() => {
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, { chrome: { webview: { postMessage: () => undefined, addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } }, fromHost: (message: unknown) => receive?.({ data: message }) });
  });
  await page.goto('/');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  const send = (type: string, payload: unknown) => page.evaluate(message => (window as any).fromHost(message), { protocolVersion: 1, type, requestId: 'host', noteId: id, revision: 7, payload });
  await send('note.load', { markdown: '본문' });
  await send('ui.preferences', { value: 'not json' });
  expect(await page.evaluate(() => localStorage.getItem('devpostit.editor.preferences.v1'))).not.toBe('not json');
  await send('ui.preferences', { value: JSON.stringify({ font: 'jetbrains', ligatures: false, size: 18 }) });
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('18px');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-family', /^"?JetBrains Mono"?/);
});

const previewSize = (page: Page) => page.locator('#preview').evaluate(element => getComputedStyle(element).fontSize);

test('미리보기 크기는 따로 정하지 않으면 소스 편집 크기를 따른다', async ({ page }) => {
  await openEditor(page, '# 제목\n\n본문');
  await mode(page, 'preview');
  expect(await previewSize(page)).toBe('13px');
  const settings = await openSettings(page);
  await expect(settings.getByLabel('미리보기 글자 크기 값')).toHaveText('13');
  await settings.getByRole('button', { name: '편집기 글자 크게', exact: true }).click();
  await settings.getByRole('button', { name: '편집기 글자 크게', exact: true }).click();
  await expect(settings.getByLabel('미리보기 글자 크기 값')).toHaveText('15');
  await expect.poll(() => previewSize(page)).toBe('15px');
});

test('미리보기 크기를 정하면 소스 편집 크기와 따로 유지하고 기본값으로 되돌린다', async ({ page }) => {
  await openEditor(page, '# 제목\n\n본문\n\n```ts\nconst a = 1;\n```\n');
  await mode(page, 'preview');
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '미리보기 글자 크게', exact: true }).click();
  await settings.getByRole('button', { name: '미리보기 글자 크게', exact: true }).click();
  await settings.getByRole('button', { name: '미리보기 글자 크게', exact: true }).click();
  await expect.poll(() => previewSize(page)).toBe('16px');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  await settings.getByRole('button', { name: '편집기 글자 크게', exact: true }).click();
  await expect(settings.getByLabel('미리보기 글자 크기 값')).toHaveText('16');
  await expect(page.locator('#preview h1')).toHaveCSS('font-size', '24px');
  const code = await page.locator('#preview code').evaluate(element => parseFloat(getComputedStyle(element).fontSize));
  expect(code).toBeGreaterThan(14);
  await page.reload();
  await mode(page, 'preview');
  expect(await previewSize(page)).toBe('16px');
  await settings.getByRole('button', { name: '기본값으로', exact: true }).click();
  await expect(settings.getByLabel('미리보기 글자 크기 값')).toHaveText('13');
  await expect.poll(() => previewSize(page)).toBe('13px');
});

test('미리보기 크기는 범위를 넘지 않고 잘못 저장된 값은 무시한다', async ({ page }) => {
  await seed(page, { ...preferences, previewSize: 99 });
  await openEditor(page, '본문');
  await mode(page, 'preview');
  expect(await previewSize(page)).toBe('13px');
  const settings = await openSettings(page);
  for (let index = 0; index < 5; index++) await settings.getByRole('button', { name: '미리보기 글자 작게', exact: true }).click();
  await expect(settings.getByLabel('미리보기 글자 크기 값')).toHaveText('8');
  await expect(settings.getByRole('button', { name: '미리보기 글자 작게', exact: true })).toBeDisabled();
});

test('전체 글자 크기를 바꿔도 메모 머리글만 바뀌고 미리보기는 소스 편집 크기를 따른다', async ({ page }) => {
  await openEditor(page, '본문');
  await mode(page, 'preview');
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect.poll(() => previewSize(page)).toBe('14px');
  await expect(page.locator('#source-mode')).toHaveCSS('font-size', '14px');
});

test('설정의 백업 버튼 이름은 백업이다', async ({ page }) => {
  await page.addInitScript(() => Object.assign(window, { chrome: { webview: { postMessage: () => undefined, addEventListener: () => undefined } } }));
  await page.goto('/settings.html');
  await expect(page.getByRole('button', { name: '백업', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '백업 만들기', exact: true })).toHaveCount(0);
});
