import type { SeatPosition } from "./seatPlans";

export function planError(seats: SeatPosition[], layout: string, capacity: number): string {
  if (!["2+1", "2+2", "Custom"].includes(layout)) return "Gerçek otobüsün koltuk düzenini seçiniz.";
  if (!Number.isInteger(capacity) || capacity < 1 || capacity > 1000 || seats.length !== capacity) return `Plan tam ${capacity} koltuk içermelidir.`;
  if (seats.some(s => !s || !Number.isInteger(s.number) || s.number < 1 || s.number > capacity || !Number.isInteger(s.row) || s.row < 1 || s.row > 1000 || ![1, 2, 4, 5].includes(s.column) || (layout === "2+1" && s.column === 5))) return "Koltuk numarası, satır veya sütun geçersiz. Koridor sütununa koltuk yerleştirilemez.";
  if (new Set(seats.map(s => s.number)).size !== capacity) return "Koltuk numaraları benzersiz olmalıdır.";
  if (new Set(seats.map(s => `${s.row}:${s.column}`)).size !== capacity) return "Aynı fiziksel konuma iki koltuk yerleştirilemez.";
  if (seats.every(s => s.isBlocked)) return "En az bir koltuk satışa açık olmalıdır.";
  return "";
}

export function seatPositionFlags(seats: SeatPosition[]): SeatPosition[] {
  return seats.map(s => ({ ...s, isWindow: s.column === 1 || s.column === 5 || (s.column === 4 && !seats.some(p => p.row === s.row && p.column === 5)), isAisle: s.column === 2 || s.column === 4 || (s.column === 1 && !seats.some(p => p.row === s.row && p.column === 2)) }));
}
