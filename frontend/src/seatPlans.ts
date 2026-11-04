export type SeatPosition = { number: number; row: number; column: number; isWindow: boolean; isAisle: boolean; isBlocked: boolean };
export function buildSeats(layout: string, capacity: number, blocked: number[] = []): SeatPosition[] {
  if (!Number.isInteger(capacity) || capacity < 1 || capacity > 1000) return [];
  const perRow = layout === "2+1" ? 3 : 4;
  return Array.from({ length: capacity }, (_, i) => {
    const index = i % perRow;
    return { number: i + 1, row: Math.floor(i / perRow) + 1, column: index < 2 ? index + 1 : index + 2,
      isWindow: layout !== "Unspecified" && (index === 0 || index === perRow - 1), isAisle: layout !== "Unspecified" && (index === 1 || index === 2), isBlocked: blocked.includes(i + 1) };
  });
}
export function storedSeats(json: string | null | undefined, layout: string, capacity: number): SeatPosition[] {
  if (!json) return buildSeats(layout, capacity);
  try { const seats: unknown = JSON.parse(json); return Array.isArray(seats) ? seats as SeatPosition[] : []; } catch { return []; }
}
export type Journey = { departureTerminalName?: string | null; departureTerminalAddress?: string | null; arrivalTerminalName?: string | null; arrivalTerminalAddress?: string | null; boardingInstructions?: string | null; departureMapUrl?: string | null; arrivalMapUrl?: string | null };
