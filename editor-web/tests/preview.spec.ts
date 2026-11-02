import { test, expect } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

test('위험 HTML·링크·원격 이미지를 실행하지 않고 원문을 보존한다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!request.url().startsWith('http://127.0.0.1:')) requests.push(request.url()); });
  const markdown = '<script>window.attacked=true</script>\n<img src="https://example.com/track.png" onerror="window.attacked=true">\n\n[외부](https://example.com)\n![추적](https://example.com/image.png)\n[실행](javascript:alert(1))';
  await openEditor(page, markdown);
  await mode(page, 'preview');
  await expect(page.locator('#preview')).toContainText('<script>');
  expect(await page.locator('#preview script, #preview img').count()).toBe(0);
  // 웹 링크는 누를 때 앱이 확인 후 엽니다. 스크립트 주소는 링크가 되지 않습니다.
  await expect(page.locator('#preview a[href]')).toHaveCount(1);
  await expect(page.locator('#preview a[href]')).toHaveAttribute('href', 'https://example.com');
  expect(await page.evaluate(() => 'attacked' in window)).toBe(false);
  expect(requests).toEqual([]);
  expect((await draft(page)).markdown).toBe(markdown);
});

test('표 칸의 br은 줄바꿈으로 표시하고 원문은 그대로 둔다', async ({ page }) => {
  const markdown = '| 항목 | 설명 |\n| --- | --- |\n| 첫 줄<br>둘째 줄<br/>셋째 줄<BR />넷째 줄 | **굵게<br>굵게** |\n\n위<br>아래';
  await openEditor(page, markdown);
  await mode(page, 'preview');
  const cells = page.locator('#preview td');
  await expect(cells.nth(0).locator('br')).toHaveCount(3);
  expect(await cells.nth(0).evaluate(element => (element as HTMLElement).innerText.split('\n'))).toEqual(['첫 줄', '둘째 줄', '셋째 줄', '넷째 줄']);
  await expect(cells.nth(1).locator('strong br')).toHaveCount(1);
  await expect(page.locator('#preview p')).toHaveText('위<br>아래');
  expect((await draft(page)).markdown).toBe(markdown);
});

test('표 칸에서도 코드와 이스케이프한 br, 다른 HTML은 글자로 둔다', async ({ page }) => {
  await openEditor(page, '| 값 |\n| --- |\n| `<br>` |\n| \\<br> |\n| &lt;br> |\n| <br onclick="window.attacked=true"> |\n| <b>굵게</b> |');
  await mode(page, 'preview');
  await expect(page.locator('#preview td br')).toHaveCount(0);
  await expect(page.locator('#preview td code')).toHaveText('<br>');
  await expect(page.locator('#preview td').nth(1)).toHaveText('<br>');
  await expect(page.locator('#preview td').nth(2)).toHaveText('<br>');
  await expect(page.locator('#preview td').nth(3)).toHaveText('<br onclick="window.attacked=true">');
  await expect(page.locator('#preview td b')).toHaveCount(0);
  await expect(page.locator('#preview td').nth(4)).toHaveText('<b>굵게</b>');
});

test('창이 넓으면 미리보기 내용도 함께 넓어진다', async ({ page }) => {
  await page.setViewportSize({ width: 1400, height: 800 });
  await openEditor(page, '# 제목\n\n' + '넓은 문단 '.repeat(200) + '\n\n```\ncode\n```\n');
  await mode(page, 'preview');
  const preview = page.locator('#preview');
  const width = await preview.evaluate(element => { const style = getComputedStyle(element); return element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight); });
  expect(width).toBeGreaterThan(1200);
  for (const selector of ['h1', 'p', 'pre']) expect((await page.locator(`#preview ${selector}`).boundingBox())!.width).toBeCloseTo(width, 0);
  await page.setViewportSize({ width: 500, height: 800 });
  const narrow = (await page.locator('#preview p').boundingBox())!.width;
  expect(narrow).toBeLessThan(500);
  expect(await preview.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
});
