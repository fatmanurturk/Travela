import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Details, TestNotice } from "../components/UiDetails";
import SiteHeader from "../components/SiteHeader";
import { apiUrl } from "../api";
import { dateTime, money } from "../reservations";
import { isIssuedTicket, paymentLabel, refundLabel, refundMessage, statusTone } from "../payments";
import type { Purchase } from "../payments";

export default function TicketDetailsPage() {
  const { pnr } = useParams();
  return <TicketDetails key={pnr} pnr={pnr ?? ""} />;
}

function TicketDetails({ pnr }: { pnr: string }) {
  const [ticket, setTicket] = useState<Purchase | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();
  const load = useCallback(async (signal?: AbortSignal): Promise<Purchase | null> => {
    const response = await fetch(apiUrl(`/api/tickets/${encodeURIComponent(pnr)}`), {
      headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, cache: "no-store", signal,
    });
    if (response.status === 401) {
      sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
      navigate(`/login?returnTo=${encodeURIComponent(`/tickets/${pnr}`)}`, { replace: true }); return null;
    }
    if (!response.ok) throw new Error(response.status === 404 ? "Bilet bulunamadı veya bu bilete erişim izniniz yok." : "Bilet bilgileri şu anda alınamıyor. Lütfen tekrar deneyiniz.");
    const current = await response.json() as Purchase;
    if (!isIssuedTicket(current)) throw new Error("Bu işlem için düzenlenmiş bilet belgesi bulunmuyor.");
    setTicket(current); setError(""); return current;
  }, [pnr, navigate]);
  useEffect(() => {
    const abort = new AbortController();
    const update = async () => {
      try { await load(abort.signal); }
      catch (cause) { if (!abort.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı." : cause instanceof Error ? cause.message : "Bilet alınamadı."); }
      finally { if (!abort.signal.aborted) setLoading(false); }
    };
    void update();
    const timer = window.setInterval(() => void update(), 15000);
    const onFocus = () => void update(); window.addEventListener("focus", onFocus);
    return () => { abort.abort(); window.clearInterval(timer); window.removeEventListener("focus", onFocus); };
  }, [load]);
  const act = async (action: "pdf" | "print" | "refresh") => {
    if (busy) return;
    setBusy(true); setError("");
    try {
      // Printing refreshes server state first; PDF generation separately reads current server state.
      const current = await load();
      if (!current) return;
      if (action === "pdf") {
        const response = await fetch(apiUrl(`/api/tickets/${encodeURIComponent(pnr)}/pdf`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, cache: "no-store" });
        if (!response.ok || !response.headers.get("Content-Type")?.startsWith("application/pdf")) throw new Error("PDF indirilemedi. Yetkinizi ve biletin güncel durumunu kontrol ediniz.");
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a"); link.href = url; link.download = `Travela-${current.pnr}.pdf`;
        document.body.appendChild(link); link.click(); link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 10000);
      } else if (action === "print") {
        await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
        window.print();
      }
    } catch (cause) { setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Belge alınamadı." : cause instanceof Error ? cause.message : "Belge işlemi tamamlanamadı."); }
    finally { setBusy(false); setLoading(false); }
  };
  const cancelled = ticket?.ticketStatus === "Cancelled";
  return <div className="travela-site ticket-details-view"><SiteHeader /><main className="trip-detail-page">
    {ticket?.isTest && <TestNotice />}
    <div className="ticket-document-actions"><Link to="/tickets" className="back-home">← Biletlerim</Link><button className="site-button secondary" disabled={busy} onClick={() => void act("refresh")}>Güncelle</button>{ticket && <><button className="site-button primary" disabled={busy} onClick={() => void act("pdf")}>{busy ? "İşleniyor..." : "PDF indir"}</button><button className="site-button secondary" disabled={busy} onClick={() => void act("print")}>Yazdır</button></>}</div>
    {error && <p className="results-message error ticket-document-error" role="alert">{error}</p>}
    {loading ? <p role="status">Bilet detayları yükleniyor...</p> : ticket && <article className="ticket-document" aria-labelledby="ticket-title">
      <div className="ticket-document-brand">Travela<span>.</span></div>
      <h1 id="ticket-title">Bilet detayı</h1>
      {ticket.isTest && <p className="test-payment-badge">TEST BİLETİ — Gerçek seyahat için geçerli değildir</p>}
      {cancelled && <p className="ticket-cancelled-banner">İPTAL EDİLDİ</p>}
      <h2>{ticket.departureCity} → {ticket.arrivalCity}</h2>
      <p className="ticket-pnr">PNR <strong>{ticket.pnr}</strong></p>
      <dl className="reservation-facts ticket-document-facts">
        <div><dt>Yolcu</dt><dd>{ticket.passengerName}</dd></div><div><dt>Firma</dt><dd>{ticket.companyName}</dd></div>
        <div><dt>Kalkış (Türkiye)</dt><dd>{dateTime.format(new Date(ticket.departureTime))}</dd></div><div><dt>Varış (Türkiye)</dt><dd>{dateTime.format(new Date(ticket.arrivalTime))}</dd></div>
        <div><dt>Koltuk</dt><dd>{ticket.seatNumber}</dd></div><div><dt>Bilet tutarı</dt><dd>{money.format(ticket.amount)}</dd></div>
        <div><dt>Bilet durumu</dt><dd className={`status-text ${statusTone(ticket.ticketStatus)}`}>{cancelled ? "İptal Edildi" : "Aktif"}</dd></div><div><dt>Ödeme durumu</dt><dd className={`status-text ${statusTone(ticket.paymentStatus)}`}>{paymentLabel(ticket.paymentStatus)}</dd></div>
        {ticket.refundStatus === "None" && <div><dt>İade durumu</dt><dd>{refundLabel(ticket.refundStatus)}</dd></div>}
        {!cancelled && <div><dt>İptal son zamanı (Türkiye)</dt><dd>{dateTime.format(new Date(ticket.cancelDeadline))}</dd></div>}
        {ticket.cancelledAt && <div><dt>İptal zamanı (Türkiye)</dt><dd>{dateTime.format(new Date(ticket.cancelledAt))}</dd></div>}
      </dl>
      {!cancelled && <p>{ticket.canCancel ? "Kalkışa en az 2 saat kala iptal ederek tam iade alabilirsiniz." : "Kalkışa 2 saatten az kaldığında iptal ve iade yapılamaz."}</p>}
      {ticket.cancellationReason && <section className="ticket-history"><h3>İptal nedeni</h3><p>{ticket.cancellationReason}</p></section>}
      {ticket.refundStatus !== "None" && <section className="ticket-refund-explanation" aria-live="polite"><h3>İade durumu</h3><p className={`status-text ${statusTone(ticket.refundStatus)}`}>{refundMessage(ticket.refundStatus)} · {money.format(ticket.refundAmount)}</p>{ticket.lastError && <Details title="İade açıklaması"><p>{ticket.lastError}</p></Details>}</section>}
      <p className="ticket-document-stamp">Güncelleme: {dateTime.format(new Date(ticket.retrievedAt))} · Türkiye saati</p>
      <p className="ticket-document-stamp">PDF, indirildiği andaki durumu gösterir.</p>
    </article>}
  </main></div>;
}
