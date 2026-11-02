// 한국 공휴일 계산입니다. 「공휴일에 관한 법률」과 「관공서의 공휴일에 관한 규정」의 현재 규칙을 따릅니다.
// 음력 날짜는 브라우저의 단기력(dangi)으로 계산하므로 해마다 표를 고칠 필요가 없습니다.
// 선거일과 임시공휴일은 미리 알 수 없어 표로 둡니다. 새로 지정되면 이 표를 고칩니다.
export type Holiday = { date: string; name: string; substitute: boolean };

const announced: Record<string, string> = {
  '2022-03-09': '대통령 선거일', '2022-06-01': '전국동시지방선거일', '2023-10-02': '임시공휴일', '2024-04-10': '국회의원 선거일',
  '2024-10-01': '임시공휴일(국군의 날)', '2025-01-27': '임시공휴일', '2025-06-03': '대통령 선거일', '2026-06-03': '전국동시지방선거일',
};
const pad = (value: number) => String(value).padStart(2, '0');
const key = (year: number, month: number, day: number) => `${year}-${pad(month)}-${pad(day)}`;
const utc = (text: string) => { const [y, m, d] = text.split('-').map(Number); return new Date(Date.UTC(y, m - 1, d)); };
const text = (date: Date) => key(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
const shift = (value: string, days: number) => text(new Date(utc(value).getTime() + days * 86400000));

const lunarFormat = new Intl.DateTimeFormat('en-u-ca-dangi', { month: 'numeric', day: 'numeric', timeZone: 'UTC' });
// 윤달은 월 값이 숫자만이 아니므로 평달만 찾습니다.
function fromLunar(year: number, month: number, day: number): string | null {
  for (let date = new Date(Date.UTC(year, 0, 1)); date.getUTCFullYear() === year; date = new Date(date.getTime() + 86400000)) {
    const parts = lunarFormat.formatToParts(date);
    if (parts.find(part => part.type === 'month')?.value === String(month) && parts.find(part => part.type === 'day')?.value === String(day)) return text(date);
  }
  return null;
}

type Rule = 'none' | 'sunday' | 'weekend';
type Base = { date: string; name: string; rule: Rule; overlap: boolean };
function baseHolidays(year: number): Base[] {
  const result: Base[] = [];
  const add = (date: string | null, name: string, rule: Rule, overlap = rule !== 'none') => { if (date) result.push({ date, name, rule, overlap }); };
  const from = (start: number): Rule => (year >= start ? 'weekend' : 'none');
  add(key(year, 1, 1), '신정', 'none');
  add(key(year, 3, 1), '3·1절', from(2021));
  if (year >= 2026) add(key(year, 5, 1), '노동절', 'weekend');
  add(key(year, 5, 5), '어린이날', from(2014));
  add(key(year, 6, 6), '현충일', 'none');
  if (year >= 2026 || year <= 2007) add(key(year, 7, 17), '제헌절', year >= 2026 ? 'weekend' : 'none');
  add(key(year, 8, 15), '광복절', from(2021));
  add(key(year, 10, 3), '개천절', from(2021));
  if (year >= 2013) add(key(year, 10, 9), '한글날', from(2021));
  add(key(year, 12, 25), '성탄절', from(2023));
  add(fromLunar(year, 4, 8), '부처님오신날', from(2023));
  // 설날과 추석 연휴는 토요일과 겹쳐도 대체하지 않습니다.
  const family = year >= 2014 ? 'sunday' : 'none';
  for (const [name, month, day] of [['설날', 1, 1], ['추석', 8, 15]] as const) {
    const middle = fromLunar(year, month, day);
    if (!middle) continue;
    add(shift(middle, -1), `${name} 연휴`, family);
    add(middle, name, family);
    add(shift(middle, 1), `${name} 연휴`, family);
  }
  for (const [date, name] of Object.entries(announced)) if (date.startsWith(`${year}-`)) add(date, name, 'none');
  return result;
}

const cache = new Map<number, Holiday[]>();
export function holidaysOf(year: number): Holiday[] {
  const known = cache.get(year);
  if (known) return known;
  const base = baseHolidays(year);
  const taken = new Set(base.map(item => item.date));
  const result: Holiday[] = base.map(item => ({ date: item.date, name: item.name, substitute: false }));
  for (const date of [...new Set(base.map(item => item.date))].sort()) {
    const here = base.filter(item => item.date === date);
    const day = utc(date).getUTCDay();
    // 같은 날 겹치는 공휴일이 둘이어도 대체공휴일은 하루입니다.
    const reason = here.find(item => (item.rule === 'weekend' && (day === 0 || day === 6)) || (item.rule === 'sunday' && day === 0) || (item.overlap && here.length > 1));
    if (!reason) continue;
    let next = shift(date, 1);
    while (taken.has(next) || utc(next).getUTCDay() === 0) next = shift(next, 1);
    taken.add(next);
    result.push({ date: next, name: `대체공휴일(${reason.name.replace(' 연휴', '')})`, substitute: true });
  }
  result.sort((a, b) => a.date.localeCompare(b.date));
  cache.set(year, result);
  return result;
}
export const holidaysOn = (date: string): Holiday[] => holidaysOf(Number(date.slice(0, 4))).filter(item => item.date === date);
