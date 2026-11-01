import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode, replaceText } from './helpers';

const press = (page: Page, name: string) => page.getByRole('toolbar', { name: 'Markdown 서식', exact: true }).getByRole('button', { name, exact: true }).click();
async function start(page: Page, text: string) {
  await openEditor(page);
  await replaceText(page, text);
  await page.keyboard.press('Control+Home');
}
const markdown = async (page: Page) => (await draft(page)).markdown;
async function selectLines(page: Page, count: number) {
  await page.keyboard.press('Control+Home');
  for (let i = 1; i < count; i++) await page.keyboard.press('Shift+ArrowDown');
  await page.keyboard.press('Shift+End');
}

test('보기 전환 아래, 본문 위에 아이콘 툴바를 표시한다', async ({ page }) => {
  await openEditor(page);
  const toolbar = page.getByRole('toolbar', { name: 'Markdown 서식', exact: true });
  const buttons = toolbar.getByRole('button');
  await expect(buttons).toHaveCount(17);
  for (const button of await buttons.all()) {
    await expect(button.locator('svg')).toHaveCount(1);
    await expect(button).toHaveText('');
    expect(await button.getAttribute('title')).toBeTruthy();
  }
  const header = (await page.locator('.note-header').boundingBox())!;
  const bar = (await toolbar.boundingBox())!;
  const area = (await page.locator('.editor-area').boundingBox())!;
  expect(bar.y).toBeGreaterThanOrEqual(header.y + header.height - 1);
  expect(bar.y + bar.height).toBeLessThanOrEqual(area.y + 1);
});

for (const [width, height, size, rows] of [[464, 601, 15, 1], [420, 520, 15, 1], [304, 320, 24, 1]] as const) {
  test(`${width}×${height} 창과 글자 ${size}px에서 툴바가 창을 넘치지 않고 ${rows}줄이다`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await page.addInitScript(value => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify({ font: 'd2', ligatures: true, size: value })), size);
    await openEditor(page);
    const bar = (await page.locator('.format-toolbar').boundingBox())!;
    expect(bar.x + bar.width).toBeLessThanOrEqual(width);
    expect(bar.height).toBeLessThanOrEqual(40 * rows);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  });
}

test('글자 서식은 선택을 감싸고 다시 누르면 푼다', async ({ page }) => {
  await start(page, '한글 본문');
  await page.keyboard.press('Shift+End');
  for (const [name, marked] of [['굵게', '**한글 본문**'], ['기울임', '*한글 본문*'], ['취소선', '~~한글 본문~~'], ['인라인 코드', '`한글 본문`']]) {
    await press(page, name);
    expect(await markdown(page)).toBe(marked);
    await press(page, name);
    expect(await markdown(page)).toBe('한글 본문');
  }
});

test('굵은 글자 안에서 기울임을 켜고 끌 수 있다', async ({ page }) => {
  await start(page, '본문');
  await page.keyboard.press('Shift+End');
  await press(page, '굵게');
  await press(page, '기울임');
  expect(await markdown(page)).toBe('***본문***');
  await press(page, '기울임');
  expect(await markdown(page)).toBe('**본문**');
});

test('선택이 없으면 표시 사이에 커서를 둔다', async ({ page }) => {
  await start(page, '앞 ');
  await page.keyboard.press('End');
  await press(page, '굵게');
  await page.keyboard.insertText('뒤');
  expect(await markdown(page)).toBe('앞 **뒤**');
});

test('제목은 단계를 바꾸고 같은 단계를 다시 누르면 없앤다', async ({ page }) => {
  await start(page, '제목');
  await press(page, '제목 1');
  expect(await markdown(page)).toBe('# 제목');
  await press(page, '제목 2');
  expect(await markdown(page)).toBe('## 제목');
  await press(page, '제목 3');
  expect(await markdown(page)).toBe('### 제목');
  await press(page, '제목 3');
  expect(await markdown(page)).toBe('제목');
});

test('목록과 인용은 선택한 줄마다 적용하고 빈 줄은 건너뛴다', async ({ page }) => {
  await start(page, '가\n\n나');
  await selectLines(page, 3);
  await press(page, '글머리 목록');
  expect(await markdown(page)).toBe('- 가\n\n- 나');
  await press(page, '번호 목록');
  expect(await markdown(page)).toBe('1. 가\n\n2. 나');
  await press(page, '할 일 목록');
  expect(await markdown(page)).toBe('- [ ] 가\n\n- [ ] 나');
  await press(page, '할 일 목록');
  expect(await markdown(page)).toBe('가\n\n나');
  await press(page, '인용');
  expect(await markdown(page)).toBe('> 가\n\n> 나');
  await press(page, '인용');
  expect(await markdown(page)).toBe('가\n\n나');
});

test('목록은 들여쓰기를 유지한다', async ({ page }) => {
  await start(page, '  - 가');
  await press(page, '번호 목록');
  expect(await markdown(page)).toBe('  1. 가');
});

test('다음 줄 첫 칸까지 선택해도 그 줄은 바꾸지 않는다', async ({ page }) => {
  await start(page, '가\n나');
  await page.keyboard.press('Shift+ArrowDown');
  await press(page, '인용');
  expect(await markdown(page)).toBe('> 가\n나');
});

test('코드 블록은 선택한 줄을 감싸고 빈 줄에서는 안에 커서를 둔다', async ({ page }) => {
  await start(page, '가\n나');
  await selectLines(page, 2);
  await press(page, '코드 블록');
  expect(await markdown(page)).toBe('```\n가\n나\n```');
});

test('빈 줄의 코드 블록은 안에 커서를 둔다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await press(page, '코드 블록');
  await page.keyboard.insertText('코드');
  expect(await markdown(page)).toBe('```\n코드\n```');
});

test('코드 블록 안에 울타리가 있으면 더 긴 울타리로 감싼다', async ({ page }) => {
  await start(page, '```\nx\n```');
  await selectLines(page, 3);
  await press(page, '코드 블록');
  expect(await markdown(page)).toBe('````\n```\nx\n```\n````');
});

test('링크는 선택을 글자로 쓰고 주소를 선택해 바로 입력하게 한다', async ({ page }) => {
  await start(page, '문서');
  await page.keyboard.press('Shift+End');
  await press(page, '링크');
  await page.keyboard.insertText('https://example.com');
  expect(await markdown(page)).toBe('[문서](https://example.com)');
});

test('링크는 선택이 없으면 글자 자리를 선택한다', async ({ page }) => {
  await start(page, '');
  await press(page, '링크');
  await page.keyboard.insertText('문서');
  expect(await markdown(page)).toBe('[문서](url)');
});

test('구분선과 표는 앞뒤 문단과 빈 줄로 분리한다', async ({ page }) => {
  await start(page, '위\n아래');
  await press(page, '구분선');
  expect(await markdown(page)).toBe('위\n\n---\n\n아래');

  await replaceText(page, '위');
  await press(page, '표');
  await page.keyboard.insertText('이름');
  expect(await markdown(page)).toBe('위\n\n| 이름 | 제목 |\n| --- | --- |\n| 내용 | 내용 |');
  await mode(page, 'preview');
  await expect(page.locator('#preview table th').first()).toHaveText('이름');
  await expect(page.locator('#preview h2')).toHaveCount(0);
});

test('빈 줄의 구분선이 윗줄을 제목으로 바꾸지 않는다', async ({ page }) => {
  await start(page, '위\n');
  await page.keyboard.press('Control+End');
  await press(page, '구분선');
  await mode(page, 'preview');
  await expect(page.locator('#preview hr')).toHaveCount(1);
  await expect(page.locator('#preview h2')).toHaveCount(0);
});

test('한 번의 실행 취소로 되돌리고 혼합 줄바꿈 원문을 복원한다', async ({ page }) => {
  const raw = '﻿가\r\n나\n다';
  await openEditor(page, raw);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press('Control+Home');
  await press(page, '인용');
  expect((await draft(page)).dirty).toBe(true);
  await page.keyboard.press('Control+z');
  await expect.poll(() => draft(page)).toMatchObject({ markdown: raw, dirty: false });
});

test('미리보기에서는 툴바를 비활성화하고 본문을 바꾸지 않는다', async ({ page }) => {
  await openEditor(page, '본문');
  await mode(page, 'preview');
  const toolbar = page.getByRole('toolbar', { name: 'Markdown 서식', exact: true });
  for (const button of await toolbar.getByRole('button').all()) await expect(button).toBeDisabled();
  expect((await draft(page)).dirty).toBe(false);
  await mode(page, 'source');
  for (const button of await toolbar.getByRole('button').all()) await expect(button).toBeEnabled();
});
