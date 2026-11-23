import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import { fetchWithCrewConflict } from "../crewRequests";
import TripCrewFields from "./TripCrewFields";
import type { CrewSelection, StaffMember } from "./TripCrewFields";

export default function TripCrewPanel({ tripId, editable, saved }: { tripId: number; editable: boolean; saved: () => void }) {
  const [data, setData] = useState<{ crew: StaffMember[]; changeVersion: number } | null>(null);
  const [value, setValue] = useState<CrewSelection | null>(null);
  const [editing, setEditing] = useState(false); const [revision, setRevision] = useState(0);
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => {
    const abort = new AbortController();
    fetch(apiUrl(`/api/trips/${tripId}/crew`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal, cache: "no-store" })
      .then(async response => { if (!response.ok) throw new Error("Görevli ekip alınamadı."); return response.json(); })
      .then(body => { if (!abort.signal.aborted) { if (!Array.isArray(body.crew)) throw new Error("Görevli ekip alınamadı."); setData(body); setError(""); } })
      .catch(cause => { if (!abort.signal.aborted) setError(cause.message); });
    return () => abort.abort();
  }, [tripId, revision]);
  return <section className="crew-panel" aria-label="Seferin görevli ekibi"><h4>Görevli ekip</h4>
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Yeniden yükle</button></p>}
    {!data ? !error && <p role="status">Ekip yükleniyor...</p> : !data.crew.length ? <p>Henüz personel atanmadı.</p> : <ul>{data.crew.map(person => <li key={person.id}>{person.fullName} · {person.duty === "Driver" ? "Şoför" : "Muavin"}{!person.isActive ? " · Pasif" : ""}</li>)}</ul>}
    {editable && data && !editing && <button type="button" className="secondary-button" onClick={() => { setValue(null); setEditing(true); }}>Ekibi düzenle</button>}
    {editing && data && <form onSubmit={async event => {
      event.preventDefault(); if (!value || busy) return; setBusy(true); setError("");
      try {
        const response = await fetchWithCrewConflict(apiUrl(`/api/trips/${tripId}/crew`), { method: "PUT", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" }, body: JSON.stringify({ crew: value, expectedVersion: data.changeVersion }) });
        const body = await response.json(); if (!response.ok) throw new Error(body.message ?? "Ekip kaydedilemedi.");
        setEditing(false); setRevision(v => v + 1); saved();
      } catch (cause) { setError(cause instanceof Error ? cause.message : "Ekip kaydedilemedi."); }
      finally { setBusy(false); }
    }}><TripCrewFields tripId={tripId} value={value} onChange={setValue} disabled={busy} />
      <div className="detail-actions"><button type="submit" className="primary-button" disabled={busy || !value}>Ekibi kaydet</button><button type="button" className="secondary-button" disabled={busy} onClick={() => setEditing(false)}>Vazgeç</button></div></form>}
  </section>;
}
