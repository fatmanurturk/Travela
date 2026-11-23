import { useEffect, useState } from "react";
import type { Dispatch, SetStateAction } from "react";
import { apiUrl } from "../api";
import "./TripCrew.css";

export type CrewSelection = { drivers: number[]; attendants: number[] };
export type StaffMember = { id: number; fullName: string; duty: "Driver" | "Attendant"; currentDuty?: string; isActive: boolean; phone?: string | null };
export default function TripCrewFields({ tripId, value, onChange, disabled = false }: {
  tripId?: number; value: CrewSelection | null; onChange: Dispatch<SetStateAction<CrewSelection | null>>; disabled?: boolean;
}) {
  const [people, setPeople] = useState<StaffMember[]>([]);
  const [assigned, setAssigned] = useState<StaffMember[]>([]);
  const [loading, setLoading] = useState(true); const [error, setError] = useState(""); const [revision, setRevision] = useState(0);
  useEffect(() => {
    const abort = new AbortController(); setLoading(true); setError("");
    const headers = { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` };
    const read = async (path: string) => { const r = await fetch(apiUrl(path), { headers, signal: abort.signal, cache: "no-store" }); if (!r.ok) throw new Error("Personel listesi alınamadı. Ekibi değiştirmek için yeniden yükleyin."); return r.json(); };
    Promise.all([read("/api/company/staff"), tripId ? read(`/api/trips/${tripId}/crew`) : Promise.resolve({ crew: [] })])
      .then(([list, current]) => {
        if (abort.signal.aborted) return;
        if (!Array.isArray(list) || !Array.isArray(current.crew)) throw new Error("Personel verileri alınamadı.");
        setPeople(list); setAssigned(current.crew);
        onChange(previous => previous ?? { drivers: current.crew.filter((s: StaffMember) => s.duty === "Driver").map((s: StaffMember) => s.id), attendants: current.crew.filter((s: StaffMember) => s.duty === "Attendant").map((s: StaffMember) => s.id) });
      }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [tripId, revision, onChange]);
  return <section className="crew-fields" aria-label="Görevli ekip seçimi"><h3>Görevli ekip</h3><p>Personel atamak isteğe bağlıdır.</p>
    {loading && <p role="status">Personeller yükleniyor...</p>}{error && <p role="alert">{error}</p>}
    <div className="crew-options">{([ ["drivers", "Driver", "Şoförler"], ["attendants", "Attendant", "Muavinler"] ] as const).map(([field, duty, label]) => {
      const ids = value?.[field] ?? [];
      const options = [...people.filter(s => s.isActive && s.duty === duty), ...assigned.filter(s => s.duty === duty && !people.some(p => p.id === s.id && p.isActive && p.duty === duty))];
      return <fieldset key={field} disabled={disabled || loading || !!error}><legend>{label}</legend>
        {options.map(person => <label className="crew-choice" key={person.id}><input type="checkbox" checked={ids.includes(person.id)}
          disabled={((!person.isActive || person.currentDuty != null && person.currentDuty !== duty) && !ids.includes(person.id)) || (value?.[field === "drivers" ? "attendants" : "drivers"] ?? []).includes(person.id)}
          onChange={event => { const checked = event.target.checked; onChange(current => ({ ...(current ?? { drivers: [], attendants: [] }), [field]: checked ? [...new Set([...(current?.[field] ?? []), person.id])] : (current?.[field] ?? []).filter(id => id !== person.id) })); }} />
          <span>{person.fullName}{!person.isActive && <small>Pasif — mevcut atama</small>}{person.currentDuty && person.currentDuty !== duty && <small>Görevi değişmiş — mevcut atama</small>}</span></label>)}
        {!people.some(s => s.isActive && s.duty === duty) && <p>Aktif {duty === "Driver" ? "şoför" : "muavin"} kaydı bulunmuyor.</p>}
        <a href="/company/staff" target="_blank" rel="noopener noreferrer">Personel ekle (yeni sekme)</a>
      </fieldset>;
    })}</div>
    <button type="button" className="secondary-button" disabled={loading || disabled} onClick={() => setRevision(v => v + 1)}>Personel listesini yenile</button>
  </section>;
}
