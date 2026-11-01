import { chromium, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';

// 독립 프로필로 실행 중인 실제 WPF WebView2에만 연결합니다.
const browser = await chromium.connectOverCDP(process.env.DEVPOSTIT_CDP_ENDPOINT ?? 'http://127.0.0.1:9333');
try {
  const pages = browser.contexts().flatMap(context => context.pages());
  const page = pages.find(candidate => candidate.url() === 'https://devpostit.local/index.html');
  if (!page) throw new Error('DevPostIt의 로컬 편집기 페이지를 찾지 못했습니다.');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  await expect(page).toHaveTitle('DevPostIt');
  await expect(page.locator('#host-status')).toContainText('앱 연결됨');
  if (process.argv.includes('--restore')) {
    await expect(page.getByLabel('기준 글자 크기')).toHaveText('16');
    await expect(page.getByLabel('편집 글꼴')).toHaveValue('jetbrains');
    await expect(page.getByLabel('합자 사용')).not.toBeChecked();
    await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '14px');
    console.log('WPF 재시작 후 크기16·편집기14·JetBrains Mono·합자 해제 복원 성공');
  } else {
    await expect(page.getByLabel('기준 글자 크기')).toHaveText('15');
    await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
    await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
    await page.keyboard.press('Control+a');
    await page.keyboard.insertText('# WPF 실제 편집\n\n한글 본문과 Monaco 연결 확인');
    await expect.poll(() => page.evaluate(() => window.hunminEditor.getDraft().markdown)).toBe('# WPF 실제 편집\n\n한글 본문과 Monaco 연결 확인');
    await page.keyboard.press('Control+s');
    await expect(page.locator('#host-status')).toContainText('파일 저장 기능은 아직 연결 전');
    await page.getByRole('button', { name: '미리보기', exact: true }).click();
    await expect(page.locator('#preview h1')).toHaveText('WPF 실제 편집');
    await page.getByRole('button', { name: '글자 크게', exact: true }).click();
    await expect(page.getByLabel('기준 글자 크기')).toHaveText('16');
    await page.getByLabel('편집 글꼴').selectOption('jetbrains');
    await page.getByLabel('합자 사용').uncheck();
    await page.getByRole('button', { name: '소스 편집', exact: true }).click();
    await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '14px');
    await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-family', /^"JetBrains Mono"/);
    console.log('WPF 초기15/13·실제 입력·네이티브 저장 요청 왕복·미리보기·전체 크기 조절 성공');
  }
  await mkdir('../.tools', { recursive: true });
  await page.screenshot({ path: `../.tools/wpf-${process.argv.includes('--restore') ? 'restore' : 'editor'}.png` });
} finally { await browser.close(); }
