import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import { turkeyInput } from "../tripScheduling";
import { fetchWithCrewConflict } from "../crewRequests";
import "./TripStopsEditor.css";

export type StopDraft = { cityId: number; terminalId: number; arrivalTime: string; departureTime: string; canBoard: boolean; canAlight: boolean; distanceFromStartKm?: number | null };
export type FareDraft = { fromStopOrder: number; toStopOrder: number; amount: number; isApproved: boolean };
export type JourneyDraft = { stops: StopDraft[]; fares: FareDraft[] };
type City = { id: number; name: string };
type Terminal = { id: number; city: string; name: string; isActive: boolean };
const headers = () => ({ Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" });
export const serializeJourney = (draft: JourneyDraft, shift = 0) => ({ stops: draft.stops.map(s => ({ ...s,
  arrivalTime: new Date(new Date(`${s.arrivalTime}+03:00`).getTime() + shift).toISOString(),
  departureTime: new Date(new Date(`${s.departureTime}+03:00`).getTime() + shift).toISOString() })), fares: draft.fares.filter(f => f.amount > 0) });

export function JourneyFields({ value, onChange, disabled = false, fullPrice }: { value: JourneyDraft; onChange: (draft: JourneyDraft) => void; disabled?: boolean; fullPrice?: number }) {
  const [cities, setCities] = useState<City[]>([]); const [terminals, setTerminals] = useState<Terminal[]>([]); const [error, setError] = useState("");
  useEffect(() => { const abort = new AbortController(); void Promise.all([
    fetch(apiUrl("/api/routes/city-options"), { signal: abort.signal }).then(r => { if (!r.ok) throw Error("Şehirler yüklenemedi."); return r.json(); }),
    fetch(apiUrl("/api/terminals"), { headers: headers(), signal: abort.signal }).then(r => { if (!r.ok) throw Error("Terminaller yüklenemedi."); return r.json(); })
  ]).then(([c,t]) => { if (!abort.signal.aborted) { setCities(c); setTerminals(t); } }).catch(e => { if (!abort.signal.aborted) setError(e.message); }); return () => abort.abort(); }, []);
  const setStop = (index: number, patch: Partial<StopDraft>) => onChange({ ...value, stops: value.stops.map((s,i) => i === index ? { ...s, ...patch } : s) });
  const pairs = value.stops.flatMap((from,i) => from.canBoard ? value.stops.flatMap((to,j) => j > i && to.canAlight ? [{ from: i + 1, to: j + 1 }] : []) : []);
  return <div className="trip-stops-editor">
    <p>Durakları seyahat sırasıyla girin. Varış ve kalkışlarda tam tarih kullanın; saatler Türkiye saatidir. İçinden geçilen noktalarda biniş ve iniş izinlerini kapatın.</p>
    {error && <p role="alert">{error}</p>}
    {value.stops.map((stop,i) => <fieldset key={i} disabled={disabled}><legend>Durak {i + 1}</legend><div className="trip-stop-fields">
      <label>Şehir<select aria-label={`Şehir · Durak ${i + 1}`} required value={stop.cityId || ""} onChange={e => setStop(i, { cityId: Number(e.target.value), terminalId: 0 })}><option value="">Şehir seçin</option>{cities.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      <label>Terminal<select aria-label={`Terminal · Durak ${i + 1}`} required value={stop.terminalId || ""} onChange={e => setStop(i, { terminalId: Number(e.target.value) })}><option value="">Terminal seçin</option>{terminals.filter(t => t.isActive && t.city === cities.find(c => c.id === stop.cityId)?.name).map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <label>Varış<input aria-label={`Varış · Durak ${i + 1}`} type="datetime-local" required value={stop.arrivalTime} onChange={e => setStop(i, { arrivalTime: e.target.value })} /></label>
      <label>Kalkış<input aria-label={`Kalkış · Durak ${i + 1}`} type="datetime-local" required value={stop.departureTime} onChange={e => setStop(i, { departureTime: e.target.value })} /></label>
      <label>Başlangıçtan mesafe (km, isteğe bağlı)<input type="number" min="0" step="0.001" value={stop.distanceFromStartKm ?? ""} onChange={e => setStop(i, { distanceFromStartKm: e.target.value ? Number(e.target.value) : null })} /></label>
      <label><input type="checkbox" checked={stop.canBoard} onChange={e => setStop(i, { canBoard: e.target.checked })} />Yolcu bindirilebilir</label>
      <label><input type="checkbox" checked={stop.canAlight} onChange={e => setStop(i, { canAlight: e.target.checked })} />Yolcu indirilebilir</label>
      {i > 0 && i < value.stops.length - 1 && <button type="button" className="secondary-button" onClick={() => onChange({ stops: value.stops.filter((_,j) => j !== i), fares: [] })}>Durak {i + 1} çıkar</button>}
    </div></fieldset>)}
    <button type="button" className="secondary-button" disabled={disabled || value.stops.length >= 81} onClick={() => onChange({ stops: [...value.stops.slice(0,-1), { cityId: 0, terminalId: 0, arrivalTime: "", departureTime: "", canBoard: true, canAlight: true }, value.stops[value.stops.length - 1]], fares: [] })}>+ Ara durak ekle</button>
    <h4>Kesim fiyatları</h4><p>Fiyatı boş veya onaysız kesimler satışa kapalıdır. Durak ekleyip çıkarınca fiyatları yeniden tanımlayın. Ana güzergâh fiyatı sefer fiyatından gelir.</p>
    {pairs.map(pair => {
      const full = pair.from === 1 && pair.to === value.stops.length;
      const fare = value.fares.find(f => f.fromStopOrder === pair.from && f.toStopOrder === pair.to);
      const setFare = (patch: Partial<FareDraft>) => onChange({ ...value, fares: [...value.fares.filter(f => f.fromStopOrder !== pair.from || f.toStopOrder !== pair.to), { fromStopOrder: pair.from, toStopOrder: pair.to, amount: fare?.amount ?? 0, isApproved: fare?.isApproved ?? false, ...patch }] });
      return <div className="trip-fare-row" key={`${pair.from}:${pair.to}`}><label>Durak {pair.from} → {pair.to} (TL)<input aria-label={`Fiyat · ${pair.from} → ${pair.to}`} type="number" min="0.01" step="0.01" disabled={full} value={full ? fullPrice ?? fare?.amount ?? "" : fare?.amount || ""} onChange={e => setFare({ amount: Number(e.target.value), isApproved: false })} /></label>
        <label><input type="checkbox" aria-label={`Fiyat onayı · ${pair.from} → ${pair.to}`} disabled={full || !fare?.amount} checked={full || !!fare?.isApproved} onChange={e => setFare({ isApproved: e.target.checked })} />{full ? "Ana güzergâh" : "Fiyatı onaylıyorum"}</label>
        {!full && !fare?.isApproved && <small>Satışa kapalı: onaylı fiyat eksik.</small>}
      </div>;
    })}
  </div>;
}

export default function TripStopsEditor({ id, fullPrice, saved }: { id: number; fullPrice: number; saved: () => void }) {
  const [price, setPrice] = useState(fullPrice);
  const [draft, setDraft] = useState<JourneyDraft | null>(null); const [version, setVersion] = useState(0); const [locked, setLocked] = useState(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [revision, setRevision] = useState(0);
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl(`/api/trips/${id}/stops`), { headers: headers(), signal: abort.signal }).then(async r => { if (!r.ok) throw Error("Durak bilgileri yüklenemedi."); return r.json(); }).then(data => {
    if (!abort.signal.aborted) { setDraft({ stops: data.stops.map((s: StopDraft) => ({ ...s, arrivalTime: turkeyInput(s.arrivalTime), departureTime: turkeyInput(s.departureTime) })), fares: data.fares }); setVersion(data.changeVersion); setLocked(data.topologyLocked); setError(""); }
  }).catch(e => { if (!abort.signal.aborted) setError(e.message); }); return () => abort.abort(); }, [id, revision]);
  async function save(suggest = false) {
    if (!draft || busy) return; setBusy(true); setError(""); setMessage("");
    try {
      if (!suggest && draft.stops.some(s => !s.terminalId || !s.arrivalTime || !s.departureTime)) throw Error("Durak şehir, terminal ve saatlerini tamamlayınız.");
      const r = await fetchWithCrewConflict(apiUrl(`/api/trips/${id}/${suggest ? "fares/suggest" : locked ? "fares" : "stops"}`), { method: suggest ? "POST" : "PUT", headers: headers(), ...(!suggest ? { body: JSON.stringify({ ...serializeJourney({ ...draft, fares: draft.fares.filter(f => f.amount > 0) }), fullPrice: price, expectedVersion: version }) } : {}) });
      const data = await r.json(); if (!r.ok) throw Error(data.message ?? "Duraklar kaydedilemedi."); setMessage(data.message); setRevision(v => v + 1); saved();
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <details className="trip-stops-panel"><summary>Duraklar ve kesim fiyatları</summary>{locked && <p>Rezervasyon geçmişi nedeniyle durak sırası, terminaller ve saatler kilitlidir. Kesim fiyatları yeni satışlar için güncellenebilir.</p>}
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {draft && <><label>Ana güzergâh fiyatı (TL)<input aria-label="Ana güzergâh fiyatı" type="number" min="0.01" step="0.01" disabled={busy} value={price} onChange={e => setPrice(Number(e.target.value))} /></label><JourneyFields value={draft} onChange={setDraft} disabled={locked || busy} fullPrice={price} /><div className="trip-stop-actions"><button type="button" className="primary-button" disabled={busy} onClick={() => void save()}>Durak ve fiyatları kaydet</button><button type="button" className="secondary-button" disabled={busy} onClick={() => void save(true)}>Mesafe/süreye göre fiyat öner</button></div></>}
  </details>;
}
