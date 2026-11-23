import { readFileSync } from "node:fs";

// Exercise the same catalog and stable IDs served by /api/routes/city-options.
const source = readFileSync(new URL("../../../backend/Routes/TurkishCities.cs", import.meta.url), "utf8");
const catalog = source.split("Array.AsReadOnly(new[]")[1].split("});")[0];
export const cities = [...catalog.matchAll(/"([^"]+)"/g)].map((match, i) => ({ id: i + 1, name: match[1] }));
