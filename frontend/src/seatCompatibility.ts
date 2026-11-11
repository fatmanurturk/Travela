import type { SeatPosition } from "./seatPlans";

export type Gender = "Female" | "Male";
export type SeatOccupancy = { seatNumber: number; gender: Gender | null; kind: string; expiresAt: string | null };
export type SunExposure = { side: "Left" | "Right" | "Mixed" | "Night" | "Unknown"; message: string; leftMinutes: number; rightMinutes: number; nightMinutes: number };
export function adjacentSeat(plan: SeatPosition[], seat: SeatPosition) {
  const column = ({ 1: 2, 2: 1, 4: 5, 5: 4 } as Record<number, number>)[seat.column];
  return plan.find(s => s.row === seat.row && s.column === column);
}
export function seatConflict(plan: SeatPosition[], number: number, gender: Gender | "" | undefined,
  occupied: SeatOccupancy[], selected: Record<number, Gender | ""> = {}) {
  const seat = plan.find(s => s.number === number);
  if (!seat) return "Koltuk planı bulunamıyor.";
  const neighbor = adjacentSeat(plan, seat);
  if (!neighbor) return "";
  const taken = occupied.find(s => s.seatNumber === neighbor.number);
  if (taken && !taken.gender) return "Yan koltuktaki yolcunun cinsiyet bilgisi doğrulanamadığı için bu koltuk satışa uygun değil.";
  const required = taken?.gender ?? selected[neighbor.number];
  if (required && gender && required !== gender) return `Bu koltuğun yanındaki yolcu ${required === "Female" ? "kadın" : "erkek"} olduğu için yalnızca ${required === "Female" ? "kadın" : "erkek"} yolcular rezervasyon yapabilir.`;
  return "";
}
