import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

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

test('미리보기에서 이미지를 눌러 크기를 고르면 원문의 그 이미지만 바꾸고 한 번에 되돌린다', async ({ page }) => {
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

test('이미지 크기 메뉴는 Esc와 바깥 클릭으로 닫고 원문을 바꾸지 않는다', async ({ page }) => {
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
