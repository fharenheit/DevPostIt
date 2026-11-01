import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode, replaceText } from './helpers';

const markdown = async (page: Page) => (await draft(page)).markdown;
async function select(page: Page, text: string) {
  await openEditor(page);
  await replaceText(page, text);
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Shift+End');
}

for (const [keys, expected] of [['Control+b', '**본문**'], ['Control+i', '*본문*'], ['Control+Shift+x', '~~본문~~'], ['Control+e', '`본문`'], ['Control+1', '# 본문'], ['Control+2', '## 본문'], ['Control+3', '### 본문']] as const) {
  test(`${keys} 단축키로 서식을 넣는다`, async ({ page }) => {
    await select(page, '본문');
    await page.keyboard.press(keys);
    expect(await markdown(page)).toBe(expected);
  });
}

test('Ctrl+K로 링크를 넣는다', async ({ page }) => {
  await select(page, '문서');
  await page.keyboard.press('Control+k');
  expect(await markdown(page)).toBe('[문서](url)');
});

test('미리보기의 체크박스를 누르면 원문의 해당 줄만 바꾸고 한 번에 되돌린다', async ({ page }) => {
  const source = '# 할 일\n\n- [ ] 첫째\n- [x] 둘째\n\n```\n- [ ] 코드 안\n```\n';
  await openEditor(page, source);
  await mode(page, 'preview');
  const boxes = page.locator('#preview input[type=checkbox]');
  await expect(boxes).toHaveCount(2);
  await boxes.nth(0).click();
  await expect.poll(() => markdown(page)).toBe(source.replace('- [ ] 첫째', '- [x] 첫째'));
  await expect(page.locator('#preview input[type=checkbox]').nth(0)).toBeChecked();
  await page.locator('#preview input[type=checkbox]').nth(1).click();
  await expect.poll(() => markdown(page)).toBe(source.replace('- [ ] 첫째', '- [x] 첫째').replace('- [x] 둘째', '- [ ] 둘째'));
  await expect(page.locator('#preview pre')).toContainText('- [ ] 코드 안');
  await mode(page, 'source');
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press('Control+z');
  await expect.poll(() => markdown(page)).toBe(source.replace('- [ ] 첫째', '- [x] 첫째'));
});

test('미리보기 코드 블록의 복사 버튼은 하이라이트 전 원문을 복사한다', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  const code = 'function a() {\n  return "메모  두칸";\n}';
  await openEditor(page, `\`\`\`ts\n${code}\n\`\`\`\n`);
  await mode(page, 'preview');
  await expect(page.locator('#preview pre code span').first()).toBeAttached();
  await page.locator('#preview pre').hover();
  await page.getByRole('button', { name: '코드 복사', exact: true }).click();
  await expect(page.getByRole('button', { name: '코드 복사', exact: true })).toHaveText('복사됨');
  // Windows 클립보드는 줄바꿈을 CRLF로 바꿉니다.
  expect((await page.evaluate(() => navigator.clipboard.readText())).replaceAll('\r\n', '\n')).toBe(code);
  expect((await draft(page)).dirty).toBe(false);
});

const assetRef = '../assets/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png';
test('앱이 저장한 이미지만 표시하고 다른 이미지는 요청하지 않는다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (/example\.com/.test(request.url())) requests.push(request.url()); });
  await openEditor(page, `![붙임](${assetRef})\n\n![외부](https://example.com/a.png)\n\n![경로](../../secret/a.png)\n`);
  await mode(page, 'preview');
  await expect(page.locator('#preview img')).toHaveCount(1);
  await expect(page.locator('#preview img')).toHaveAttribute('src', 'https://devpostit-assets.local/0123456789abcdef0123456789abcdef/fedcba9876543210fedcba9876543210.png');
  await expect(page.locator('#preview')).toContainText('[이미지: 외부]');
  await expect(page.locator('#preview')).toContainText('[이미지: 경로]');
  expect(requests).toEqual([]);
});

const id = '018f5df0-7e6c-7ad0-a8c1-9b67490da11b';
async function hosted(page: Page) {
  await page.addInitScript(() => {
    const messages: unknown[] = [];
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, {
      chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } },
      hostMessages: messages,
      fromHost: (message: unknown) => receive?.({ data: message }),
    });
  });
  await page.goto('/');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
}
const send = (page: Page, type: string, revision: number, payload: unknown) => page.evaluate(message => (window as any).fromHost(message), { protocolVersion: 1, type, requestId: 'host-1', noteId: id, revision, payload });
const sent = (page: Page) => page.evaluate(() => (window as any).hostMessages as any[]);

test('이미지만 붙여넣으면 앱에 요청하고 받은 Markdown만 넣는다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '' });
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.evaluate(() => {
    const data = new DataTransfer();
    data.items.add(new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }));
    document.querySelector('#source textarea')!.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  });
  await expect.poll(async () => (await sent(page)).at(-1)?.type).toBe('note.imagePasteRequested');
  await send(page, 'note.imageSaved', 7, { markdown: '<script>alert(1)</script>' });
  await send(page, 'note.imageSaved', 7, { markdown: '![이미지](https://example.com/a.png)' });
  expect(await markdown(page)).toBe('');
  await send(page, 'note.imageSaved', 7, { markdown: `![이미지](${assetRef})` });
  expect(await markdown(page)).toBe(`![이미지](${assetRef})`);
});

test('글자가 함께 있는 클립보드는 이미지로 처리하지 않는다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '' });
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.evaluate(() => {
    const data = new DataTransfer();
    data.setData('text/plain', '글자');
    data.items.add(new File([new Uint8Array([1])], 'a.png', { type: 'image/png' }));
    document.querySelector('#source textarea')!.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  });
  expect((await sent(page)).some(message => message.type === 'note.imagePasteRequested')).toBe(false);
});

test('항상 위와 색상을 앱에 알리고 앱이 보낸 값을 표시한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '본문' });
  await page.getByRole('button', { name: '항상 위', exact: true }).click();
  await expect.poll(async () => (await sent(page)).at(-1)).toMatchObject({ type: 'window.appearanceChanged', payload: { color: 'yellow', topmost: true } });
  await page.getByRole('button', { name: '메모 색상', exact: true }).click();
  await page.getByRole('button', { name: '파랑', exact: true }).click();
  await expect.poll(async () => (await sent(page)).at(-1)).toMatchObject({ type: 'window.appearanceChanged', payload: { color: 'blue', topmost: true } });
  await expect(page.locator('.note-header')).toHaveCSS('background-color', 'rgb(214, 234, 248)');
  await send(page, 'ui.appearance', 7, { color: 'green', topmost: false });
  await expect(page.locator('html')).toHaveAttribute('data-note-color', 'green');
  await expect(page.getByRole('button', { name: '항상 위', exact: true })).toHaveAttribute('aria-pressed', 'false');
  await send(page, 'ui.appearance', 7, { color: 'url(x)', topmost: false });
  await expect(page.locator('html')).toHaveAttribute('data-note-color', 'green');
  expect((await draft(page)).dirty).toBe(false);
});

test('Ctrl+Shift+F는 앱에 검색 창을 요청한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '본문' });
  await page.keyboard.press('Control+Shift+f');
  await expect.poll(async () => (await sent(page)).at(-1)?.type).toBe('window.searchRequested');
});

test('외부 변경으로 다시 불러와도 미리보기를 유지한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '원본' });
  await mode(page, 'preview');
  await send(page, 'note.load', 8, { markdown: '# 외부 변경' });
  await expect(page.locator('#preview h1')).toHaveText('외부 변경');
  await expect(page.locator('#preview-mode')).toHaveAttribute('aria-pressed', 'true');
});

async function hostedSearch(page: Page) {
  await page.addInitScript(() => {
    const messages: unknown[] = [];
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, {
      chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } },
      hostMessages: messages,
      fromHost: (message: unknown) => receive?.({ data: message }),
    });
  });
  await page.goto('/search.html');
}

test('검색 페이지는 결과를 글자로만 표시하고 메모 ID만 보낸다', async ({ page }) => {
  await hostedSearch(page);
  await expect.poll(async () => (await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'search.query', query: '' });
  await page.getByLabel('검색어').fill('메모');
  await expect.poll(async () => (await sent(page)).at(-1)?.query).toBe('메모');
  await page.evaluate(() => (window as any).fromHost({ type: 'search.results', query: '메모', results: [{ id: 'note-a', title: '<img src=x onerror="window.attacked=1">메모', excerpt: '앞 메모 뒤', updated: '2026-11-01T00:00:00Z' }] }));
  await expect(page.locator('#search-results li')).toHaveCount(1);
  await expect(page.locator('#search-results img')).toHaveCount(0);
  await expect(page.locator('#search-results mark').first()).toHaveText('메모');
  await page.locator('#search-results button').click();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'search.open', id: 'note-a' });
  expect(await page.evaluate(() => (window as any).attacked)).toBeUndefined();
});

test('이전 검색어의 늦은 결과는 표시하지 않는다', async ({ page }) => {
  await hostedSearch(page);
  await page.getByLabel('검색어').fill('새 검색');
  await page.evaluate(() => (window as any).fromHost({ type: 'search.results', query: '옛 검색', results: [{ id: 'a', title: '옛', excerpt: '', updated: '' }] }));
  await expect(page.locator('#search-results li')).toHaveCount(0);
});

test('브라우저 단독 실행의 검색 페이지는 앱이 필요하다고 알린다', async ({ page }) => {
  await page.goto('/search.html');
  await expect(page.locator('#search-status')).toContainText('앱에서');
});

test('설정 페이지는 앱이 보낸 상태를 표시하고 경로 없이 요청한다', async ({ page }) => {
  await page.addInitScript(() => {
    const messages: unknown[] = [];
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, {
      chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } },
      hostMessages: messages,
      fromHost: (message: unknown) => receive?.({ data: message }),
    });
  });
  await page.goto('/settings.html');
  await page.evaluate(() => (window as any).fromHost({ type: 'settings.state', dataFolder: 'C:\\data', pendingFolder: null, startInTray: true, canChangeFolder: true }));
  await expect(page.locator('#data-folder-path')).toHaveText('C:\\data');
  await expect(page.getByLabel('트레이로만 시작')).toBeChecked();
  await page.getByRole('button', { name: '폴더 변경', exact: true }).click();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.changeDataFolder' });
  await page.getByLabel('트레이로만 시작').uncheck();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.startInTray', value: false });
});

test('PDF 저장은 미리보기로 바꿔 요청하고 끝나면 원래 보기로 돌아온다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '# 제목\n\n본문' });
  await page.getByRole('button', { name: 'PDF로 저장', exact: true }).click();
  await expect.poll(async () => (await sent(page)).at(-1)?.type).toBe('note.exportPdfRequested');
  await expect(page.locator('#preview h1')).toHaveText('제목');
  await expect(page.getByRole('button', { name: 'PDF로 저장', exact: true })).toBeDisabled();
  await send(page, 'note.pdfFinished', 7, {});
  await expect(page.locator('#source-mode')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'PDF로 저장', exact: true })).toBeEnabled();
  expect((await draft(page)).dirty).toBe(false);
});

test('PDF 저장 후에도 미리보기에서 시작했으면 미리보기를 유지한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '본문' });
  await mode(page, 'preview');
  await page.getByRole('button', { name: 'PDF로 저장', exact: true }).click();
  await expect.poll(async () => (await sent(page)).at(-1)?.type).toBe('note.exportPdfRequested');
  await send(page, 'note.pdfFinished', 7, {});
  await expect(page.locator('#preview-mode')).toHaveAttribute('aria-pressed', 'true');
});

test('인쇄 화면은 제목과 본문만 남기고 긴 문서를 자르지 않는다', async ({ page }) => {
  const body = Array.from({ length: 200 }, (_, index) => `문단 ${index}`).join('\n\n');
  await openEditor(page, `# 긴 문서\n\n${body}\n\n\`\`\`ts\nconst a = 1;\n\`\`\`\n`);
  await mode(page, 'preview');
  await page.emulateMedia({ media: 'print' });
  for (const selector of ['.note-actions', '.modes', '.format-toolbar', '.note-bar', '.copy-code']) await expect(page.locator(selector).first()).toBeHidden();
  await expect(page.locator('h1').first()).toBeVisible();
  await expect(page.locator('.workspace')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  const height = await page.evaluate(() => document.documentElement.scrollHeight);
  expect(height).toBeGreaterThan(3000);
  const pdf = await page.pdf({ printBackground: true });
  expect(pdf.subarray(0, 4).toString()).toBe('%PDF');
});

test('설정 페이지의 백업·복원·업데이트 요청에는 경로가 없다', async ({ page }) => {
  await page.addInitScript(() => {
    const messages: unknown[] = [];
    let receive: ((event: { data: unknown }) => void) | undefined;
    Object.assign(window, {
      chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_type: string, handler: typeof receive) => { receive = handler; } } },
      hostMessages: messages,
      fromHost: (message: unknown) => receive?.({ data: message }),
    });
  });
  await page.goto('/settings.html');
  await page.evaluate(() => (window as any).fromHost({ type: 'settings.state', dataFolder: 'C:\\data', startInTray: false, canChangeFolder: true, autoUpdate: true, canUpdate: true, version: '0.1.0' }));
  await expect(page.locator('#app-version')).toHaveText('버전 0.1.0');
  await page.getByRole('button', { name: '백업 만들기', exact: true }).click();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.backup' });
  await page.getByRole('button', { name: '복원', exact: true }).click();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.restore' });
  await page.getByRole('button', { name: '지금 확인', exact: true }).click();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.checkUpdate' });
  await page.getByLabel('자동으로 업데이트 확인').uncheck();
  expect((await sent(page)).at(-1)).toEqual({ protocolVersion: 1, type: 'settings.autoUpdate', value: false });
  await page.evaluate(() => (window as any).fromHost({ type: 'settings.status', message: '메모 3개를 백업했습니다' }));
  await expect(page.locator('#settings-status')).toHaveText('메모 3개를 백업했습니다');
});
