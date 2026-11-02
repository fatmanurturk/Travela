import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import ReservationSummary from "../components/ReservationSummary";
import type { Reservation } from "../reservations";

export default function MyReservationsPage() {
  const [items, setItems] = useState<Reservation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);
  const [confirming, setConfirming] = useState<Reservation | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [actionError, setActionError] = useState("");
  const [success, setSuccess] = useState("");
  const [now, setNow] = useState(Date.now);
  const navigate = useNavigate();
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const response = await fetch(apiUrl("/api/reservations/mine"), {
          headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: controller.signal,
        });
        if (response.status === 401) {
          sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
          navigate("/login?returnTo=%2Freservations", { replace: true }); return;
        }
        if (!response.ok) throw new Error(response.status === 403 ? "Rezervasyonlar yalnızca yolcu hesabıyla görüntülenebilir." : "Rezervasyonlarınız alınamadı. Lütfen tekrar deneyiniz.");
        setItems(await response.json() as Reservation[]);
      } catch (cause) {
        if (!controller.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Lütfen tekrar deneyiniz." : cause instanceof Error ? cause.message : "Rezervasyonlarınız alınamadı.");
      } finally { if (!controller.signal.aborted) setLoading(false); }
    })();
    return () => controller.abort();
  }, [navigate, attempt]);
  const cancelReservation = async () => {
    if (!confirming || cancelling) return;
    setCancelling(true); setActionError(""); setSuccess("");
    try {
      const response = await fetch(apiUrl(`/api/reservations/${encodeURIComponent(confirming.reservationNumber)}/cancel`), {
        method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` },
      });
      if (response.status === 401) {
        sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
        navigate("/login?returnTo=%2Freservations", { replace: true }); return;
      }
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "Rezervasyon iptal edilemedi. Listeyi yenileyip tekrar deneyiniz.");
      setItems(current => current.map(item => item.reservationNumber === data.reservationNumber ? data as Reservation : item));
      setConfirming(null); setSuccess("Rezervasyonunuz iptal edildi. Güncel bilgileri listede görebilirsiniz.");
    } catch (cause) {
      setActionError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Güncel durumu görmek için listeyi yenileyiniz." : cause instanceof Error ? cause.message : "İptal tamamlanamadı.");
    } finally { setCancelling(false); }
  };
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page">
    <h1>Rezervasyonlarım</h1>
    {success && <p className="reservation-success" role="status">{success}</p>}
    {actionError && !confirming && <p className="results-message error" role="alert">{actionError}</p>}
    <button type="button" className="site-button secondary" disabled={loading || cancelling} onClick={() => { setLoading(true); setError(""); setAttempt(v => v + 1); }}>Listeyi yenile</button>
    {loading ? <p className="site-empty-state" role="status">Rezervasyonlarınız yükleniyor...</p> : error ?
      <div className="results-message error" role="alert"><p>{error}</p><button type="button" className="site-button secondary" onClick={() => { setLoading(true); setError(""); setAttempt(v => v + 1); }}>Tekrar dene</button></div> :
      items.length === 0 ? <div className="site-empty-state"><p>Henüz rezervasyonunuz bulunmuyor.</p><Link to="/#sefer-ara" className="site-button primary">Sefer ara</Link></div> :
      <div className="reservation-list">{items.map(item => <ReservationSummary key={item.reservationNumber} reservation={item} cancelling={cancelling} onCancel={new Date(item.departureTime).getTime() > now ? item => { setConfirming(item); setActionError(""); setSuccess(""); } : undefined} />)}</div>}
    {confirming && <CancelConfirmation busy={cancelling} onClose={() => setConfirming(null)}>
      <h2 id="cancel-title">Rezervasyon iptal edilsin mi?</h2>
      <p id="cancel-description">{confirming.departureCity} → {confirming.arrivalCity}, koltuk {confirming.seatNumber}. İptal sonrası koltuk başka bir yolcu tarafından rezerve edilebilir.</p>
      {actionError && <p role="alert" className="profile-error">{actionError}</p>}
      <div className="reservation-confirm-actions"><button autoFocus type="button" className="site-button secondary" disabled={cancelling} onClick={() => setConfirming(null)}>Vazgeç</button><button type="button" className="site-button reservation-cancel" disabled={cancelling} onClick={() => void cancelReservation()}>{cancelling ? "İptal ediliyor..." : "Evet, iptal et"}</button></div>
    </CancelConfirmation>}
  </main></div>;
}

function CancelConfirmation({ busy, onClose, children }: { busy: boolean; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = dialog.current;
    element?.showModal();
    return () => element?.close();
  }, []);
  return <dialog ref={dialog} className="reservation-confirm" aria-labelledby="cancel-title" aria-describedby="cancel-description"
    onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}>{children}</dialog>;
}
