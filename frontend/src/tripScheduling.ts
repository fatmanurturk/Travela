export type ScheduleMode = "single" | "weekly" | "dates";
export type ScheduledTime = { departureTime: string; arrivalTime: string };
export const weekDays = [[1, "Pazartesi"], [2, "Salı"], [3, "Çarşamba"], [4, "Perşembe"], [5, "Cuma"], [6, "Cumartesi"], [0, "Pazar"]] as const;
const dayMs = 86_400_000;
function day(value: string): number {
  const instant = Date.parse(`${value}T00:00:00Z`);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(instant) || new Date(instant).toISOString().slice(0, 10) !== value)
    throw new Error("Geçerli bir tarih seçin.");
  return instant;
}
export function turkeyInput(value: string): string {
  return new Date(Date.parse(value) + 3 * 3_600_000).toISOString().slice(0, 16);
}
export function buildSchedule(departure: string, arrival: string, mode: ScheduleMode, until: string, weekdays: number[], dates: string[], now = Date.now()): ScheduledTime[] {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(departure) || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(arrival))
    throw new Error("Kalkış ve varış tarihlerini girin.");
  day(departure.slice(0, 10)); day(arrival.slice(0, 10));
  const start = Date.parse(departure + ":00+03:00"), end = Date.parse(arrival + ":00+03:00");
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) throw new Error("Varış, kalkıştan sonra olmalıdır.");
  let selected = [departure.slice(0, 10)];
  if (mode === "weekly") {
    const first = day(selected[0]), last = day(until);
    if (last < first || last - first >= 366 * dayMs) throw new Error("Tekrar aralığı en fazla 366 gün olmalı; bitiş başlangıçtan önce olmamalıdır.");
    if (!weekdays.length || weekdays.some(d => !Number.isInteger(d) || d < 0 || d > 6)) throw new Error("En az bir tekrar günü seçin.");
    selected = [];
    for (let value = first; value <= last; value += dayMs) if (weekdays.includes(new Date(value).getUTCDay())) selected.push(new Date(value).toISOString().slice(0, 10));
  } else if (mode === "dates") {
    dates.forEach(day); selected = [...new Set(dates)].sort();
  }
  if (!selected.length || selected.length > 100) throw new Error("Bir işlemde 1 ile 100 arasında sefer seçin.");
  const result = selected.map(date => {
    const at = Date.parse(`${date}T${departure.slice(11)}:00+03:00`);
    if (at <= now) throw new Error("Tüm seferlerin kalkış zamanı gelecekte olmalıdır.");
    return { departureTime: new Date(at).toISOString(), arrivalTime: new Date(at + end - start).toISOString() };
  });
  for (let i = 1; i < result.length; i++) if (result[i].departureTime < result[i - 1].arrivalTime) throw new Error("Seçilen tarihlerdeki seferler birbiriyle çakışıyor.");
  return result;
}
