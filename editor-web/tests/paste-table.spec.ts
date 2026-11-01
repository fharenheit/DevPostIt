import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode, replaceText } from './helpers';

// Excel이 클립보드에 넣는 형식과 같은 구조입니다.
const excelHtml = `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel"><head><meta name=ProgId content=Excel.Sheet><style>td {mso-number-format:General;}</style></head><body link="#0563C1">
<table border=0 cellpadding=0 cellspacing=0 width=144 style='border-collapse:collapse'>
 <col width=72 span=2>
 <!--StartFragment-->
 <tr height=22><td height=22 width=72>이름</td><td width=72>수량</td></tr>
 <tr height=22><td height=22>사과</td><td align=right x:num>3</td></tr>
 <!--EndFragment-->
</table></body></html>`;
const excelText = '이름\t수량\r\n사과\t3\r\n';
const excelMarkdown = '| 이름 | 수량 |\n| --- | --- |\n| 사과 | 3 |';

async function paste(page: Page, html: string | null, text: string) {
  return page.evaluate(({ html, text }) => {
    const data = new DataTransfer();
    if (html !== null) data.setData('text/html', html);
    data.setData('text/plain', text);
    return document.querySelector('#source textarea')!.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  }, { html, text });
}
async function start(page: Page, text: string) {
  await openEditor(page);
  await replaceText(page, text);
}
const markdown = async (page: Page) => (await draft(page)).markdown;

test('Excel에서 복사한 셀을 Markdown 표로 붙여넣는다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await paste(page, excelHtml, excelText);
  expect(await markdown(page)).toBe(excelMarkdown);
  await mode(page, 'preview');
  await expect(page.locator('#preview th').first()).toHaveText('이름');
  await expect(page.locator('#preview td').last()).toHaveText('3');
});

test('실제 Ctrl+V로 클립보드의 표를 붙여넣는다', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.evaluate(async ({ html, text }) => {
    await navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([text], { type: 'text/plain' }) })]);
  }, { html: excelHtml, text: excelText });
  await page.keyboard.press('Control+v');
  await expect.poll(() => markdown(page)).toBe(excelMarkdown);
});

test('문단 사이에서는 빈 줄로 분리하고 커서를 표 뒤에 둔다', async ({ page }) => {
  await start(page, '위\n아래');
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('End');
  await paste(page, excelHtml, excelText);
  expect(await markdown(page)).toBe(`위\n\n${excelMarkdown}\n\n아래`);
});

test('글 가운데에서는 앞뒤 글을 나누고 선택 영역은 표로 바꾼다', async ({ page }) => {
  await start(page, '앞X뒤');
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Shift+ArrowRight');
  await paste(page, excelHtml, excelText);
  expect(await markdown(page)).toBe(`앞\n\n${excelMarkdown}\n\n뒤`);
});

test('표 뒤에 이어서 입력하면 표 다음 위치에 들어간다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await paste(page, excelHtml, excelText);
  await page.keyboard.insertText('끝');
  expect(await markdown(page)).toBe(`${excelMarkdown}끝`);
});

test('한 번의 실행 취소로 되돌린다', async ({ page }) => {
  await start(page, '본문');
  await page.keyboard.press('Control+End');
  await paste(page, excelHtml, excelText);
  await page.keyboard.press('Control+z');
  await expect.poll(() => markdown(page)).toBe('본문');
});

test('셀의 파이프·역슬래시·줄바꿈을 표가 깨지지 않게 바꾼다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await paste(page, '<table><tr><td>a|b</td><td>C:\\temp</td></tr><tr><td>첫 줄<br>둘째\n  줄</td><td>&nbsp;값&nbsp;</td></tr></table>', '');
  expect(await markdown(page)).toBe('| a\\|b | C:\\\\temp |\n| --- | --- |\n| 첫 줄 둘째 줄 | 값 |');
});

test('병합 셀은 칸을 맞추고 끝의 빈 행은 버린다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await paste(page, '<table><tr><td colspan=2>합침</td><td>c</td></tr><tr><td rowspan=2>세로</td><td>b</td><td>c</td></tr><tr><td>b2</td></tr><tr><td></td><td></td><td></td></tr></table>', '');
  expect(await markdown(page)).toBe('| 합침 |  | c |\n| --- | --- | --- |\n| 세로 | b | c |\n|  | b2 |  |');
});

for (const [name, html, text] of [
  ['표가 없는 HTML', '<p>그냥 <b>글</b></p>', '그냥 글'],
  ['HTML 없이 탭이 든 글', null, 'a\tb\nc\td'],
  ['셀 하나', '<table><tr><td>값</td></tr></table>', '값'],
  ['표 밖에 글이 있는 웹 문서', '<p>설명 글</p><table><tr><td>a</td><td>b</td></tr></table>', '설명 글 a b'],
  ['표가 여러 개', '<table><tr><td>a</td><td>b</td></tr></table><table><tr><td>c</td><td>d</td></tr></table>', 'a b c d'],
  ['표 안의 표', '<table><tr><td><table><tr><td>a</td><td>b</td></tr></table></td><td>c</td></tr></table>', 'a b c'],
] as const) {
  test(`${name}은 표로 바꾸지 않고 Monaco 기본 붙여넣기에 맡긴다`, async ({ page }) => {
    await openEditor(page);
    await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
    await paste(page, html, text);
    expect(await markdown(page)).toBe(text);
  });
}

test('붙여넣은 HTML의 스크립트와 이미지를 실행·요청하지 않는다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!request.url().startsWith('http://127.0.0.1:')) requests.push(request.url()); });
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await paste(page, '<table><tr><td>a<img src="https://example.com/track.png" onerror="window.attacked=true"></td><td><script>window.attacked=true</script>b</td></tr></table>', '');
  await page.waitForTimeout(200);
  expect(await page.evaluate(() => (window as unknown as { attacked?: boolean }).attacked)).toBeUndefined();
  expect(requests).toEqual([]);
  expect(await markdown(page)).toBe('| a | b |\n| --- | --- |');
});

test('한글 조합 중에는 표로 바꾸지 않는다', async ({ page }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.locator('#source').dispatchEvent('compositionstart');
  await paste(page, excelHtml, excelText);
  expect(await markdown(page)).not.toContain('| --- |');
  await page.locator('#source').dispatchEvent('compositionend');
});
