import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import { safeProviderCheckout } from "../payments";
import type { Purchase } from "../payments";
import SiteHeader from "../components/SiteHeader";
import TicketCard from "../components/TicketCard";
import JourneyStops from "../components/JourneyStops";
import { money, dateTime } from "../reservations";
import "../campaigns.css";
import PendingLegalAcceptance from "../components/PendingLegalAcceptance";
type Group = {
  id: string;
  amount: number;
  originalAmount: number;
  discountAmount: number;
  campaignName: string | null;
  couponCode: string | null;
  provider: string;
  isTest: boolean;
  paymentStatus: string;
  checkoutState: string;
  checkoutUrl: string | null;
  lastError: string | null;
  needsAttention: boolean;
  holdExpiresAt: string;
  refundRequested: number;
  refunded: number;
  tickets: Purchase[];
  legalAccepted?: boolean;
};
export default function GroupCheckoutPage() {
  const { id } = useParams(); return <GroupCheckout key={id} id={id ?? ""} />;
}
function GroupCheckout({ id }: { id: string }) {
  const [group, setGroup] = useState<Group | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [revision, setRevision] = useState(0); const navigate = useNavigate();
  useEffect(() => { const abort = new AbortController(); const load = async () => { try { const response = await fetch(apiUrl(`/api/purchases/${id}`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal, cache: "no-store" }); if (response.status === 401) { navigate(`/login?returnTo=${encodeURIComponent(`/purchase/${id}`)}`); return; } if (!response.ok) throw new Error("Ortak sipariş bulunamadı veya erişim izniniz yok."); const data = await response.json(); if (!abort.signal.aborted) { setGroup(data); setError(""); } } catch (cause) { if (!abort.signal.aborted) setError((cause as Error).message); } }; void load(); const timer = setInterval(() => void load(), 10000); return () => { abort.abort(); clearInterval(timer); }; }, [id, revision, navigate]);
  const action = async (path: string, body?: object) => { if (busy) return; setBusy(true); setError(""); try { const response = await fetch(apiUrl(`/api/purchases/${id}/${path}`), { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" }, ...(body ? { body: JSON.stringify(body) } : {}) }); const data = await response.json().catch(() => null); if (!response.ok) throw new Error(data?.message ?? "Ödeme sonucu alınamadı. Sipariş durumunu kontrol edin."); setGroup(data); } catch (cause) { setError((cause as Error).message); } finally { setBusy(false); } };
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page"><h1>Ortak sipariş ve ödeme</h1>{error && <p role="alert">{error}</p>}{!group && !error && <p role="status">Sipariş yükleniyor...</p>}{group && <>
    <section className="booking-summary"><h2>{group.tickets.length} yolcu · {money.format(group.amount)}</h2><p>Sipariş: {group.id}</p><p>{group.provider === "LocalTest" ? "Yerel ödeme simülasyonu" : "iyzico sandbox"} · Test işlemi</p><p>{group.paymentStatus === "Paid" ? "Ödeme doğrulandı" : group.paymentStatus === "Failed" ? "Ödeme başarısız" : "Ödeme sonucu bekleniyor"}</p><p>İade talebi: {money.format(group.refundRequested)} · Doğrulanmış iade: {money.format(group.refunded)}</p>
      {group.discountAmount > 0 && <div className="campaign-price"><dl><div><dt>İndirim öncesi</dt><dd>{money.format(group.originalAmount)}</dd></div><div className="campaign-saving"><dt>{group.campaignName}{group.couponCode && ` · ${group.couponCode}`}</dt><dd>−{money.format(group.discountAmount)}</dd></div><div className="campaign-total"><dt>Sipariş tutarı</dt><dd>{money.format(group.amount)}</dd></div></dl><small>İndirim bilet tutarlarına dağıtıldı. İade, ilgili biletin ödenen tutarı üzerinden hesaplanır.</small></div>}
      {group.paymentStatus === "Pending" && <><p>Koltuk tutma sonu: {dateTime.format(new Date(group.holdExpiresAt))}. Sonradan gelen ödeme tüm biletleri düzenleyemezse tamamı iade sürecine girer.</p>
      <PendingLegalAcceptance
        key={group.id}
        endpoint={`/api/purchases/${group.id}/legal-acceptance`}
        accepted={group.legalAccepted === true}
        onSaved={() => setRevision(value => value + 1)}
      />
      {group.legalAccepted && group.provider === "IyzicoSandbox" && safeProviderCheckout(group.checkoutUrl) && <a className="site-button primary" href={safeProviderCheckout(group.checkoutUrl)!} referrerPolicy="no-referrer">iyzico sandbox ödeme sayfasına git</a>}
      {group.legalAccepted && group.provider === "LocalTest" && group.checkoutUrl && <div><button type="button" className="site-button primary" disabled={busy} onClick={() => void action("local-complete", { outcome: "Succeeded" })}>Ödemeyi başarılı yap</button><button type="button" className="site-button secondary" disabled={busy} onClick={() => void action("local-complete", { outcome: "Failed" })}>Ödemeyi başarısız yap</button></div>}
      <button type="button" className="site-button secondary" disabled={busy} onClick={() => void action("refresh")}>Sağlayıcıdan durumu sorgula</button></>}
      {group.lastError && <p role="status">{group.lastError}</p>}{group.needsAttention && <p>Siparişiniz destek takibinde. Yeni ödeme başlatmadan durumu kontrol edin.</p>}
    </section><p>Her yolcunun ayrı PNR'si ve iptal/iade tutarı vardır. Bir bileti iptal etmek diğer biletleri iptal etmez.</p>
    {group.tickets[0] && <JourneyStops journey={group.tickets[0]} />}
    <div className="reservation-list">{group.tickets.map(ticket => <section className="group-ticket-passenger" key={ticket.id} aria-label={`Yolcu · Koltuk ${ticket.seatNumber}`}><h2>{ticket.passengerName}</h2><p>{ticket.passengerEmail} · {ticket.passengerPhone}</p><p>Cinsiyet: {ticket.passengerGender === "Female" ? "Kadın" : ticket.passengerGender === "Male" ? "Erkek" : "Eski kayıtta belirtilmemiş"}</p><TicketCard item={ticket} showCheckout={false} /></section>)}</div></>}
    <button type="button" className="site-button secondary" onClick={() => setRevision(value => value + 1)}>Yenile</button> <Link to="/tickets" className="site-button secondary">Biletlerim</Link>
  </main></div>;
}
