import { test, expect } from '@playwright/test';
import { openEditor, draft, mode, replaceText } from './helpers';

for (const [name, markdown] of Object.entries({
  '빈 메모': '',
  'CRLF': '# 제목\r\n\r\n- [ ] 업무\r\n',
  'BOM과 혼합 줄바꿈': '\uFEFF# 제목\r\n문단\n끝\r\n',
  'HTML과 확장 구문': '<details><summary>원문</summary>그대로</details>\n\n:::warning\n경고\n:::\n',
})) {
  test(`${name}: 열기와 보기 전환은 원문을 변경하지 않는다`, async ({ page }) => {
    await openEditor(page, markdown);
    expect(await draft(page)).toEqual({ markdown, revision: 7, dirty: false });
    await mode(page, 'preview');
    await mode(page, 'source');
    expect(await draft(page)).toEqual({ markdown, revision: 7, dirty: false });
    await page.reload();
    await page.waitForFunction(() => Boolean(window.hunminEditor));
    await page.evaluate(markdown => window.hunminEditor.load('note-1', markdown, 7), markdown);
    expect(await draft(page)).toEqual({ markdown, revision: 7, dirty: false });
  });
}

test('소스 편집 후 Markdown 문법과 미지원 본문이 유지된다', async ({ page }) => {
  await openEditor(page, '초기 본문');
  const edited = '# 수정 제목\n\n- 목록\n- [x] 완료\n\n| 항목 | 값 |\n| --- | --- |\n| 한글 | 보존 |\n\n```ts\nconst a = 1;\n```\n\n<details>미지원 원문</details>';
  await replaceText(page, edited);
  expect((await draft(page)).dirty).toBe(true);
  expect((await draft(page)).revision).toBeGreaterThan(7);
  await mode(page, 'preview');
  await expect(page.locator('#preview h1')).toHaveText('수정 제목');
  await expect(page.locator('#preview table')).toContainText('보존');
  await expect(page.locator('#preview pre code')).toContainText('const a = 1;');
  await expect(page.locator('#preview input[type=checkbox]')).toBeChecked();
  await mode(page, 'source');
  expect((await draft(page)).markdown).toBe(edited);
});

test('Undo로 원문에 복귀하면 dirty가 해제된다', async ({ page }) => {
  await openEditor(page, '원본');
  await replaceText(page, '변경');
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await draft(page)).markdown).toBe('원본');
  expect((await draft(page)).dirty).toBe(false);
});

test('새 메모 로드시 이전 문서와 Undo 기록이 남지 않는다', async ({ page }) => {
  await openEditor(page, '첫째');
  await replaceText(page, '첫째 수정');
  await page.evaluate(() => window.hunminEditor.load('note-2', '둘째', 20));
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
  await page.keyboard.press('Control+z');
  expect(await draft(page)).toEqual({ markdown: '둘째', revision: 20, dirty: false });
});
