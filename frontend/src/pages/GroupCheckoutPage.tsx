import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import { safeProviderCheckout } from "../payments";
import type { Purchase } from "../payments";
import { downloadPrivatePdf } from "../privatePdf";
import "../guestTickets.css";
import SiteHeader from "../components/SiteHeader";
import TicketCard from "../components/TicketCard";
import JourneyStops from "../components/JourneyStops";
import { money, dateTime } from "../reservations";
import "../campaigns.css";
import PendingLegalAcceptance from "../components/PendingLegalAcceptance";
import HoldCountdown from "../components/HoldCountdown";
type Group = {
  guest?: boolean;
  serverTime?: string;
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
  useEffect(() => { window.scrollTo(0, 0); }, [id]);
  const [endpoint, setEndpoint] = useState(sessionStorage.getItem("travela_token") ? "/api/purchases" : "/api/guest/purchases");
  const [paymentExpired, setPaymentExpired] = useState(false);
  useEffect(() => { const abort = new AbortController(); const load = async () => { try { const response = await fetch(apiUrl(`${endpoint}/${id}`), { credentials: "include", headers: endpoint === "/api/purchases" && sessionStorage.getItem("travela_token") ? { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` } : {}, signal: abort.signal, cache: "no-store" });
      if ((response.status === 404 || response.status === 401) && endpoint === "/api/purchases") { setEndpoint("/api/guest/purchases"); return; } if (!response.ok) throw new Error("Ortak sipariş bulunamadı veya erişim izniniz yok."); const data = await response.json(); if (!Array.isArray(data?.tickets)) throw new Error("Sipariş verisi alınamadı. Tekrar deneyin."); if (!abort.signal.aborted) { setGroup(data); setError(""); } } catch (cause) { if (!abort.signal.aborted) setError((cause as Error).message); } }; void load(); const timer = setInterval(() => void load(), 10000); return () => { abort.abort(); clearInterval(timer); }; }, [id, revision, endpoint, navigate]);
  const action = async (path: string, body?: object) => { if (busy) return; setBusy(true); setError(""); try { const response = await fetch(apiUrl(`${endpoint}/${id}/${path}`), { method: "POST", credentials: "include", headers: { ...(endpoint === "/api/purchases" && sessionStorage.getItem("travela_token") ? { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` } : {}), "Content-Type": "application/json" }, ...(body ? { body: JSON.stringify(body) } : {}) }); const data = await response.json().catch(() => null); if (!response.ok) throw new Error(data?.message ?? "Ödeme sonucu alınamadı. Sipariş durumunu kontrol edin."); setGroup(data); } catch (cause) { setError((cause as Error).message); } finally { setBusy(false); } };
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page"><h1>Ortak sipariş ve ödeme</h1>{error && <p role="alert">{error}</p>}{!group && !error && <p role="status">Sipariş yükleniyor...</p>}{group && <>
    <section className="booking-summary"><h2>{group.tickets.length} yolcu · {money.format(group.amount)}</h2><p>Sipariş: {group.id}</p><p>{group.provider === "LocalTest" ? "Yerel ödeme simülasyonu" : group.isTest ? "iyzico sandbox" : "iyzico"}{group.isTest ? " · Test işlemi" : " · Güvenli ödeme"}</p><p>{group.paymentStatus === "Paid" ? "Ödeme doğrulandı" : group.paymentStatus === "Failed" ? "Ödeme başarısız" : "Ödeme sonucu bekleniyor"}</p><p>İade talebi: {money.format(group.refundRequested)} · Doğrulanmış iade: {money.format(group.refunded)}</p>
      {group.discountAmount > 0 && <div className="campaign-price"><dl><div><dt>İndirim öncesi</dt><dd>{money.format(group.originalAmount)}</dd></div><div className="campaign-saving"><dt>{group.campaignName}{group.couponCode && ` · ${group.couponCode}`}</dt><dd>−{money.format(group.discountAmount)}</dd></div><div className="campaign-total"><dt>Sipariş tutarı</dt><dd>{money.format(group.amount)}</dd></div></dl><small>İndirim bilet tutarlarına dağıtıldı. İade, ilgili biletin ödenen tutarı üzerinden hesaplanır.</small></div>}
      {group.paymentStatus === "Pending" && <><p>Koltuk tutma sonu: {dateTime.format(new Date(group.holdExpiresAt))}. Sonradan gelen ödeme tüm biletleri düzenleyemezse tamamı iade sürecine girer.</p>
      {group.serverTime && <HoldCountdown serverTime={group.serverTime} expiresAt={group.holdExpiresAt} payment onExpired={() => { setPaymentExpired(true); void action("refresh"); }} />}
      {paymentExpired && <p role="status">Ödeme tutma süresi doldu. Sağlayıcı sonucu kesinleşmeden yeni ödeme başlatmayın; mevcut siparişin durumunu kontrol edin.</p>}
      {!group.guest && <PendingLegalAcceptance
        key={group.id}
        endpoint={`/api/purchases/${group.id}/legal-acceptance`}
        accepted={group.legalAccepted === true}
        onSaved={() => setRevision(value => value + 1)}
      />}
      {!paymentExpired && group.legalAccepted && ["IyzicoSandbox", "Iyzico"].includes(group.provider) && safeProviderCheckout(group.checkoutUrl, group.provider) && <a className="site-button primary" href={safeProviderCheckout(group.checkoutUrl, group.provider)!} referrerPolicy="no-referrer">iyzico{group.isTest ? " sandbox" : ""} ödeme sayfasına git</a>}
      {!paymentExpired && group.legalAccepted && group.provider === "LocalTest" && group.checkoutUrl && <div><button type="button" className="site-button primary" disabled={busy} onClick={() => void action("local-complete", { outcome: "Succeeded" })}>Ödemeyi başarılı yap</button><button type="button" className="site-button secondary" disabled={busy} onClick={() => void action("local-complete", { outcome: "Failed" })}>Ödemeyi başarısız yap</button></div>}
      <button type="button" className="site-button secondary" disabled={busy} onClick={() => void action("refresh")}>Sağlayıcıdan durumu sorgula</button></>}
      {group.lastError && <p role="status">{group.lastError}</p>}{group.needsAttention && <p>Siparişiniz destek takibinde. Yeni ödeme başlatmadan durumu kontrol edin.</p>}
    </section><p>Her yolcunun ayrı PNR'si ve iptal/iade tutarı vardır. Bir bileti iptal etmek diğer biletleri iptal etmez.</p>
    {group.tickets[0] && <JourneyStops journey={group.tickets[0]} />}
    <div className="reservation-list">{group.tickets.map(ticket => <section className="group-ticket-passenger" key={ticket.id} aria-label={`Yolcu · Koltuk ${ticket.seatNumber}`}><h2>{ticket.passengerName}</h2><p>{ticket.passengerEmail} · {ticket.passengerPhone}</p><p>Cinsiyet: {ticket.passengerGender === "Female" ? "Kadın" : ticket.passengerGender === "Male" ? "Erkek" : "Eski kayıtta belirtilmemiş"}</p><TicketCard item={ticket} showCheckout={false} publicAccess={group.guest} />
      {ticket.pnr && ticket.kind === "Ticket" && ticket.paymentStatus === "Paid" && <div className="guest-ticket-actions"><button type="button" className="site-button primary" disabled={busy} onClick={() => {
        setBusy(true); setError("");
        void downloadPrivatePdf(group.guest ? `/api/guest/purchases/${group.id}/tickets/${ticket.pnr}/pdf` : `/api/tickets/${ticket.pnr}/pdf`, group.guest ? null : sessionStorage.getItem("travela_token")).catch(cause => setError(cause.message)).finally(() => setBusy(false));
      }}>PDF indir</button><Link className="site-button secondary" to="/bilet-sorgula">Biletimi Sorgula</Link></div>}</section>)}</div></>}
    <button type="button" className="site-button secondary" onClick={() => setRevision(value => value + 1)}>Yenile</button> {sessionStorage.getItem("travela_token") && <Link to="/tickets" className="site-button secondary">Biletlerim</Link>} <Link to="/bilet-sorgula" className="site-button secondary">Biletimi Sorgula</Link>
  </main></div>;
}
