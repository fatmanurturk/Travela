import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import "./TripStopsEditor.css";

type Stop = { cityId: number; terminalId: number; arrivalOffsetMinutes: number; departureOffsetMinutes: number; canBoard: boolean; canAlight: boolean };
export default function RouteStopsEditor({ id, departureCity, arrivalCity }: { id: number; departureCity: string; arrivalCity: string }) {
  const [stops, setStops] = useState<Stop[]>([]); const [cities, setCities] = useState<{ id: number; name: string }[]>([]);
  const [terminals, setTerminals] = useState<{ id: number; name: string; city: string; isActive: boolean }[]>([]); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const headers = { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" };
  useEffect(() => { const abort = new AbortController(); const headers = { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` };
    void Promise.all([`/api/routes/${id}/stops`, "/api/routes/city-options", "/api/terminals"].map(path => fetch(apiUrl(path), { headers, signal: abort.signal }).then(r => { if (!r.ok) throw Error("Durak şablonu veya katalog yüklenemedi."); return r.json(); })))
      .then(([s,c,t]) => { if (!abort.signal.aborted) { setStops(s); setCities(c); setTerminals(t); } }).catch(e => { if (!abort.signal.aborted) setError(e.message); }); return () => abort.abort();
  }, [id]);
  const update = (index: number, patch: Partial<Stop>) => setStops(stops => stops.map((s,i) => i === index ? { ...s, ...patch } : s));
  const blank = (): Stop => ({ cityId: 0, terminalId: 0, arrivalOffsetMinutes: 0, departureOffsetMinutes: 0, canBoard: true, canAlight: true });
  return <details className="trip-stops-panel"><summary>Güzergâhın sıralı durak şablonu</summary><p>Şablon yeni seferlere uygulanır. Süreleri ilk kalkıştan itibaren dakika olarak girin; 1440 dakikayı aşan değerler sonraki günlere karşılık gelir. Mevcut sefer ve biletler korunur.</p>
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {!stops.length ? <button type="button" className="secondary-button" onClick={() => setStops([{ ...blank(), cityId: cities.find(c => c.name === departureCity)?.id ?? 0, canAlight: false }, { ...blank(), cityId: cities.find(c => c.name === arrivalCity)?.id ?? 0, canBoard: false }])}>Durak şablonu oluştur</button> : <div className="trip-stops-editor">
      {stops.map((s,i) => <fieldset disabled={busy} key={i}><legend>Durak {i + 1}</legend><div className="trip-stop-fields">
        <label>Şehir<select value={s.cityId || ""} onChange={e => update(i, { cityId: Number(e.target.value), terminalId: 0 })}><option value="">Şehir seçin</option>{cities.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
        <label>Terminal<select value={s.terminalId || ""} onChange={e => update(i, { terminalId: Number(e.target.value) })}><option value="">Terminal seçin</option>{terminals.filter(t => t.isActive && t.city === cities.find(c => c.id === s.cityId)?.name).map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
        <label>Varış (başlangıçtan dakika)<input type="number" min="0" max="100000" value={s.arrivalOffsetMinutes} onChange={e => update(i, { arrivalOffsetMinutes: Number(e.target.value) })} /></label>
        <label>Kalkış (başlangıçtan dakika)<input type="number" min="0" max="100000" value={s.departureOffsetMinutes} onChange={e => update(i, { departureOffsetMinutes: Number(e.target.value) })} /></label>
        <label><input type="checkbox" checked={s.canBoard} onChange={e => update(i, { canBoard: e.target.checked })} />Yolcu bindirilebilir</label><label><input type="checkbox" checked={s.canAlight} onChange={e => update(i, { canAlight: e.target.checked })} />Yolcu indirilebilir</label>
        {i > 0 && i < stops.length - 1 && <button type="button" className="secondary-button" onClick={() => setStops(current => current.filter((_,j) => i !== j))}>Durağı çıkar</button>}
      </div></fieldset>)}
      <div className="trip-stop-actions"><button type="button" className="secondary-button" disabled={busy || stops.length >= 81} onClick={() => setStops(current => [...current.slice(0,-1), blank(), current[current.length - 1]])}>+ Ara durak ekle</button>
        <button type="button" className="primary-button" disabled={busy} onClick={async () => { setBusy(true); setError(""); setMessage(""); try {
          const r = await fetch(apiUrl(`/api/routes/${id}/stops`), { method: "PUT", headers, body: JSON.stringify({ stops }) }); const data = await r.json(); if (!r.ok) throw Error(data.message ?? "Şablon kaydedilemedi."); setMessage(data.message);
        } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Durak şablonunu kaydet</button></div>
    </div>}
  </details>;
}
