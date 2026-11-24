import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { apiUrl } from "../api";
import CampaignPriceSummary from "./CampaignPriceSummary";
import type { CampaignPrice } from "../campaigns";
import "../campaigns.css";

export default function CouponCheckout({ tripId, count, code, disabled, onCode, onQuote, boardingStopOrder, alightingStopOrder, showPrice = true }: {
  tripId: number; count: number; code: string; disabled: boolean; showPrice?: boolean;
  boardingStopOrder?: number | null; alightingStopOrder?: number | null;
  onCode: (code: string) => void; onQuote: (price: CampaignPrice | null) => void;
}) {
  const [input, setInput] = useState(""); const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; price: CampaignPrice | null; error: string } | null>(null);
  const token = sessionStorage.getItem("travela_token");
  const requestKey = `${token ? "member" : "guest"}:${tripId}:${boardingStopOrder}:${alightingStopOrder}:${count}:${code}:${revision}`;
  const current = result?.key === requestKey ? result : null;
  const price = count ? current?.price : null; const error = count ? current?.error : "";
  const loading = count > 0 && current == null;
  useEffect(() => {
    const abort = new AbortController(); onQuote(null);
    if (!count) return;
    void fetch(apiUrl(token ? "/api/campaigns/quote" : "/api/guest/purchases/quote"), { method: "POST", headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) }, signal: abort.signal,
      body: JSON.stringify({ tripId, boardingStopOrder, alightingStopOrder, passengerCount: count, couponCode: code || null }) }).then(async response => {
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "İndirim ve güncel fiyat alınamadı.");
      if (!abort.signal.aborted) { setResult({ key: requestKey, price: data, error: "" }); onQuote(data); }
    }).catch(cause => { if (!abort.signal.aborted) setResult({ key: requestKey, price: null, error: cause instanceof TypeError ? "Fiyat için sunucuya bağlanılamadı." : cause.message }); });
    return () => abort.abort();
  }, [tripId, boardingStopOrder, alightingStopOrder, count, code, onQuote, revision, requestKey, token]);
  return <fieldset className="coupon-checkout" disabled={disabled || !count}>
    <legend>Kupon ve kampanya</legend>
    <label htmlFor={`coupon-${tripId}`}>Kupon kodunuz var mı?</label>
    <div className="coupon-input-row"><input id={`coupon-${tripId}`} maxLength={32} autoComplete="off" placeholder="Örn. YOLCULUK20" value={input} onChange={e => setInput(e.target.value.toUpperCase())} />
      <button type="button" className="site-button secondary" disabled={loading || !input.trim()} onClick={() => { onQuote(null); onCode(input.trim().toUpperCase()); setRevision(v => v + 1); }}>Uygula</button>
      {code && <button type="button" className="site-button secondary" onClick={() => { onQuote(null); onCode(""); setInput(""); }}>Kaldır</button>}</div>
    <p className="campaign-muted">Kod girmezseniz uygun otomatik kampanyalardan en avantajlısı seçilir. İndirimler birleştirilmez. <Link to={`/campaigns?tripId=${tripId}`} target="_blank" rel="noopener noreferrer">Bu seferin kampanyalarını gör</Link></p>
    {loading && <p role="status">Güncel fiyat hesaplanıyor...</p>}
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></p>}
    {price && showPrice && <CampaignPriceSummary price={price} />}
  </fieldset>;
}
