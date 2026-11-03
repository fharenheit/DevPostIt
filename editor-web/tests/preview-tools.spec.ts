import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode, assetOrigin } from './helpers';

const a = 'a'.repeat(32), b = 'b'.repeat(32), c = 'c'.repeat(32);
const image = (alt: string, file = b) => `![${alt}](../assets/${a}/${file}.png)`;
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
const noteId = '11111111-1111-1111-1111-111111111111';
const send = (page: Page, type: string, revision: number, payload: object) => page.evaluate(message => (window as any).fromHost(message), { protocolVersion: 1, type, requestId: 'r', noteId, revision, payload });
const sent = (page: Page) => page.evaluate(() => (window as any).hostMessages) as Promise<any[]>;

test('미리보기의 웹 링크를 누르면 페이지를 옮기지 않고 앱에 열기를 요청한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '[문서](https://example.com/a?b=1) <mailto:me@example.com>' });
  await mode(page, 'preview');
  const url = page.url();
  await page.locator('#preview a', { hasText: '문서' }).click();
  await expect.poll(async () => (await sent(page)).at(-1)).toMatchObject({ type: 'note.openLinkRequested', revision: 7, payload: { url: 'https://example.com/a?b=1' } });
  await page.locator('#preview a', { hasText: 'me@example.com' }).click();
  await expect.poll(async () => (await sent(page)).at(-1)?.payload.url).toBe('mailto:me@example.com');
  expect(page.url()).toBe(url);
});

test('웹과 메일이 아닌 링크는 누를 수 없는 글자로 둔다', async ({ page }) => {
  await openEditor(page, '[파일](file:///C:/a.txt) [상대](a.md) [앱](ms-settings:privacy) [스크립트](javascript:alert(1)) [정상](https://example.com)');
  await mode(page, 'preview');
  await expect(page.locator('#preview a')).toHaveCount(1);
  await expect(page.locator('#preview a')).toHaveAttribute('href', 'https://example.com');
  await expect(page.locator('#preview')).toContainText('파일');
  await expect(page.locator('#preview')).toContainText('상대');
});

test('브라우저 단독 실행의 링크는 앱이 필요하다고 알린다', async ({ page }) => {
  await openEditor(page, '[문서](https://example.com)');
  await mode(page, 'preview');
  const url = page.url();
  await page.locator('#preview a').click();
  await expect(page.locator('#host-status')).toContainText('링크는 앱에서');
  expect(page.url()).toBe(url);
});

test('이미지 대체 글자 뒤의 크기를 표시 폭으로 쓰고 글자에서는 뺀다', async ({ page }) => {
  await openEditor(page, [image('이미지|50%'), image('작게|300', c), image('잘못|999%'), image('넓게|9')].join('\n\n'));
  await mode(page, 'preview');
  const images = page.locator('#preview img');
  await expect(images.nth(0)).toHaveAttribute('width', '50%');
  await expect(images.nth(0)).toHaveAttribute('alt', '이미지');
  await expect(images.nth(1)).toHaveAttribute('width', '300');
  await expect(images.nth(2)).not.toHaveAttribute('width', /.*/);
  await expect(images.nth(2)).toHaveAttribute('alt', '잘못|999%');
  await expect(images.nth(3)).not.toHaveAttribute('width', /.*/);
});

test('미리보기에서 이미지를 눌러 크기를 고르면 원문의 그 이미지만 바꾸고 한 번에 되돌린다', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', 'Playwright WebKit에서 가로챈 이미지가 표시되지 않아 누를 수 없습니다. 실제 WKWebView 동작은 MacSmoke가 확인합니다.');
  const text = `앞\n${image('첫째')} ${image('둘째', c)}\n\n뒤`;
  await openEditor(page, text);
  await mode(page, 'preview');
  await page.locator('#preview img').nth(1).click();
  await expect(page.getByRole('group', { name: '이미지 크기' })).toBeVisible();
  await page.getByRole('button', { name: '50%', exact: true }).click();
  expect((await draft(page)).markdown).toBe(`앞\n${image('첫째')} ${image('둘째|50%', c)}\n\n뒤`);
  await expect(page.locator('#preview img').nth(1)).toHaveAttribute('width', '50%');
  await expect(page.getByRole('group', { name: '이미지 크기' })).toBeHidden();
  await page.locator('#preview img').nth(1).click();
  await page.getByRole('button', { name: '원래 크기', exact: true }).click();
  expect((await draft(page)).markdown).toBe(text);
  await expect(page.locator('#preview img').nth(1)).not.toHaveAttribute('width', /.*/);
});

test('이미지 크기 메뉴는 Esc와 바깥 클릭으로 닫고 원문을 바꾸지 않는다', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', 'Playwright WebKit에서 가로챈 이미지가 표시되지 않아 누를 수 없습니다. 실제 WKWebView 동작은 MacSmoke가 확인합니다.');
  const text = `${image('그림')}\n\n본문`;
  await openEditor(page, text);
  await mode(page, 'preview');
  const menu = page.getByRole('group', { name: '이미지 크기' });
  await page.locator('#preview img').click();
  await expect(menu).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
  await page.locator('#preview img').click();
  await page.locator('#preview p').last().click();
  await expect(menu).toBeHidden();
  expect((await draft(page)).markdown).toBe(text);
});

test('원문이 바뀌어 이미지를 찾지 못하면 크기 변경을 하지 않는다', async ({ page }) => {
  const text = image('그림');
  await openEditor(page, text);
  await mode(page, 'preview');
  await page.evaluate(() => (window as any).hunminEditor.resizeImage(50, 51, 0, 'x.png', '25%'));
  await page.evaluate(() => (window as any).hunminEditor.resizeImage(0, 1, 0, 'x.png', '25%'));
  expect((await draft(page)).markdown).toBe(text);
});

const diagram = (name = b) => `![다이어그램](../assets/${a}/${name}.drawio.svg)`;

test('Mermaid 코드 블록을 미리보기에서 그림으로 그리고 원문은 그대로 둔다', async ({ page }) => {
  const text = '앞\n\n```mermaid\nflowchart TD\n  가[시작] --> 나[끝]\n```\n';
  await openEditor(page, text);
  await mode(page, 'preview');
  const figure = page.locator('#preview figure.mermaid-diagram svg');
  await expect(figure).toBeVisible();
  await expect(figure).toContainText('시작');
  await expect(figure).toContainText('끝');
  await expect(page.locator('#preview pre')).toHaveCount(0);
  expect((await draft(page)).markdown).toBe(text);
  await mode(page, 'source');
  await mode(page, 'preview');
  await expect(figure).toContainText('시작');
});

test('Mermaid 문법 오류는 원문과 오류를 함께 보여 준다', async ({ page }) => {
  await openEditor(page, '```mermaid\nflowchart TD\n  가[[[ --> \n```\n');
  await mode(page, 'preview');
  await expect(page.locator('#preview .diagram-error')).toContainText('다이어그램을 그리지 못했습니다');
  await expect(page.locator('#preview pre code')).toContainText('flowchart TD');
  await expect(page.locator('body > [id^="dmermaid"]')).toHaveCount(0);
});

test('Mermaid의 HTML과 스크립트는 실행하지 않고 외부로 요청하지 않는다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!request.url().startsWith('http://127.0.0.1:')) requests.push(request.url()); });
  await openEditor(page, '```mermaid\nflowchart TD\n  가["<img src=https://example.com/x.png onerror=window.attacked=true>"] --> 나\n  click 나 href "javascript:window.attacked=true"\n```\n');
  await mode(page, 'preview');
  await expect(page.locator('#preview figure.mermaid-diagram svg')).toBeVisible();
  expect(await page.locator('#preview svg img, #preview svg script, #preview svg foreignObject, #preview svg [onerror], #preview svg a[href^="javascript"]').count()).toBe(0);
  expect(await page.evaluate(() => 'attacked' in window)).toBe(false);
  expect(requests).toEqual([]);
});

test('PDF 준비는 Mermaid 그림이 끝날 때까지 기다린다', async ({ page }) => {
  await openEditor(page, '```mermaid\nflowchart TD\n  가 --> 나\n```\n');
  expect(await page.evaluate(() => window.hunminEditor.renderForPrint())).toBe(true);
  await expect(page.locator('#preview figure.mermaid-diagram svg')).toHaveCount(1);
});

test('draw.io 이미지는 미리보기에 표시하고 다른 SVG는 요청하지 않는다', async ({ page }) => {
  await openEditor(page, `${diagram()}\n\n![나쁨](../assets/${a}/${b}.svg)`);
  await mode(page, 'preview');
  await expect(page.locator('#preview img')).toHaveCount(1);
  await expect(page.locator('#preview img')).toHaveAttribute('src', new RegExp(`^${assetOrigin.replace(/[./]/g, '\\$&')}/${a}/${b}\\.drawio\\.svg`));
  await expect(page.locator('#preview')).toContainText('[이미지: 나쁨]');
});

test('머리글 버튼으로 draw.io 다이어그램을 요청하고 앱이 보낸 그림만 넣는다', async ({ page }) => {
  await page.setViewportSize({ width: 480, height: 640 });
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '' });
  await page.getByRole('button', { name: 'draw.io 다이어그램 추가', exact: true }).click();
  await expect.poll(async () => (await sent(page)).at(-1)).toMatchObject({ type: 'note.newDiagramRequested', revision: 7, payload: {} });
  await send(page, 'note.imageSaved', 7, { markdown: '![다이어그램](https://example.com/a.drawio.svg)' });
  expect((await draft(page)).markdown).toBe('');
  await send(page, 'note.imageSaved', 7, { markdown: diagram() });
  await expect.poll(async () => (await draft(page)).markdown).toBe(diagram());
});

test('브라우저 단독 실행의 다이어그램 버튼은 앱이 필요하다고 알린다', async ({ page }) => {
  await openEditor(page, '');
  await page.locator('#new-diagram').click();
  await expect(page.locator('#host-status')).toContainText('다이어그램은 앱에서');
});

test('draw.io 이미지의 메뉴에서만 편집을 요청한다', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', 'Playwright WebKit에서 가로챈 이미지가 표시되지 않아 누를 수 없습니다. 실제 WKWebView 동작은 MacSmoke가 확인합니다.');
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: `${image('그림')}\n\n${diagram()}` });
  await mode(page, 'preview');
  const editButton = page.getByRole('button', { name: 'draw.io로 편집', exact: true });
  await page.locator('#preview img').nth(0).click();
  await expect(editButton).toBeHidden();
  await page.keyboard.press('Escape');
  await page.locator('#preview img').nth(1).click();
  await editButton.click();
  await expect.poll(async () => (await sent(page)).at(-1)).toMatchObject({ type: 'note.editDiagramRequested', payload: { file: `${a}/${b}.drawio.svg` } });
});

test('앱으로 돌아오면 draw.io 그림만 다시 읽어 저장한 내용을 보여 준다', async ({ page, browserName }) => {
  test.skip(browserName === 'webkit', 'Playwright WebKit에서 가로챈 이미지가 표시되지 않아 누를 수 없습니다. 실제 WKWebView 동작은 MacSmoke가 확인합니다.');
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: `${image('그림')}\n\n${diagram()}` });
  await mode(page, 'preview');
  const png = page.locator('#preview img').nth(0);
  const svg = page.locator('#preview img').nth(1);
  const [pngBefore, svgBefore] = [await png.getAttribute('src'), await svg.getAttribute('src')];
  await page.waitForTimeout(5);
  await send(page, 'note.assetsChanged', 7, {});
  await expect(svg).not.toHaveAttribute('src', svgBefore!);
  expect(await png.getAttribute('src')).toBe(pngBefore);
  await page.locator('#preview img').nth(1).click();
  await page.getByRole('button', { name: '50%', exact: true }).click();
  expect((await draft(page)).markdown).toContain(`![다이어그램|50%](../assets/${a}/${b}.drawio.svg)`);
});

test('허용하지 않은 주소의 링크와 이미지는 표시하지 않는다', async ({ page }) => {
  await openEditor(page, `[나쁨](devpostit://app/index.html) [자바](javascript:alert(1)) [앱](devpostit-assets://notes/${a}/${b}.png)\n\n![외부](devpostit-assets://other/${a}/${b}.png)`);
  await mode(page, 'preview');
  await expect(page.locator('#preview a')).toHaveCount(0);
  await expect(page.locator('#preview img')).toHaveCount(0);
  await expect(page.locator('#preview')).toContainText('[이미지: 외부]');
});

test('이미지의 크기와 원문 위치 속성을 미리보기에 유지한다', async ({ page }) => {
  await openEditor(page, `${image('그림|50%')}\n\n3. 셋째`);
  await mode(page, 'preview');
  const shown = page.locator('#preview img');
  await expect(shown).toHaveAttribute('width', '50%');
  await expect(shown).toHaveAttribute('data-line', '0');
  await expect(shown).toHaveAttribute('data-index', '0');
  await expect(page.locator('#preview ol')).toHaveAttribute('start', '3');
});
