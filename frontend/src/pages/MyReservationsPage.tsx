import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import { Details } from "../components/UiDetails";
import TicketCard from "../components/TicketCard";
import ReservationSummary from "../components/ReservationSummary";
import { dateTime, money } from "../reservations";
import type { Reservation } from "../reservations";
import type { Purchase } from "../payments";

export default function MyReservationsPage() {
  const [items, setItems] = useState<Purchase[]>([]);
  const [legacy, setLegacy] = useState<Reservation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [confirming, setConfirming] = useState<{ ticket?: Purchase; legacy?: Reservation } | null>(null);
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const [now, setNow] = useState(Date.now);
  const navigate = useNavigate();
  useEffect(() => {
    const tick = window.setInterval(() => setNow(Date.now()), 1000);
    const poll = window.setInterval(() => setRevision(v => v + 1), 10000);
    return () => { window.clearInterval(tick); window.clearInterval(poll); };
  }, []);
  useEffect(() => {
    const abort = new AbortController();
    void (async () => {
      try {
        const options = { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal };
        const responses = await Promise.all([fetch(apiUrl("/api/tickets/mine"), options), fetch(apiUrl("/api/reservations/mine"), options)]);
        if (responses.some(r => r.status === 401)) {
          sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
          navigate("/login?returnTo=%2Ftickets", { replace: true }); return;
        }
        if (responses.some(r => !r.ok)) throw new Error("Bilet ve geçmiş kayıtlarınız alınamadı. Lütfen tekrar deneyiniz.");
        const [tickets, archive] = await Promise.all(responses.map(r => r.json()));
        setItems(tickets as Purchase[]); setLegacy(archive as Reservation[]); setError("");
      } catch (e) { if (!abort.signal.aborted) setError(e instanceof TypeError ? "Sunucuya bağlanılamadı." : e instanceof Error ? e.message : "Biletler alınamadı."); }
      finally { if (!abort.signal.aborted) setLoading(false); }
    })();
    return () => abort.abort();
  }, [navigate, revision]);
  const cancel = async () => {
    if (!confirming || busy) return;
    setBusy(true); setError(""); setSuccess("");
    const path = confirming.ticket ? `/api/tickets/${confirming.ticket.pnr}/cancel` : `/api/reservations/${confirming.legacy!.reservationNumber}/cancel`;
    try {
      const response = await fetch(apiUrl(path), { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` } });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "İptal tamamlanamadı. Güncel durumu kontrol ediniz.");
      setSuccess(confirming.ticket ? "Bilet iptal edildi. İadeyi biletinizden takip edebilirsiniz." : "Ücretsiz kayıt serbest bırakıldı.");
      setConfirming(null); setRevision(v => v + 1);
    } catch (e) { setError(e instanceof TypeError ? "Yanıt alınamadı. İptali tekrar denemeden önce güncel durumu kontrol ediniz." : e instanceof Error ? e.message : "İptal tamamlanamadı."); }
    finally { setBusy(false); }
  };
  const target = confirming?.ticket ?? confirming?.legacy;
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page"><h1>Biletlerim</h1>
    {success && <p className="reservation-success" role="status">{success}</p>}
    {error && !confirming && <p className="results-message error" role="alert">{error}</p>}
    <button className="site-button secondary" disabled={busy} onClick={() => setRevision(v => v + 1)}>Durumları yenile</button>
    {loading ? <p role="status">Biletleriniz yükleniyor...</p> : <>
      {items.length === 0 ? <div className="site-empty-state"><p>Henüz biletiniz veya ödeme işleminiz yok.</p><Link to="/#sefer-ara" className="site-button primary">Sefer ara</Link></div> : <div className="reservation-list">{items.map(item => <TicketCard key={item.id} item={{ ...item, canCancel: item.canCancel && now <= new Date(item.cancelDeadline).getTime() }} busy={busy} onCancel={ticket => { setConfirming({ ticket }); setError(""); }} />)}</div>}
      {legacy.length > 0 && <Details title={`Eski ücretsiz kayıtlar (${legacy.length})`}><p>Bu kayıtlar satın alınmış bilet değildir.</p><div className="reservation-list">{legacy.map(item => <ReservationSummary key={item.reservationNumber} reservation={item} cancelling={busy} onCancel={item.canCancel && new Date(item.departureTime).getTime() > now ? r => { setConfirming({ legacy: r }); setError(""); } : undefined} />)}</div></Details>}
    </>}
    {target && confirming && <Confirm busy={busy} close={() => setConfirming(null)}>
      <h2 id="cancel-title">{confirming.ticket ? "Bilet iptal edilsin mi?" : "Eski ücretsiz kayıt serbest bırakılsın mı?"}</h2>
      <p>{target.departureCity} → {target.arrivalCity} · Koltuk {target.seatNumber}</p><p>Kalkış: {dateTime.format(new Date(target.departureTime))}</p>
      {confirming.ticket ? <><p>Tam iade tutarı: <strong>{money.format(confirming.ticket.amount)}</strong>{confirming.ticket.isTest && " (yerel test)"}</p><p>İptal son zamanı: {dateTime.format(new Date(confirming.ticket.cancelDeadline))}.</p></> : <p>Ücretsiz kayıt için ödeme veya iade işlemi bulunmuyor.</p>}
      {error && <p role="alert">{error}</p>}<div className="reservation-confirm-actions"><button autoFocus className="site-button secondary" disabled={busy} onClick={() => setConfirming(null)}>Vazgeç</button><button className="site-button reservation-cancel" disabled={busy} onClick={() => void cancel()}>{busy ? "İşleniyor..." : "Evet, iptal et"}</button></div>
    </Confirm>}
  </main></div>;
}

function Confirm({ children, busy, close }: { children: React.ReactNode; busy: boolean; close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const d = dialog.current; d?.showModal(); return () => d?.close(); }, []);
  return <dialog ref={dialog} className="reservation-confirm" aria-labelledby="cancel-title" onCancel={e => { e.preventDefault(); if (!busy) close(); }}>{children}</dialog>;
}
