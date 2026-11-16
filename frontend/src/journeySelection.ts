export type JourneySelection = { boardingStopOrder?: number | null; alightingStopOrder?: number | null };
export function journeyQuery(selection: JourneySelection): string {
  const { boardingStopOrder: from, alightingStopOrder: to } = selection;
  return Number.isInteger(from) && Number.isInteger(to) && from! > 0 && from! < to!
    ? `?boardingStopOrder=${from}&alightingStopOrder=${to}` : "";
}
export function tripPath(id: number, selection: JourneySelection): string { return `/trips/${id}${journeyQuery(selection)}`; }
