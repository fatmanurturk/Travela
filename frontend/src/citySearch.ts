export type CityOption = { id: number; name: string };
export type CitySuggestion = { city: CityOption; kind: "exact" | "prefix" | "contains" | "similar" };

export const compareCityNames = (a: CityOption, b: CityOption) => a.name.localeCompare(b.name, "tr-TR");

export function normalizeCityQuery(value: string): string {
  return value.trim().toLocaleLowerCase("tr-TR").normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "").replace(/ı/g, "i");
}

// Optimal string alignment distance also counts adjacent transpositions as one typo.
function editDistance(a: string, b: string): number {
  const rows = Array.from({ length: a.length + 1 }, (_, i) => [i]);
  rows[0] = Array.from({ length: b.length + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    for (let j = 1; j <= b.length; j++) {
      rows[i][j] = Math.min(rows[i - 1][j] + 1, rows[i][j - 1] + 1,
        rows[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        rows[i][j] = Math.min(rows[i][j], rows[i - 2][j - 2] + 1);
      }
    }
  }
  return rows[a.length][b.length];
}

export function suggestCities(cities: readonly CityOption[], value: string): CitySuggestion[] {
  const query = normalizeCityQuery(value);
  const ranked = cities.flatMap<CitySuggestion & { rank: number; distance: number }>(city => {
    const name = normalizeCityQuery(city.name);
    if (!query || name === query) return [{ city, kind: "exact" as const, rank: 0, distance: 0 }];
    if (name.startsWith(query)) return [{ city, kind: "prefix" as const, rank: 1, distance: 0 }];
    if (name.includes(query)) return [{ city, kind: "contains" as const, rank: 2, distance: 0 }];
    const threshold = query.length < 4 ? 0 : query.length <= 5 ? 1 : 2;
    if (!threshold || Math.abs(query.length - name.length) > threshold) return [];
    const distance = editDistance(query, name);
    return distance <= threshold && distance / Math.max(query.length, name.length) <= 0.3
      ? [{ city, kind: "similar" as const, rank: 3, distance }] : [];
  });
  return ranked.sort((a, b) => a.rank - b.rank || a.distance - b.distance || compareCityNames(a.city, b.city))
    .map(({ city, kind }) => ({ city, kind }));
}
