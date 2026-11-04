import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import { deliveryLabel, journeyDate, notificationLabel } from "../notifications";
type Change = { id: string; reason: string; beforeJson: string; afterJson: string; createdAt: string };
type Delivery = { id: string; type: string; channel: string; status: string };
export default function TicketCommunications({ pnr, revision }: { pnr: string; revision?: string }) {
  const [data, setData] = useState<{ changes: Change[]; notifications: Delivery[] } | null>(null); const [error, setError] = useState("");
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl(`/api/tickets/${pnr}/communications`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal, cache: "no-store" }).then(async response => { if (!response.ok) throw new Error("Sefer değişikliği ve bildirim geçmişi alınamadı."); return response.json(); }).then(result => { if (!abort.signal.aborted) { setData(result); setError(""); } }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [pnr, revision]);
  return <section aria-label="Sefer değişikliği ve bildirim geçmişi"><h3>Sefer değişikliği ve bildirim geçmişi</h3>{error && <p role="status">{error}</p>}
    {data?.changes.map(change => { const before = JSON.parse(change.beforeJson); const after = JSON.parse(change.afterJson); return <article key={change.id}><p>{journeyDate(change.createdAt)} · {change.reason}</p><p>Önceki: {journeyDate(before.departureTime)} · {before.busPlateNumber}</p><p>Yeni: {journeyDate(after.departureTime)} · {after.busPlateNumber}</p></article>; })}
    {data?.notifications.map(item => <p key={item.id}>{notificationLabel(item.type)} · {item.channel === "Sms" ? "SMS" : "E-posta"} · {deliveryLabel(item.status)}</p>)}
  </section>;
}
