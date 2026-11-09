import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import TicketCard from "../components/TicketCard";
import JourneyStops from "../components/JourneyStops";
import type { Purchase } from "../payments";
import PendingLegalAcceptance from "../components/PendingLegalAcceptance";

export default function CheckoutPage() {
  const { id } = useParams();
  const [item, setItem] = useState<Purchase | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [refund, setRefund] = useState("Succeeded");
  const [revision, setRevision] = useState(0);
  const navigate = useNavigate();
  useEffect(() => {
    const abort = new AbortController();
    void (async () => {
      try {
        const response = await fetch(apiUrl(`/api/tickets/orders/${id}`), { headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal });
        if (response.status === 401) { navigate(`/login?returnTo=${encodeURIComponent(`/payment/${id}`)}`); return; }
        if (!response.ok) throw new Error("Ödeme siparişi bulunamadı veya erişim izniniz yok.");
        setItem(await response.json() as Purchase);
      } catch (e) { if (!abort.signal.aborted) setError(e instanceof TypeError ? "Sunucuya bağlanılamadı." : e instanceof Error ? e.message : "Sipariş yüklenemedi."); }
    })();
    return () => abort.abort();
  }, [id, revision, navigate]);
  const simulate = async (outcome: string, refundResult = false) => {
    setBusy(true); setError("");
    try {
      const response = await fetch(apiUrl(`/api/tickets/local/${id}/${refundResult ? "refund-result" : "complete"}`), { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" }, body: JSON.stringify({ outcome, refundOutcome: refund }) });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "İşlem tamamlanamadı. Biletlerim sayfasından durumu kontrol ediniz.");
      setItem(data as Purchase);
    } catch (e) { setError(e instanceof TypeError ? "Yanıt alınamadı. Yeniden denemeden önce işlem durumunu kontrol ediniz." : e instanceof Error ? e.message : "İşlem tamamlanamadı."); }
    finally { setBusy(false); }
  };
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page"><h1>Ödeme</h1>
    {error && <p role="alert" className="results-message error">{error}</p>}
    {!item && !error ? <p role="status">Sipariş yükleniyor...</p> : item && <><TicketCard item={item} showCheckout={false} />
      <section className="booking-summary" aria-label="Bilette kayıtlı yolcu"><h2>Yolcu bilgileri</h2><dl className="reservation-facts">
        <div><dt>Ad soyad</dt><dd>{item.passengerName}</dd></div>{item.passengerEmail && <div><dt>İletişim e-postası</dt><dd>{item.passengerEmail}</dd></div>}
        {item.passengerPhone && <div><dt>İletişim telefonu</dt><dd>{item.passengerPhone}</dd></div>}
      </dl><p>Bu siparişin yolcu bilgileri ödeme adımı açılırken kaydedilmiştir. Profil güncellemeleri bu bilgileri değiştirmez.</p></section>
      {!item.purchaseGroupId && item.paymentStatus === "Pending" && (
        <PendingLegalAcceptance
          key={item.id}
          endpoint={`/api/tickets/orders/${item.id}/legal-acceptance`}
          accepted={item.legalAccepted === true}
          onSaved={() => setRevision(value => value + 1)}
        />
      )}
      {item.legalAccepted && item.isTest && (!item.provider || item.provider === "LocalTest") && !item.purchaseGroupId && item.paymentStatus === "Pending" && item.checkoutUrl && <section className="booking-summary"><h2>Ödeme işlemleri</h2><label>İade sonucu <select value={refund} onChange={e => setRefund(e.target.value)}><option value="Succeeded">Başarılı</option><option value="Pending">Bekleyen</option><option value="Failed">Başarısız</option></select></label><div className="reservation-confirm-actions"><button className="site-button primary" disabled={busy} onClick={() => void simulate("Succeeded")}>Ödemeyi başarılı yap</button><button className="site-button secondary" disabled={busy} onClick={() => void simulate("Failed")}>Ödemeyi başarısız yap</button></div><p>Ödeme tamamlanmazsa bilet oluşmaz. Ayrılan koltuk süre sonunda boşalır.</p></section>}
      {item.isTest && (!item.provider || item.provider === "LocalTest") && item.refundStatus !== "None" && item.refundStatus !== "Succeeded" && <section className="booking-summary"><h2>İade işlemleri</h2><div className="reservation-confirm-actions">{["Succeeded", "Pending", "Failed"].map(value => <button key={value} className="site-button secondary" disabled={busy} onClick={() => void simulate(value, true)}>{value === "Succeeded" ? "İadeyi tamamla" : value === "Pending" ? "İadeyi beklet" : "İadeyi başarısız yap"}</button>)}</div></section>}
      <JourneyStops journey={item} />
    </>}
    <div className="reservation-confirm-actions"><button className="site-button secondary" disabled={busy} onClick={() => { setError(""); setRevision(v => v + 1); }}>Durumu yenile</button><Link to="/tickets" className="site-button secondary">Biletlerim</Link></div>
  </main></div>;
}
