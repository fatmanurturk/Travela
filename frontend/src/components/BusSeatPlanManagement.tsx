import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { apiUrl } from "../api";
import type { SeatPosition } from "../seatPlans";
import PhysicalSeatPlanEditor from "./PhysicalSeatPlanEditor";

type PlanTrip = { id: number; departureCity: string; arrivalCity: string; departureTime: string; seatLayout: string; activeReservations: number; canApply: boolean; reason: string | null };
type ManagedPlan = { id: number; plateNumber: string; capacity: number; layout: string; seats: SeatPosition[]; verified: boolean; version: string; trips: PlanTrip[] };
export default function BusSeatPlanManagement({ id, close, saved }: { id: number; close: () => void; saved: () => void }) {
  const [plan, setPlan] = useState<ManagedPlan | null>(null); const [targets, setTargets] = useState<number[]>([]); const [revision, setRevision] = useState(0);
  const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [loading, setLoading] = useState(true);
  const panel = useRef<HTMLElement>(null);
  useEffect(() => { panel.current?.focus({ preventScroll: true }); panel.current?.scrollIntoView({ block: "start", behavior: "smooth" }); }, []);
  useEffect(() => { const abort = new AbortController(); setLoading(true); setError("");
    fetch(apiUrl(`/api/buses/${id}/seat-plan`), { signal: abort.signal, cache: "no-store", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` } })
      .then(async response => { const data = await response.json(); if (!response.ok) throw new Error(data.message ?? "Koltuk planı yönetimi yüklenemedi."); return data as ManagedPlan; })
      .then(data => { if (!abort.signal.aborted) { setPlan(data); setTargets([]); } }).catch(e => { if (!abort.signal.aborted) setError(e.message); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [id, revision]);
  return <section ref={panel} tabIndex={-1} className="dashboard-section" aria-label="Koltuk Planı Yönetimi">
    <div className="plan-management-header"><div><h2>Koltuk Planı Yönetimi</h2><p>{plan ? `${plan.plateNumber} · Otobüs #${id} · ${plan.capacity} koltuk` : `Otobüs #${id}`}</p></div><button type="button" className="secondary-button" onClick={close}>Plan yönetimini kapat</button></div>
    {loading && <p role="status">Koltuk planı ve bağlı seferler yükleniyor...</p>}{error && <p role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></p>}{message && <p className="plan-success" role="status">{message}</p>}
    {plan && !loading && !error && <><p><strong>{plan.verified ? `Doğrulanmış plan: ${plan.layout}` : "Fiziksel plan doğrulanmamış · Seferler otomatik satışa açılmaz."}</strong></p>
      <PhysicalSeatPlanEditor key={`${id}:${plan.version}:${revision}`} path={`/api/buses/${id}/seat-plan`} capacity={plan.capacity} initialLayout={plan.layout} initialSeats={plan.seats} confirmationScope={targets.join(",")} save={async draft => {
        const response = await fetch(apiUrl(`/api/buses/${id}/seat-plan`), { method: "PUT", headers: { "Content-Type": "application/json", Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, body: JSON.stringify({ ...draft, applyToTripIds: targets, expectedPlanVersion: plan.version }) });
        const data = await response.json(); if (!response.ok) throw new Error(data.message ?? "Koltuk planı kaydedilemedi.");
        setMessage(data.message); setRevision(v => v + 1); saved(); return data.message;
      }}>
        <h3>Planın aktarılacağı mevcut seferler</h3><p>Yalnızca seçtiğiniz, kalkışı gelmemiş, rezervasyonsuz ve fiziksel planı doğrulanmamış seferler güncellenir. Diğer seferlerin planı korunur. Seçim yapmazsanız plan yalnızca yeni seferlerde kullanılır.</p>
        {plan.trips.length === 0 ? <p>Bu otobüse bağlı sefer bulunmuyor.</p> : <div className="plan-target-list">{plan.trips.map(trip => <label className="plan-target-label" key={trip.id}><input type="checkbox" checked={targets.includes(trip.id)} disabled={!trip.canApply} onChange={e => setTargets(current => e.target.checked ? [...current, trip.id].sort((a, b) => a - b) : current.filter(n => n !== trip.id))} /><span>Sefer #{trip.id} · {trip.departureCity} → {trip.arrivalCity}<small>{new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short", timeZone: "Europe/Istanbul" }).format(new Date(trip.departureTime))} · {trip.activeReservations} aktif kayıt · {trip.reason ?? "Plan aktarılabilir"}</small></span></label>)}</div>}
      </PhysicalSeatPlanEditor>
      {message && <p>{plan.trips.filter(t => t.seatLayout !== "Unspecified").map(trip => <Link key={trip.id} to={`/trips/${trip.id}`} style={{ marginRight: 16 }}>Sefer #{trip.id} koltuklarını görüntüle →</Link>)}</p>}
    </>}
  </section>;
}
