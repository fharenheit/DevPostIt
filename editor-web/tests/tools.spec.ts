import { test, expect, type Page, type Locator } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { applyHolidayData, holidaysOf } from '../src/korean-holidays';
import { fromOutline, toOutline, type MapNode } from '../src/mindmap-core';
import { nextDue, occursOn, type CalendarEvent } from '../src/planner-core';

const days = (year: number) => holidaysOf(year).map(item => item.date.slice(5));
const substitutes = (year: number) => holidaysOf(year).filter(item => item.substitute).map(item => item.date.slice(5));

test.describe('한국 공휴일', () => {
  test('2026년 공휴일', () => {
    expect(days(2026)).toEqual(['01-01', '02-16', '02-17', '02-18', '03-01', '03-02', '05-01', '05-05', '05-24', '05-25', '06-03', '06-06', '07-17', '08-15', '08-17', '09-24', '09-25', '09-26', '10-03', '10-05', '10-09', '12-25']);
  });
  test('2027년 대체공휴일 7일', () => {
    expect(substitutes(2027)).toEqual(['02-09', '05-03', '07-19', '08-16', '10-04', '10-11', '12-27']);
    expect(days(2027)).toEqual(expect.arrayContaining(['02-06', '02-07', '02-08', '05-13', '09-14', '09-15', '09-16']));
  });
  test('2025년 어린이날과 부처님오신날이 겹치면 대체공휴일은 하루다', () => {
    expect(substitutes(2025)).toEqual(['03-03', '05-06', '10-08']);
    expect(holidaysOf(2025).filter(item => item.date === '2025-05-05').map(item => item.name).sort()).toEqual(['부처님오신날', '어린이날']);
    expect(days(2025)).toEqual(expect.arrayContaining(['01-27', '01-28', '01-29', '01-30', '06-03', '10-05', '10-06', '10-07']));
    expect(days(2025)).not.toContain('07-17');
  });
  test('2024년 음력 공휴일과 임시공휴일', () => {
    expect(days(2024)).toEqual(expect.arrayContaining(['02-09', '02-10', '02-11', '02-12', '04-10', '05-15', '09-16', '09-17', '09-18', '10-01']));
    expect(substitutes(2024)).toEqual(['02-12', '05-06']);
  });
  test('신정과 현충일은 대체하지 않는다', () => {
    expect(days(2028)).toContain('01-01');
    expect(substitutes(2028)).not.toContain('01-03');
    expect(days(2027)).toContain('06-06');
    expect(substitutes(2027)).not.toContain('06-07');
  });
});

test.describe('공휴일 데이터 파일', () => {
  const file = JSON.parse(readFileSync('../data/korean-holidays.json', 'utf8'));
  test.afterEach(() => applyHolidayData(null));
  for (const year of ['2026', '2027']) {
    test(`저장소의 ${year}년 목록은 내장 계산과 일치한다`, () => {
      applyHolidayData(null);
      const key = (item: { date: string; name: string; substitute: boolean }) => `${item.date}|${item.name}|${item.substitute}`;
      expect(file.years[year].map(key)).toEqual(holidaysOf(Number(year)).map(key));
    });
  }
  test('파일에 있는 연도는 목록 전체를 쓰고 없는 연도는 계산한다', () => {
    applyHolidayData({ 2030: [{ date: '2030-03-05', name: '시험 공휴일', substitute: false }] });
    expect(holidaysOf(2030)).toEqual([{ date: '2030-03-05', name: '시험 공휴일', substitute: false }]);
    expect(holidaysOf(2027).map(item => item.date)).toContain('2027-05-03');
    applyHolidayData(null);
    expect(holidaysOf(2030).map(item => item.name)).toContain('신정');
  });
  test('형식이 잘못된 데이터는 통째로 무시한다', () => {
    for (const wrong of [null, 'x', [], { 2030: 'x' }, { abcd: [] }, { 2030: [{ date: '2031-01-01', name: 'a', substitute: false }] }, { 2030: [{ date: '2030-01-01', name: 1, substitute: false }] }, { 2030: [{ date: '2030-01-01', name: 'a'.repeat(51), substitute: false }] }, { 2030: [{ date: '2030-01-01', name: 'a', substitute: 'no' }] }]) {
      applyHolidayData(wrong);
      expect(holidaysOf(2030).map(item => item.name)).toContain('신정');
    }
  });
});

test.describe('일정 규칙', () => {
  const event = (repeat: CalendarEvent['repeat'], extra: Partial<CalendarEvent> = {}): CalendarEvent => ({ id: 'a', title: 't', start: '2026-01-31T09:00', end: '2026-01-31T10:00', allDay: false, location: '', notes: '', color: 'blue', repeat, repeatUntil: null, reminderMinutes: null, exceptions: [], ...extra });
  test('반복 규칙', () => {
    expect(occursOn(event('none'), '2026-01-31')).toBe(true);
    expect(occursOn(event('none'), '2026-02-01')).toBe(false);
    expect(occursOn(event('daily'), '2026-01-30')).toBe(false);
    expect(occursOn(event('weekly'), '2026-02-07')).toBe(true);
    expect(occursOn(event('weekdays'), '2026-02-01')).toBe(false);
    expect(occursOn(event('weekdays'), '2026-02-02')).toBe(true);
    expect(occursOn(event('monthly'), '2026-03-31')).toBe(true);
    expect(occursOn(event('yearly'), '2027-01-31')).toBe(true);
    expect(occursOn(event('daily', { exceptions: ['2026-02-02'] }), '2026-02-02')).toBe(false);
    expect(occursOn(event('daily', { repeatUntil: '2026-02-05' }), '2026-02-06')).toBe(false);
  });
  test('반복 할 일의 다음 기한', () => {
    expect(nextDue('2026-01-31', 'daily')).toBe('2026-02-01');
    expect(nextDue('2026-01-30', 'weekdays')).toBe('2026-02-02');
    expect(nextDue('2026-01-31', 'weekly')).toBe('2026-02-07');
    expect(nextDue('2026-01-31', 'monthly')).toBe('2026-02-28');
    expect(nextDue('2026-01-31', 'yearly')).toBe('2027-01-31');
  });
  test('개요 글을 주제로 바꾸고 되돌린다', () => {
    const nodes = fromOutline('- 가\n  - 나\n    - 다\n  - 라\n- 마\r\n\t탭');
    expect(nodes.map(node => node.text)).toEqual(['가', '마']);
    expect(nodes[0].children.map(node => node.text)).toEqual(['나', '라']);
    expect(nodes[0].children[0].children[0].text).toBe('다');
    expect(nodes[1].children[0].text).toBe('탭');
    expect(toOutline(nodes[0])).toBe('- 가\n  - 나\n    - 다\n  - 라');
    expect(fromOutline('\n\n')).toEqual([]);
  });
});

async function openMap(page: Page) {
  await page.goto('/mindmap.html');
  await expect(page.locator('.mm-node.root')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.locator('.mm-editor')).toHaveCount(0);
  await expect(page.locator('#mm-canvas')).toBeFocused();
}
const nodeTexts = (page: Page) => page.locator('.mm-node .mm-text').allInnerTexts();
const nodeNamed = (page: Page, text: string) => page.locator('.mm-node', { hasText: new RegExp(`^${text}$`) });
const storedMap = (page: Page) => page.evaluate(() => { const maps = JSON.parse(localStorage.getItem('devpostit.local.mindmaps') ?? '{}'); return Object.values<any>(maps); });

test.describe('마인드맵', () => {
  test('Tab은 하위, Enter는 같은 단계 주제를 키보드만으로 만든다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('Tab');
    await page.keyboard.type('첫째');
    await page.keyboard.press('Enter');
    await expect(page.locator('.mm-editor')).toHaveCount(0);
    await page.keyboard.press('Enter');
    await page.keyboard.type('둘째');
    await page.keyboard.press('Tab');
    await page.keyboard.type('손자');
    await page.keyboard.press('Enter');
    expect(await nodeTexts(page)).toEqual(expect.arrayContaining(['중심 주제', '첫째', '둘째', '손자']));
    await expect(nodeNamed(page, '손자')).toHaveClass(/selected/);
    await page.keyboard.press('Shift+Enter');
    await page.keyboard.type('위');
    await page.keyboard.press('Enter');
    await expect.poll(async () => (await storedMap(page))[0]?.root.children.at(-1)?.children.map((node: MapNode) => node.text) ?? (await storedMap(page))[0]?.root.children[1]?.children.map((node: MapNode) => node.text)).toEqual(['위', '손자']);
  });

  test('글자를 치면 바로 편집하고 Esc는 취소, F2는 기존 글을 편집한다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.type('topic');
    await expect(page.locator('.mm-editor')).toHaveValue('topic');
    await page.keyboard.press('Enter');
    await expect(nodeNamed(page, 'topic')).toBeVisible();
    await page.keyboard.press('F2');
    await expect(page.locator('.mm-editor')).toHaveValue('topic');
    await page.keyboard.type('다른이름');
    await page.keyboard.press('Escape');
    await expect(nodeNamed(page, 'topic')).toBeVisible();
    await page.keyboard.press('Tab');
    await page.keyboard.press('Escape');
    expect(await nodeTexts(page)).toEqual(['topic']);
  });

  test('편집 중 Shift+Enter는 줄바꿈이다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('Tab');
    await page.keyboard.type('줄1');
    await page.keyboard.press('Shift+Enter');
    await page.keyboard.type('줄2');
    await page.keyboard.press('Enter');
    expect((await nodeTexts(page)).some(text => text.includes('줄1') && text.includes('줄2'))).toBe(true);
  });

  async function threeTopics(page: Page) {
    await openMap(page);
    for (const name of ['가', '나', '다']) {
      await page.locator('[data-action="child"]').click();
      await page.keyboard.type(name);
      await page.keyboard.press('Enter');
      await nodeNamed(page, '중심 주제').click();
    }
  }

  test('방향키로 고르고 Delete로 지우고 Ctrl+Z로 되돌린다', async ({ page }) => {
    await threeTopics(page);
    await page.keyboard.press('ArrowRight');
    await expect(page.locator('.mm-node.selected')).not.toHaveClass(/root/);
    const picked = await page.locator('.mm-node.selected .mm-text').innerText();
    await page.keyboard.press('Delete');
    expect(await nodeTexts(page)).not.toContain(picked);
    await page.keyboard.press('Control+z');
    expect(await nodeTexts(page)).toContain(picked);
    await page.keyboard.press('Control+y');
    expect(await nodeTexts(page)).not.toContain(picked);
  });

  test('중심 주제는 삭제할 수 없다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('Delete');
    await expect(page.locator('.mm-node.root')).toBeVisible();
    await expect(page.locator('#mm-status')).toContainText('삭제할 수 없습니다');
  });

  test('Space로 접고 펼친다', async ({ page }) => {
    await threeTopics(page);
    await nodeNamed(page, '가').click();
    await page.keyboard.press('Tab');
    await page.keyboard.type('하위');
    await page.keyboard.press('Enter');
    await nodeNamed(page, '가').click();
    await page.keyboard.press('Space');
    await expect(nodeNamed(page, '하위')).toHaveCount(0);
    await page.keyboard.press('Space');
    await expect(nodeNamed(page, '하위')).toBeVisible();
  });

  test('주제를 끌어 다른 주제의 하위로 옮기고 되돌린다', async ({ page }) => {
    await threeTopics(page);
    const from = (await nodeNamed(page, '가').boundingBox())!;
    const to = (await nodeNamed(page, '나').boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 6 });
    await expect(nodeNamed(page, '나')).toHaveClass(/drop-child/);
    await page.mouse.up();
    await expect.poll(async () => (await storedMap(page))[0].root.children.find((node: MapNode) => node.text === '나')?.children.map((node: MapNode) => node.text)).toEqual(['가']);
    await page.keyboard.press('Control+z');
    await expect.poll(async () => (await storedMap(page))[0].root.children.map((node: MapNode) => node.text)).toContain('가');
  });

  test('주제를 자기 하위로는 옮기지 않는다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('Tab');
    await page.keyboard.type('부모');
    await page.keyboard.press('Tab');
    await page.keyboard.type('자식');
    await page.keyboard.press('Enter');
    const from = (await nodeNamed(page, '부모').boundingBox())!;
    const to = (await nodeNamed(page, '자식').boundingBox())!;
    await page.mouse.move(from.x + 5, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 5 });
    await page.mouse.up();
    await expect.poll(async () => (await storedMap(page))[0]?.root.children[0]?.children[0]?.text).toBe('자식');
    expect((await storedMap(page))[0].root.children[0].text).toBe('부모');
  });

  test('Alt+방향키로 순서와 단계를 옮긴다', async ({ page }) => {
    await threeTopics(page);
    await page.locator('#mm-layout').selectOption('right');
    await nodeNamed(page, '나').click();
    await page.keyboard.press('Alt+ArrowUp');
    await expect.poll(async () => (await storedMap(page))[0].root.children.map((node: MapNode) => node.text)).toEqual(['나', '가', '다']);
    await nodeNamed(page, '다').click();
    await page.keyboard.press('Alt+ArrowRight');
    await expect.poll(async () => (await storedMap(page))[0].root.children[1].children.map((node: MapNode) => node.text)).toEqual(['다']);
    await page.keyboard.press('Alt+ArrowLeft');
    await expect.poll(async () => (await storedMap(page))[0].root.children.map((node: MapNode) => node.text)).toEqual(['나', '가', '다']);
  });

  test('여러 마인드맵을 만들고 다시 열어도 내용이 남는다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('F2');
    await page.keyboard.type('첫 맵 주제');
    await page.keyboard.press('Enter');
    await page.locator('#mm-title').fill('첫 맵');
    await page.locator('#mm-title').press('Enter');
    await page.waitForTimeout(650);
    await page.getByRole('button', { name: '새 마인드맵', exact: true }).click();
    await expect(page.locator('.mm-editor')).toBeFocused();
    await page.keyboard.type('둘째 맵 주제');
    await page.keyboard.press('Enter');
    await expect(page.locator('.mm-map')).toHaveCount(2);
    await page.locator('.mm-map', { hasText: '첫 맵' }).click();
    await expect(nodeNamed(page, '첫 맵 주제')).toBeVisible();
    await page.waitForTimeout(650);
    await page.reload();
    await expect(page.locator('.mm-map')).toHaveCount(2);
    await expect(nodeNamed(page, '첫 맵 주제')).toBeVisible();
    await page.locator('.mm-map', { hasText: '새 마인드맵' }).click();
    await expect(nodeNamed(page, '둘째 맵 주제')).toBeVisible();
  });

  test('목록에서 복제와 삭제를 한다. 삭제는 기본이 취소다', async ({ page }) => {
    await openMap(page);
    await page.waitForTimeout(100);
    await page.locator('#mm-maps li').first().hover();
    await page.getByRole('button', { name: '새 마인드맵 복제' }).click();
    await expect(page.locator('.mm-map')).toHaveCount(2);
    await expect(page.locator('.mm-map', { hasText: '복사본' })).toBeVisible();
    await page.locator('#mm-maps li').first().hover();
    await page.locator('#mm-maps li').first().getByRole('button', { name: /삭제$/ }).click();
    await expect(page.locator('.tool-confirm button:focus')).toHaveText('취소');
    await page.keyboard.press('Enter');
    await expect(page.locator('.mm-map')).toHaveCount(2);
    await page.locator('#mm-maps li').first().hover();
    await page.locator('#mm-maps li').first().getByRole('button', { name: /삭제$/ }).click();
    await page.locator('.tool-confirm').getByRole('button', { name: '삭제', exact: true }).click();
    await expect(page.locator('.mm-map')).toHaveCount(1);
  });

  test('들여쓴 글을 붙여넣으면 주제가 되고 가지를 복사해 붙여넣는다', async ({ page }) => {
    await openMap(page);
    await page.evaluate(() => {
      const data = new DataTransfer();
      data.setData('text/plain', '- 일\n  - 이\n- 삼');
      document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
    });
    expect(await nodeTexts(page)).toEqual(expect.arrayContaining(['일', '이', '삼']));
    await nodeNamed(page, '일').click();
    await page.keyboard.press('Control+d');
    await expect(nodeNamed(page, '이')).toHaveCount(2);
  });

  test('색, 굵게, 표시, 메모, 링크를 저장한다', async ({ page }) => {
    await openMap(page);
    await page.keyboard.press('Control+b');
    await page.locator('#mm-icon').selectOption('star');
    await page.locator('#mm-color').evaluate((input: HTMLInputElement) => { input.value = '#112233'; input.dispatchEvent(new Event('change', { bubbles: true })); });
    await page.locator('#mm-note').fill('메모 내용');
    await page.locator('#mm-link').fill('https://example.com');
    await page.locator('#mm-canvas').click({ position: { x: 5, y: 5 } });
    await expect.poll(async () => (await storedMap(page))[0].root).toMatchObject({ bold: true, icon: 'star', color: '#112233', note: '메모 내용', link: 'https://example.com' });
  });

  test('찾기는 접힌 가지 안의 주제까지 찾아 펼친다', async ({ page }) => {
    await threeTopics(page);
    await nodeNamed(page, '가').click();
    await page.keyboard.press('Tab');
    await page.keyboard.type('숨은주제');
    await page.keyboard.press('Enter');
    await nodeNamed(page, '가').click();
    await page.keyboard.press('Space');
    await page.keyboard.press('Control+f');
    await page.locator('#mm-find-input').fill('숨은');
    await page.locator('#mm-find-input').press('Enter');
    await expect(nodeNamed(page, '숨은주제')).toHaveClass(/selected/);
    await expect(page.locator('#mm-find-count')).toHaveText('1/1');
  });

  test('휠과 단축키로 확대, 축소, 맞춤을 한다', async ({ page }) => {
    await openMap(page);
    const scale = () => page.locator('#mm-stage').evaluate(stage => new DOMMatrix(getComputedStyle(stage).transform).a);
    const before = await scale();
    await page.keyboard.press('Control+=');
    expect(await scale()).toBeGreaterThan(before);
    await page.locator('#mm-canvas').hover();
    await page.mouse.wheel(0, 200);
    await page.keyboard.press('Control+0');
    expect(await scale()).toBeCloseTo(before, 1);
  });

  test('페이지는 앱에 경로 없이 저장 요청만 보낸다', async ({ page }) => {
    await page.addInitScript(() => {
      const messages: any[] = [];
      let receive: ((event: { data: unknown }) => void) | undefined;
      Object.assign(window, { chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_: string, handler: typeof receive) => { receive = handler; } } }, hostMessages: messages, fromHost: (message: unknown) => receive?.({ data: message }) });
    });
    await page.goto('/mindmap.html');
    await page.evaluate(() => (window as any).fromHost({ type: 'mindmap.list', maps: [] }));
    await expect.poll(() => page.evaluate(() => (window as any).hostMessages.map((message: any) => message.type))).toContain('mindmap.save');
    const save = await page.evaluate(() => (window as any).hostMessages.find((message: any) => message.type === 'mindmap.save'));
    expect(Object.keys(save).sort()).toEqual(['map', 'protocolVersion', 'type']);
    expect(await page.evaluate(() => (window as any).devpostitPending())).toBeNull();
    await page.locator('#mm-canvas').focus();
    await page.keyboard.press('Tab');
    await page.keyboard.type('닫기 직전');
    await page.keyboard.press('Enter');
    const pending = await page.evaluate(() => (window as any).devpostitPending());
    expect(JSON.parse(pending).root.children[0].text).toBe('닫기 직전');
  });
});

async function openTodo(page: Page) {
  await page.goto('/todo.html');
  await expect(page.getByRole('textbox', { name: '새 할 일' })).toBeFocused();
}
const addTodo = async (page: Page, text: string) => { await page.getByRole('textbox', { name: '새 할 일' }).fill(text); await page.keyboard.press('Enter'); };

test.describe('할 일', () => {
  test('Enter로 등록하면 입력칸이 비워지고 커서가 그대로 남는다', async ({ page }) => {
    await openTodo(page);
    const input = page.getByRole('textbox', { name: '새 할 일' });
    await page.keyboard.type('우유 사기');
    await page.keyboard.press('Enter');
    await expect(page.locator('.todo-item')).toHaveCount(1);
    await expect(input).toHaveValue('');
    await expect(input).toBeFocused();
    await page.keyboard.type('빵 사기');
    await page.keyboard.press('Enter');
    await expect(page.locator('.todo-item')).toHaveCount(2);
    await expect(input).toBeFocused();
    await expect(page.locator('.todo-item .todo-name').first()).toHaveText('빵 사기');
    await page.keyboard.press('Enter');
    await expect(page.locator('.todo-item')).toHaveCount(2);
  });

  test('한글 조합 중 Enter는 조합이 끝난 뒤 한 번만 등록한다', async ({ page }) => {
    await openTodo(page);
    await page.locator('#todo-input').evaluate((input: HTMLInputElement) => {
      input.value = '한글';
      input.dispatchEvent(new CompositionEvent('compositionstart'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
      input.dispatchEvent(new CompositionEvent('compositionend'));
    });
    await expect(page.locator('.todo-item')).toHaveCount(1);
    await expect(page.locator('#todo-input')).toHaveValue('');
  });

  test('완료하면 완료됨으로 옮기고 되돌린다', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '할 것');
    await page.getByRole('checkbox', { name: '할 것 완료' }).click();
    await expect(page.locator('#todo-items .todo-item')).toHaveCount(0);
    await page.locator('#todo-completed-toggle').click();
    await expect(page.locator('#todo-completed .todo-item')).toHaveCount(1);
    await page.getByRole('checkbox', { name: '할 것 완료' }).click();
    await expect(page.locator('#todo-items .todo-item')).toHaveCount(1);
  });

  test('완료된 할 일을 현재 목록에서만 일괄 삭제하고 기본은 취소다', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '남길 일');
    await addTodo(page, '끝낸 일 1');
    await addTodo(page, '끝낸 일 2');
    const clear = page.getByRole('button', { name: '완료된 할 일 삭제' });
    await expect(clear).toBeHidden();
    await page.getByRole('checkbox', { name: '끝낸 일 1 완료' }).click();
    await page.getByRole('checkbox', { name: '끝낸 일 2 완료' }).click();
    await page.locator('#todo-new-list').fill('회사');
    await page.keyboard.press('Enter');
    await expect(page.locator('#todo-title')).toHaveText('회사');
    await addTodo(page, '회사에서 끝낸 일');
    await page.getByRole('checkbox', { name: '회사에서 끝낸 일 완료' }).click();
    await page.locator('#todo-smart button', { hasText: '오늘 할 일' }).click();
    await expect(clear).toBeVisible();
    await clear.click();
    await expect(page.locator('.tool-confirm')).toContainText('2개');
    await expect(page.locator('.tool-confirm button:focus')).toHaveText('취소');
    await page.keyboard.press('Enter');
    await expect(page.locator('#todo-completed-toggle')).toContainText('2');
    await clear.click();
    await page.locator('.tool-confirm').getByRole('button', { name: '삭제', exact: true }).click();
    await expect(page.locator('#todo-completed-toggle')).toBeHidden();
    await expect(clear).toBeHidden();
    await expect(page.locator('#todo-items .todo-item')).toHaveCount(1);
    await expect(page.locator('#todo-items .todo-name')).toHaveText('남길 일');
    await page.reload();
    await expect(page.locator('#todo-items .todo-item')).toHaveCount(1);
    await page.locator('#todo-smart button', { hasText: '전체' }).click();
    await expect(page.locator('#todo-completed-toggle')).toContainText('1');
  });

  test('자세히를 연 완료 항목을 일괄 삭제하면 자세히 창을 닫는다', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '끝낸 일');
    await page.locator('.todo-item').click();
    await page.getByRole('checkbox', { name: '끝낸 일 완료' }).click();
    await page.getByRole('button', { name: '완료된 할 일 삭제' }).click();
    await page.locator('.tool-confirm').getByRole('button', { name: '삭제', exact: true }).click();
    await expect(page.locator('#todo-detail')).toBeHidden();
    await expect(page.locator('#todo-input')).toBeFocused();
  });

  test('목록별 등록과 중요 표시, 개수, 다시 열기', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '오늘 일');
    await page.locator('#todo-smart button', { hasText: '중요' }).click();
    await expect(page.locator('#todo-title')).toHaveText('중요');
    await addTodo(page, '중요한 일');
    await expect(page.locator('.todo-star.on')).toHaveCount(1);
    await page.locator('#todo-new-list').fill('회사');
    await page.keyboard.press('Enter');
    await expect(page.locator('#todo-title')).toHaveText('회사');
    await addTodo(page, '회사 일');
    await page.locator('#todo-smart button', { hasText: '전체' }).click();
    await expect(page.locator('.todo-item')).toHaveCount(3);
    await expect(page.locator('#todo-smart button', { hasText: '전체' }).locator('.todo-count')).toHaveText('3');
    await page.reload();
    await expect(page.locator('.todo-item')).toHaveCount(3);
  });

  test('자세히에서 단계, 기한, 반복, 메모를 적고 삭제는 기본이 취소다', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '보고서');
    await page.locator('.todo-item').click();
    const detail = page.getByRole('complementary', { name: '할 일 자세히' });
    await detail.getByRole('textbox', { name: '새 단계' }).fill('초안');
    await page.keyboard.press('Enter');
    await expect(detail.getByRole('checkbox', { name: '초안 완료' })).toBeVisible();
    await expect(detail.getByRole('textbox', { name: '새 단계' })).toBeFocused();
    await detail.getByRole('button', { name: '내일', exact: true }).click();
    await detail.getByLabel('메모').fill('메모');
    await detail.getByLabel('메모').blur();
    await expect(page.locator('.todo-meta')).toContainText('0/1단계');
    await expect(page.locator('.todo-meta')).toContainText('내일');
    await expect(page.locator('.todo-meta')).toContainText('메모');
    await detail.getByRole('button', { name: '삭제', exact: true }).click();
    await expect(page.locator('.tool-confirm button:focus')).toHaveText('취소');
    await page.keyboard.press('Enter');
    await expect(page.locator('.todo-item')).toHaveCount(1);
    await detail.getByRole('button', { name: '삭제', exact: true }).click();
    await page.locator('.tool-confirm').getByRole('button', { name: '삭제', exact: true }).click();
    await expect(page.locator('.todo-item')).toHaveCount(0);
  });

  test('반복 할 일을 완료하면 다음 차례가 생긴다', async ({ page }) => {
    await openTodo(page);
    await page.locator('#todo-smart button', { hasText: '전체' }).click();
    await addTodo(page, '운동');
    await page.locator('.todo-item').click();
    await page.getByRole('complementary', { name: '할 일 자세히' }).getByLabel('반복').selectOption('daily');
    await page.getByRole('checkbox', { name: '운동 완료' }).click();
    await expect(page.locator('#todo-items .todo-item')).toHaveCount(1);
    await expect(page.locator('#todo-items .todo-meta')).toContainText('내일');
  });

  test('끌어서 순서를 바꾸고 검색한다', async ({ page }) => {
    await openTodo(page);
    await addTodo(page, '셋째');
    await addTodo(page, '둘째');
    await addTodo(page, '첫째');
    await page.locator('.todo-item', { hasText: '셋째' }).dragTo(page.locator('.todo-item', { hasText: '첫째' }), { targetPosition: { x: 40, y: 4 } });
    await expect(page.locator('.todo-name').first()).toHaveText('셋째');
    await page.locator('#todo-search').fill('둘');
    await expect(page.locator('.todo-item')).toHaveCount(1);
  });
});

async function openCalendar(page: Page, view: string) {
  await page.clock.setFixedTime(new Date('2026-05-13T10:00:00'));
  await page.goto('/calendar.html');
  await page.locator(`#cal-views [data-view="${view}"]`).click();
  await expect(page.locator(`#cal-views [data-view="${view}"]`)).toHaveAttribute('aria-pressed', 'true');
}
// 저장 응답으로 달력을 다시 그리는 순간에는 요소가 없어 위치가 비어 있을 수 있습니다.
async function boxOf(locator: Locator) {
  let box: Awaited<ReturnType<Locator['boundingBox']>> = null;
  await expect.poll(async () => (box = await locator.boundingBox())).not.toBeNull();
  return box!;
}

async function createEvent(page: Page, title: string) {
  await page.getByRole('textbox', { name: '제목' }).fill(title);
  await page.getByRole('button', { name: '저장', exact: true }).click();
  await expect(page.locator('#event-dialog')).toBeHidden();
}

test.describe('달력', () => {
  test('월간 보기에 한국 공휴일과 대체공휴일을 표시한다', async ({ page }) => {
    await openCalendar(page, 'month');
    await expect(page.locator('#cal-title')).toHaveText('2026년 5월');
    await expect(page.locator('[data-date="2026-05-05"] .cal-holiday')).toHaveText('어린이날');
    await expect(page.locator('[data-date="2026-05-24"] .cal-holiday')).toHaveText('부처님오신날');
    await expect(page.locator('[data-date="2026-05-25"] .cal-holiday')).toContainText('대체공휴일');
    await expect(page.locator('[data-date="2026-05-25"]')).toHaveClass(/sunday/);
    await expect(page.locator('[data-date="2026-05-13"]')).toHaveClass(/today/);
    await expect(page.locator('.cal-day')).toHaveCount(42);
  });

  test('날짜를 더블클릭해 일정을 만들고 다시 열어 고친다', async ({ page }) => {
    await openCalendar(page, 'month');
    await page.locator('[data-date="2026-05-20"]').dblclick({ position: { x: 60, y: 40 } });
    await expect(page.locator('#ev-start-date')).toHaveValue('2026-05-20');
    await createEvent(page, '회의');
    await expect(page.locator('[data-date="2026-05-20"] .cal-chip')).toHaveText('회의');
    await page.reload();
    await expect(page.locator('[data-date="2026-05-20"] .cal-chip')).toHaveText('회의');
    await page.locator('[data-date="2026-05-20"] .cal-chip').click();
    await expect(page.locator('#event-heading')).toHaveText('일정 편집');
    await createEvent(page, '주간 회의');
    await expect(page.locator('[data-date="2026-05-20"] .cal-chip')).toHaveText('주간 회의');
  });

  test('끝이 시작보다 빠르면 저장하지 않는다', async ({ page }) => {
    await openCalendar(page, 'month');
    await page.getByRole('button', { name: '새 일정' }).click();
    await page.getByRole('textbox', { name: '제목' }).fill('잘못');
    await page.locator('#ev-end-date').fill('2026-01-01');
    await page.getByRole('button', { name: '저장', exact: true }).click();
    await expect(page.locator('#ev-error')).toContainText('끝이 시작보다');
    await expect(page.locator('#event-dialog')).toBeVisible();
  });

  test('일정을 끌어 다른 날로 옮긴다', async ({ page }) => {
    await openCalendar(page, 'month');
    await page.locator('[data-date="2026-05-20"]').dblclick({ position: { x: 60, y: 40 } });
    await createEvent(page, '이동');
    await expect(page.locator('[data-date="2026-05-20"] .cal-chip')).toHaveText('이동');
    await page.waitForTimeout(50);
    const from = (await page.locator('[data-date="2026-05-20"] .cal-chip').boundingBox())!;
    const to = (await page.locator('[data-date="2026-05-22"]').boundingBox())!;
    await page.mouse.move(from.x + 5, from.y + 5);
    await page.mouse.down();
    await page.mouse.move(to.x + 30, to.y + 40, { steps: 6 });
    await page.mouse.up();
    await expect(page.locator('[data-date="2026-05-22"] .cal-chip')).toHaveText('이동');
    await expect(page.locator('[data-date="2026-05-20"] .cal-chip')).toHaveCount(0);
  });

  test('주간과 일간 보기에서 시간을 끌어 만들고 일정 시각을 옮긴다', async ({ page }) => {
    await openCalendar(page, 'week');
    await expect(page.locator('#cal-title')).toContainText('5월 10일');
    await expect(page.locator('.cal-column')).toHaveCount(7);
    const column = page.locator('[data-column="2026-05-13"]');
    await column.scrollIntoViewIfNeeded();
    const box = (await column.boundingBox())!;
    const y = (await page.locator('.cal-scroll').boundingBox())!.y + 96;
    await page.mouse.move(box.x + 20, y);
    await page.mouse.down();
    await page.mouse.move(box.x + 20, y + 96, { steps: 4 });
    await page.mouse.up();
    await expect(page.locator('#ev-start-time')).toHaveValue('10:00');
    await expect(page.locator('#ev-end-time')).toHaveValue('12:00');
    await createEvent(page, '집중');
    const event = page.locator('.cal-event', { hasText: '집중' });
    await expect(event).toContainText('10:00~12:00');
    const eventBox = await boxOf(event);
    await page.mouse.move(eventBox.x + 10, eventBox.y + 10);
    await page.mouse.down();
    await page.mouse.move(eventBox.x + 10, eventBox.y + 10 + 48, { steps: 4 });
    await page.mouse.up();
    await expect(page.locator('.cal-event', { hasText: '집중' })).toContainText('11:00~13:00');
    const after = await boxOf(page.locator('.cal-event', { hasText: '집중' }).locator('.cal-resize'));
    await page.mouse.move(after.x + 5, after.y + 3);
    await page.mouse.down();
    await page.mouse.move(after.x + 5, after.y + 3 + 48, { steps: 4 });
    await page.mouse.up();
    await expect(page.locator('.cal-event', { hasText: '집중' })).toContainText('11:00~14:00');
    await page.keyboard.press('d');
    await expect(page.locator('.cal-column')).toHaveCount(1);
    await expect(page.locator('#cal-title')).toContainText('5월 13일');
    await expect(page.locator('.cal-now')).toHaveCount(1);
  });

  test('주간 보기의 머리글과 시간 칸이 맞고 선택한 보기 버튼 글자가 보인다', async ({ page }) => {
    await openCalendar(page, 'week');
    const head = (await page.locator('.cal-allday').nth(3).boundingBox())!;
    const column = (await page.locator('.cal-column').nth(3).boundingBox())!;
    expect(Math.abs(head.x - column.x)).toBeLessThanOrEqual(1);
    expect(Math.abs(head.width - column.width)).toBeLessThanOrEqual(1);
    const pressed = page.locator('#cal-views [aria-pressed=true]');
    await pressed.hover();
    await expect(pressed).toHaveCSS('background-color', 'rgb(122, 92, 0)');
  });

  test('앱이 보낸 공휴일 데이터로 달력을 바꾼다', async ({ page }) => {
    await page.clock.setFixedTime(new Date('2026-05-13T10:00:00'));
    await page.addInitScript(() => {
      const messages: unknown[] = [];
      let receive: ((event: { data: unknown }) => void) | undefined;
      Object.assign(window, { chrome: { webview: { postMessage: (message: unknown) => messages.push(message), addEventListener: (_: string, handler: typeof receive) => { receive = handler; } } }, fromHost: (message: unknown) => receive?.({ data: message }) });
    });
    await page.goto('/calendar.html');
    await expect(page.locator('[data-date="2026-05-07"] .cal-holiday')).toHaveCount(0);
    await page.evaluate(() => (window as any).fromHost({ type: 'holidays.state', years: { 2026: [{ date: '2026-05-07', name: '<img src=x onerror=window.attacked=1>', substitute: false }] } }));
    await expect(page.locator('[data-date="2026-05-07"] .cal-holiday')).toHaveText('<img src=x onerror=window.attacked=1>');
    await expect(page.locator('[data-date="2026-05-05"] .cal-holiday')).toHaveCount(0);
    expect(await page.evaluate(() => 'attacked' in window)).toBe(false);
    await page.evaluate(() => (window as any).fromHost({ type: 'holidays.state', years: 'x' }));
    await expect(page.locator('[data-date="2026-05-05"] .cal-holiday')).toHaveText('어린이날');
  });

  test('겹치는 일정은 나란히 놓는다', async ({ page }) => {
    await page.clock.setFixedTime(new Date('2026-05-13T10:00:00'));
    await page.goto('/calendar.html');
    await page.evaluate(() => {
      const base = { allDay: false, location: '', notes: '', color: 'blue', repeat: 'none', repeatUntil: null, reminderMinutes: null, exceptions: [] };
      localStorage.setItem('devpostit.local.planner', JSON.stringify({ lists: [], todos: [], events: [{ ...base, id: crypto.randomUUID(), title: 'A', start: '2026-05-13T10:00', end: '2026-05-13T11:00' }, { ...base, id: crypto.randomUUID(), title: 'B', start: '2026-05-13T10:30', end: '2026-05-13T11:30' }] }));
      localStorage.setItem('devpostit.calendar.view', 'day');
    });
    await page.reload();
    const a = (await page.locator('.cal-event', { hasText: 'A' }).boundingBox())!;
    const b = (await page.locator('.cal-event', { hasText: 'B' }).boundingBox())!;
    expect(a.x + a.width).toBeLessThanOrEqual(b.x + 1);
  });

  test('목록 보기는 일정, 공휴일, 할 일을 날짜별로 보여 준다', async ({ page }) => {
    await page.clock.setFixedTime(new Date('2026-05-13T10:00:00'));
    await page.goto('/calendar.html');
    await page.evaluate(() => {
      const event = { id: crypto.randomUUID(), title: '월례 회의', start: '2026-05-15', end: '2026-05-15', allDay: true, location: '본사', notes: '', color: 'green', repeat: 'monthly', repeatUntil: null, reminderMinutes: null, exceptions: [] };
      const todo = { id: crypto.randomUUID(), title: '세금 내기', notes: '', listId: 'tasks', important: false, myDay: null, due: '2026-05-18', reminder: null, repeat: 'none', completedAt: null, steps: [], created: '2026-05-01T09:00', order: 0 };
      localStorage.setItem('devpostit.local.planner', JSON.stringify({ lists: [], todos: [todo], events: [event] }));
      localStorage.setItem('devpostit.calendar.view', 'list');
    });
    await page.reload();
    await expect(page.locator('[data-date="2026-05-15"]')).toContainText('월례 회의');
    await expect(page.locator('[data-date="2026-06-15"]')).toContainText('월례 회의');
    await expect(page.locator('[data-date="2026-05-05"]')).toContainText('어린이날');
    await expect(page.locator('[data-date="2026-05-18"]')).toContainText('세금 내기');
    await page.locator('.cal-todo', { hasText: '세금 내기' }).click();
    await expect(page.locator('.cal-todo', { hasText: '세금 내기' })).toHaveClass(/done/);
  });

  test('반복 일정은 이 날만 또는 전체를 삭제한다', async ({ page }) => {
    await openCalendar(page, 'month');
    await page.locator('[data-date="2026-05-11"]').dblclick({ position: { x: 60, y: 40 } });
    await page.locator('#ev-repeat').selectOption('weekly');
    await createEvent(page, '주간');
    await expect(page.locator('.cal-chip', { hasText: '주간' })).toHaveCount(4);
    await page.locator('[data-date="2026-05-18"] .cal-chip').click();
    await page.locator('#ev-delete').click();
    await page.getByRole('button', { name: '이 날만 삭제' }).click();
    await expect(page.locator('[data-date="2026-05-18"] .cal-chip')).toHaveCount(0);
    await expect(page.locator('[data-date="2026-05-25"] .cal-chip')).toHaveCount(1);
    await page.locator('[data-date="2026-05-25"] .cal-chip').click();
    await page.locator('#ev-delete').click();
    await page.getByRole('button', { name: '반복 전체 삭제' }).click();
    await expect(page.locator('.cal-chip')).toHaveCount(0);
  });

  test('이전, 다음, 오늘과 단축키', async ({ page }) => {
    await openCalendar(page, 'month');
    await page.getByRole('button', { name: '다음' }).click();
    await expect(page.locator('#cal-title')).toHaveText('2026년 6월');
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('ArrowLeft');
    await expect(page.locator('#cal-title')).toHaveText('2026년 4월');
    await page.keyboard.press('t');
    await expect(page.locator('#cal-title')).toHaveText('2026년 5월');
    await page.keyboard.press('n');
    await expect(page.getByRole('textbox', { name: '제목' })).toBeFocused();
  });
});
