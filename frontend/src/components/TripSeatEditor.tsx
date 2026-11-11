import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import SeatMap from "./SeatMap";
import type { SeatPosition } from "../seatPlans";
import PhysicalSeatPlanEditor from "./PhysicalSeatPlanEditor";

export default function TripSeatEditor({ id, saved }: { id: number; saved: () => void }) {
  const [plan, setPlan] = useState<{ seats: SeatPosition[]; occupiedSeats: number[]; seatLayout: string; seatCapacity: number; seatPlanAvailable: boolean } | null>(null);
  const [closed, setClosed] = useState<number[]>([]); const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [revision, setRevision] = useState(0);
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl(`/api/trips/public/${id}`), { signal: abort.signal, cache: "no-store" })
    .then(async response => { if (!response.ok) throw new Error("Koltuk planı alınamadı."); return response.json(); })
    .then(data => { if (!abort.signal.aborted) { setPlan(data); setClosed((data.seats ?? []).filter((seat: SeatPosition) => seat.isBlocked).map((seat: SeatPosition) => seat.number)); } })
    .catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [id, revision]);
  return <section><h4>Seferde satışa kapalı koltuklar</h4>{error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {plan && <PhysicalSeatPlanEditor key={`${id}:${revision}`} path={`/api/trips/${id}/seat-plan`} capacity={plan.seatCapacity} initialLayout={plan.seatLayout} initialSeats={plan.seats} saved={() => { setRevision(v => v + 1); saved(); }} />}
    {plan?.seats && <><SeatMap layout={plan.seatLayout} seats={plan.seats.map(seat => ({ ...seat, isBlocked: closed.includes(seat.number) }))} occupied={plan.occupiedSeats} mode="block" disabled={busy} onChoose={number => setClosed(current => current.includes(number) ? current.filter(value => value !== number) : [...current, number])} />
    <p>Satılmış ve ödeme için tutulmuş koltuklar değiştirilemez. Bu işlem yalnızca bu seferin satışa kapalı koltuklarını değiştirir.</p>
    <button type="button" className="secondary-button" disabled={busy || !plan.seatPlanAvailable} onClick={async () => {
      setBusy(true); setError(""); setMessage(""); try { const response = await fetch(apiUrl(`/api/trips/${id}/seat-blocks`), { method: "PUT", headers: { "Content-Type": "application/json", Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, body: JSON.stringify({ blockedSeats: closed }) }); const data = await response.json(); if (!response.ok) throw new Error(data.message ?? "Koltuklar kaydedilemedi."); setMessage(data.message); saved(); } catch (cause) { setError(cause instanceof Error ? cause.message : "İşlem tamamlanamadı."); } finally { setBusy(false); }
    }}>{busy ? "Kaydediliyor..." : "Sefer koltuklarını kaydet"}</button></>}
  </section>;
}
