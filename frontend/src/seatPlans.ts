export type SeatPosition = { number: number; row: number; column: number; isWindow: boolean; isAisle: boolean; isBlocked: boolean };

// Rows run from the cab to the rear; columns run from the top window to the bottom window.
// Zero marks the middle door, never a passenger seat.
const coach41Rows = [
  [3, 6, 9, 12, 15, 18, 21, 0, 25, 28, 31, 34, 37, 41],
  [2, 5, 8, 11, 14, 17, 20, 0, 24, 27, 30, 33, 36, 40],
  [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39],
  [1, 4, 7, 10, 13, 16, 19, 22, 23, 26, 29, 32, 35, 38],
];
export const coach41Positions = coach41Rows.flatMap((numbers, column) =>
  numbers.flatMap((number, row) => number ? [{ number, row: row + 1, column: column + 1 }] : []));

export function isCoach41Plan(seats: SeatPosition[], layout: string): boolean {
  return layout === "2+1" && seats.length === 41 && new Set(seats.map(s => s.number)).size === 41 &&
    seats.every(seat => coach41Positions.some(p => p.number === seat.number && p.row === seat.row && p.column === seat.column));
}

export function buildSeats(layout: string, capacity: number, blocked: number[] = []): SeatPosition[] {
  if (!Number.isInteger(capacity) || capacity < 1 || capacity > 1000) return [];
  if (layout === "2+1" && capacity === 41) return coach41Positions.map(position => ({
    ...position, isWindow: position.column === 1 || position.column === 4,
    isAisle: position.column === 2 || position.column === 4, isBlocked: blocked.includes(position.number),
  })).sort((a, b) => a.number - b.number);
  const perRow = layout === "2+1" ? 3 : 4;
  return Array.from({ length: capacity }, (_, i) => {
    const index = i % perRow;
    return { number: i + 1, row: Math.floor(i / perRow) + 1, column: index < 2 ? index + 1 : index + 2,
      isWindow: layout !== "Unspecified" && (index === 0 || index === perRow - 1), isAisle: layout !== "Unspecified" && (index === 1 || index === 2), isBlocked: blocked.includes(i + 1) };
  });
}

// Recognize only the former generated 41-seat template. Keep custom physical plans intact.
export function normalizeSeatPlan(seats: SeatPosition[], layout: string, capacity = seats.length): SeatPosition[] {
  const legacy = layout === "2+1" && capacity === 41 && seats.length === 41 &&
    new Set(seats.map(s => s?.number)).size === 41 && seats.every(s => s &&
      Number.isInteger(s.number) && s.number >= 1 && s.number <= 41 &&
      s.row === Math.floor((s.number - 1) / 3) + 1 && s.column === [1, 2, 4][(s.number - 1) % 3]);
  if (!legacy) return seats;
  const positions = buildSeats("2+1", 41);
  return seats.map(seat => ({ ...seat, ...positions[seat.number - 1], isBlocked: seat.isBlocked }));
}

export function storedSeats(json: string | null | undefined, layout: string, capacity: number): SeatPosition[] {
  if (!json) return buildSeats(layout, capacity);
  try { const seats: unknown = JSON.parse(json); return Array.isArray(seats) ? normalizeSeatPlan(seats as SeatPosition[], layout, capacity) : []; } catch { return []; }
}
export type Journey = { departureTerminalName?: string | null; departureTerminalAddress?: string | null; arrivalTerminalName?: string | null; arrivalTerminalAddress?: string | null; boardingInstructions?: string | null; departureMapUrl?: string | null; arrivalMapUrl?: string | null };
