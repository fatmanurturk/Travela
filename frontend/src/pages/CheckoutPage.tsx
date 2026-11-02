import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import TicketCard from "../components/TicketCard";
import type { Purchase } from "../payments";

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
        if (response.status === 401) { navigate(`/login?returnTo=${encodeURIComponent(`/test-payment/${id}`)}`); return; }
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
      if (!response.ok) throw new Error(data?.message ?? "Test işlemi tamamlanamadı. Biletlerim sayfasından durumu kontrol ediniz.");
      setItem(data as Purchase);
    } catch (e) { setError(e instanceof TypeError ? "Yanıt alınamadı. Yeniden denemeden önce işlem durumunu kontrol ediniz." : e instanceof Error ? e.message : "Test işlemi tamamlanamadı."); }
    finally { setBusy(false); }
  };
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page"><h1>Yerel test ödeme ekranı</h1>
    <p className="test-payment-badge">Bu ekran yalnızca geliştirme adaptörüdür. Kart bilgisi girmeyiniz. Gerçek ödeme veya iade yapılmaz.</p>
    {error && <p role="alert" className="results-message error">{error}</p>}
    {!item && !error ? <p role="status">Sipariş yükleniyor...</p> : item && <><TicketCard item={item} />
      {item.isTest && item.paymentStatus === "Pending" && item.checkoutUrl && <section className="booking-summary"><h2>Test sağlayıcı simülatörü</h2><label>Olası iadenin test sonucu <select value={refund} onChange={e => setRefund(e.target.value)}><option value="Succeeded">Başarılı</option><option value="Pending">Bekleyen</option><option value="Failed">Başarısız</option></select></label><div className="reservation-confirm-actions"><button className="site-button primary" disabled={busy} onClick={() => void simulate("Succeeded")}>Test ödemesini başarılı bildir</button><button className="site-button secondary" disabled={busy} onClick={() => void simulate("Failed")}>Test ödemesini başarısız bildir</button></div><p>Ekranı kapatırsanız bilet oluşmaz; ödeme tutması süre sonunda serbest kalır. Sonuç, sağlayıcı bildirimi ve sunucu sorgusuyla doğrulanır.</p></section>}
      {item.isTest && item.refundStatus !== "None" && item.refundStatus !== "Succeeded" && <section className="booking-summary"><h2>Yerel test iade sonucu</h2><div className="reservation-confirm-actions">{["Succeeded", "Pending", "Failed"].map(value => <button key={value} className="site-button secondary" disabled={busy} onClick={() => void simulate(value, true)}>{value === "Succeeded" ? "Test iadesini tamamla" : value === "Pending" ? "Test iadesini beklet" : "Test iadesini başarısız yap"}</button>)}</div></section>}
    </>}
    <div className="reservation-confirm-actions"><button className="site-button secondary" disabled={busy} onClick={() => { setError(""); setRevision(v => v + 1); }}>Durumu yenile</button><Link to="/tickets" className="site-button secondary">Biletlerim</Link></div>
  </main></div>;
}
