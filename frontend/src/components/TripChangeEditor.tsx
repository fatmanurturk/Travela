import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import { turkeyInput } from "../tripScheduling";
import { journeyDate } from "../notifications";

type Journey = { departureTime: string; arrivalTime: string; busPlateNumber: string };
type Change = { id: string; reason: string; actorId: number; createdAt: string; beforeJson: string; afterJson: string; affectedTickets: number; releasedHolds: number };
export default function TripChangeEditor({ trip, buses, saved }: { trip: { id: number; busId: number; departureTime: string; arrivalTime: string }; buses: { id: number; plateNumber: string }[]; saved: () => void }) {
  const [history, setHistory] = useState<Change[]>([]); const [version, setVersion] = useState(0); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ busId: String(trip.busId), departure: turkeyInput(trip.departureTime), arrival: turkeyInput(trip.arrivalTime), reason: "" });
  const [preview, setPreview] = useState<{ before: Journey; after: Journey; affectedTickets: number; releasedHolds: number; cancellationUntil: string; changeVersion: number } | null>(null);
  const [key, setKey] = useState(() => crypto.randomUUID());
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl(`/api/trips/${trip.id}/changes`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal, cache: "no-store" }).then(async response => { if (!response.ok) throw new Error("Değişiklik geçmişi alınamadı."); return response.json(); }).then(data => { if (!abort.signal.aborted) { setHistory(data.changes); setVersion(data.changeVersion); } }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [trip.id]);
  const update = (field: keyof typeof form, value: string) => { setForm(current => ({ ...current, [field]: value })); setPreview(null); setKey(crypto.randomUUID()); };
  const submit = async (isPreview: boolean) => {
    if (busy) return; setBusy(true); setError(""); setMessage("");
    try {
      const response = await fetch(apiUrl(`/api/trips/${trip.id}/changes`), { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" }, body: JSON.stringify({ requestKey: key, expectedVersion: version, expectedAffectedTickets: preview?.affectedTickets, preview: isPreview, busId: Number(form.busId), departureTime: new Date(form.departure + "+03:00").toISOString(), arrivalTime: new Date(form.arrival + "+03:00").toISOString(), reason: form.reason }) });
      const data = await response.json(); if (!response.ok) { setPreview(null); throw new Error(data.message ?? "Değişiklik kaydedilemedi."); }
      if (isPreview) setPreview(data); else { setMessage(data.message); setPreview(null); setHistory(current => [data.change, ...current]); setVersion(data.change.version); setKey(crypto.randomUUID()); saved(); }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "İşlem tamamlanamadı."); } finally { setBusy(false); }
  };
  return <section aria-label="Kontrollü sefer değişikliği"><h4>Kontrollü saat / araç değişikliği</h4><p>Firma ve güzergâh korunur. Yeni araç mevcut yolcuların koltuklarını korumalıdır.</p>
    <form className="user-form" onSubmit={event => { event.preventDefault(); void submit(true); }}><div className="form-grid">
      <label>Yeni araç<select aria-label="Yeni araç" required value={form.busId} onChange={event => update("busId", event.target.value)} disabled={busy}>{!buses.some(bus => bus.id === trip.busId) && <option value={trip.busId}>Mevcut araç</option>}{buses.map(bus => <option key={bus.id} value={bus.id}>{bus.plateNumber}</option>)}</select></label>
      <label>Yeni kalkış (Türkiye)<input aria-label="Yeni kalkış (Türkiye)" type="datetime-local" required value={form.departure} onChange={event => update("departure", event.target.value)} disabled={busy} /></label>
      <label>Yeni varış (Türkiye)<input aria-label="Yeni varış (Türkiye)" type="datetime-local" required value={form.arrival} onChange={event => update("arrival", event.target.value)} disabled={busy} /></label>
      <label>Değişiklik gerekçesi<textarea aria-label="Değişiklik gerekçesi" required minLength={10} maxLength={1000} value={form.reason} onChange={event => update("reason", event.target.value)} disabled={busy} /></label>
    </div><button className="secondary-button" type="submit" disabled={busy}>Değişikliği önizle</button></form>
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {preview && <div role="region" aria-label="Değişiklik önizlemesi"><p>Eski: {journeyDate(preview.before.departureTime)} · {preview.before.busPlateNumber}</p><p>Yeni: {journeyDate(preview.after.departureTime)} · {preview.after.busPlateNumber}</p><p>{preview.affectedTickets} bilet etkilenecek; {preview.releasedHolds} ödeme tutması sonlandırılacak. Yolcular {journeyDate(preview.cancellationUntil)} tarihine kadar tam iadeyle iptal edebilir. Bildirimler kuyruğa alınacak.</p><button type="button" className="primary-button" disabled={busy} onClick={() => void submit(false)}>Değişikliği uygula ve bildir</button></div>}
    <h4>Değişiklik geçmişi</h4>{!history.length && <p>Kayıtlı kontrollü değişiklik yok.</p>}{history.map(change => { const before = JSON.parse(change.beforeJson) as Journey; const after = JSON.parse(change.afterJson) as Journey; return <article key={change.id}><p>{journeyDate(change.createdAt)} · İşlemi yapan #{change.actorId}</p><p>{journeyDate(before.departureTime)} / {before.busPlateNumber} → {journeyDate(after.departureTime)} / {after.busPlateNumber}</p><p>{change.reason} · {change.affectedTickets} bilet, {change.releasedHolds} tutma</p></article>; })}
  </section>;
}
