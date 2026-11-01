import { test, expect, type Page } from '@playwright/test';
import { replaceText, mode, draft, openSettings } from './helpers';

const id = '018f5df0-7e6c-7ad0-a8c1-9b67490da11b';
test('제목 더블클릭은 접기 대신 이름 편집을 연다', async ({ page }) => {
  await hosted(page);
  await send(page);
  await page.locator('h1').dblclick();
  await expect(page.getByLabel('메모 이름', { exact: true })).toBeVisible();
  expect((await messages(page)).some(message => message.type === 'window.collapseRequested')).toBe(false);
});

test('Ctrl+,는 호스트에 설정 창을 요청하고 메모 안에 대화상자를 띄우지 않는다', async ({ page }) => {
  await hosted(page);
  await send(page);
  await page.keyboard.press('Control+,');
  await expect.poll(async () => (await messages(page)).at(-1)).toMatchObject({ type: 'window.settingsRequested', revision: 7, payload: {} });
  await expect(page.getByRole('dialog', { name: '설정', exact: true })).toHaveCount(0);
  await send(page, 'ui.openSettings', 7, {});
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('설정 창에서 바꾼 글자 크기가 열린 메모에 적용되고 저장 메시지를 만들지 않는다', async ({ page }) => {
  await hosted(page);
  await send(page);
  const count = (await messages(page)).length;
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('16px');
  expect((await messages(page)).length).toBe(count);
});

test('메모 제목 로드·이름 변경·취소와 본문을 같은 리비전으로 전달한다', async ({ page }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '원본', title: '첫 이름' });
  await expect(page.locator('h1')).toHaveText('첫 이름');
  await page.locator('h1').dblclick();
  await page.getByLabel('메모 이름', { exact: true }).fill('  회의 기록  ');
  await page.getByRole('button', { name: '확인', exact: true }).click();
  await expect(page.locator('h1')).toHaveText('회의 기록');
  await expect.poll(async () => (await messages(page)).at(-1)?.payload.title).toBe('회의 기록');
  const change = (await messages(page)).at(-1);
  expect(change.payload.markdown).toBe('원본');
  expect(change.revision).toBe(8);
  await send(page, 'note.saved', 8, {});
  expect((await draft(page)).dirty).toBe(false);
  await page.locator('h1').dblclick();
  await page.getByLabel('메모 이름', { exact: true }).fill('취소 이름');
  await page.getByRole('button', { name: '취소', exact: true }).click();
  await expect(page.locator('h1')).toHaveText('회의 기록');
  expect((await draft(page)).revision).toBe(8);
});
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
async function send(page: Page, type = 'note.load', revision = 7, payload: unknown = { markdown: '원본' }, noteId = id, protocolVersion = 1) {
  await page.evaluate(message => (window as any).fromHost(message), { protocolVersion, type, requestId: 'host-1', noteId, revision, payload });
}
async function messages(page: Page) { return page.evaluate(() => (window as any).hostMessages as any[]); }

test('준비→호스트 문서 로드→변경 메시지→저장 요청을 실제 편집에 연결한다', async ({ page }) => {
  await hosted(page);
  expect((await messages(page))[0]).toMatchObject({ protocolVersion: 1, type: 'editor.ready', revision: 0 });
  await send(page);
  expect((await draft(page)).markdown).toBe('원본');
  await replaceText(page, '한글 수정');
  await expect.poll(async () => (await messages(page)).filter(m => m.type === 'note.changed').at(-1)?.payload.markdown).toBe('한글 수정');
  const changed = (await messages(page)).filter(m => m.type === 'note.changed').at(-1);
  expect(changed.noteId).toBe(id);
  await page.keyboard.press('Control+s');
  await expect.poll(async () => (await messages(page)).at(-1)?.type).toBe('note.saveRequested');
  await send(page, 'note.conflict', changed.revision, { message: '저장 기능 연결 전입니다.' });
  await expect(page.getByRole('status').filter({ hasText: '저장 기능 연결 전입니다.' })).toBeVisible();
  expect((await draft(page)).dirty).toBe(true);
  await send(page, 'note.saved', changed.revision, {});
  expect((await draft(page)).dirty).toBe(false);
  await expect(page.locator('#host-status')).toContainText('저장됨');
});

test('잘못된 호스트 메시지와 오래된 저장 응답은 현재 본문에 영향을 주지 않는다', async ({ page }) => {
  await hosted(page);
  await send(page);
  await send(page, 'note.load', 8, { markdown: '공격' }, id, 2);
  await send(page, 'unknown.command', 8, { markdown: '공격' });
  await send(page, 'note.load', 6, { markdown: '오래된 본문' });
  expect((await draft(page)).markdown).toBe('원본');
  await replaceText(page, '유지할 변경');
  await send(page, 'note.saved', 7, {});
  expect((await draft(page)).dirty).toBe(true);
  await send(page, 'note.load', 8, { markdown: 123 });
  expect((await draft(page)).markdown).toBe('유지할 변경');
  const count = (await messages(page)).length;
  await mode(page, 'preview');
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('16px');
  expect((await messages(page)).length).toBe(count);
});

test('한글 조합 중간값을 호스트에 보내지 않고 완료한 본문만 전송한다', async ({ page, context }) => {
  await hosted(page);
  await send(page, 'note.load', 7, { markdown: '' });
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
  const cdp = await context.newCDPSession(page);
  await cdp.send('Input.imeSetComposition', { text: 'ㅎ', selectionStart: 1, selectionEnd: 1 });
  expect((await messages(page)).filter(m => m.type === 'note.changed')).toHaveLength(0);
  await cdp.send('Input.imeSetComposition', { text: '한', selectionStart: 1, selectionEnd: 1 });
  await cdp.send('Input.insertText', { text: '한' });
  await expect.poll(async () => (await messages(page)).filter(m => m.type === 'note.changed').at(-1)?.payload.markdown).toBe('한');
  expect((await messages(page)).filter(m => m.type === 'note.changed').every(m => m.payload.markdown === '한')).toBe(true);
});
