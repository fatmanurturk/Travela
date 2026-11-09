import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { apiUrl } from "../api";
import { money } from "../reservations";
import type { CampaignPrice } from "../campaigns";
import "../campaigns.css";

export default function CouponCheckout({ tripId, count, code, disabled, onCode, onQuote }: {
  tripId: number; count: number; code: string; disabled: boolean;
  onCode: (code: string) => void; onQuote: (price: CampaignPrice | null) => void;
}) {
  const [input, setInput] = useState(""); const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; price: CampaignPrice | null; error: string } | null>(null);
  const requestKey = `${tripId}:${count}:${code}:${revision}`;
  const current = result?.key === requestKey ? result : null;
  const price = count ? current?.price : null; const error = count ? current?.error : "";
  const loading = count > 0 && current == null;
  useEffect(() => {
    const abort = new AbortController(); onQuote(null);
    if (!count) return;
    void fetch(apiUrl("/api/campaigns/quote"), { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, signal: abort.signal,
      body: JSON.stringify({ tripId, passengerCount: count, couponCode: code || null }) }).then(async response => {
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message ?? "İndirim ve güncel fiyat alınamadı.");
      if (!abort.signal.aborted) { setResult({ key: requestKey, price: data, error: "" }); onQuote(data); }
    }).catch(cause => { if (!abort.signal.aborted) setResult({ key: requestKey, price: null, error: cause instanceof TypeError ? "Fiyat için sunucuya bağlanılamadı." : cause.message }); });
    return () => abort.abort();
  }, [tripId, count, code, onQuote, revision, requestKey]);
  return <fieldset className="coupon-checkout" disabled={disabled || !count}>
    <legend>Kupon ve kampanya</legend>
    <label htmlFor={`coupon-${tripId}`}>Kupon kodunuz var mı?</label>
    <div className="coupon-input-row"><input id={`coupon-${tripId}`} maxLength={32} autoComplete="off" placeholder="Örn. YOLCULUK20" value={input} onChange={e => setInput(e.target.value.toUpperCase())} />
      <button type="button" className="site-button secondary" disabled={loading || !input.trim()} onClick={() => { onQuote(null); onCode(input.trim().toUpperCase()); setRevision(v => v + 1); }}>Uygula</button>
      {code && <button type="button" className="site-button secondary" onClick={() => { onQuote(null); onCode(""); setInput(""); }}>Kaldır</button>}</div>
    <p className="campaign-muted">Kod girmezseniz uygun otomatik kampanyalardan en avantajlısı seçilir. İndirimler birleştirilmez. <Link to={`/campaigns?tripId=${tripId}`} target="_blank" rel="noopener noreferrer">Bu seferin kampanyalarını gör</Link></p>
    {loading && <p role="status">Güncel fiyat hesaplanıyor...</p>}
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></p>}
    {price && <div className="campaign-price" aria-live="polite"><dl><div><dt>Biletler toplamı</dt><dd>{money.format(price.originalAmount)}</dd></div>
      {price.discountAmount > 0 && <div className="campaign-saving"><dt>{price.campaignName}{price.couponCode && ` · ${price.couponCode}`}</dt><dd>−{money.format(price.discountAmount)}</dd></div>}
      <div className="campaign-total"><dt>Ödenecek toplam</dt><dd>{money.format(price.amount)}</dd></div></dl>
      <small>{price.discountAmount > 0 ? "İndirim sipariş oluşturulurken kesinleşir ve her bilete paylaştırılır. İadeler ödenen bilet tutarı üzerinden yapılır." : "Bu sepet için uygulanabilir otomatik indirim bulunmuyor."}</small></div>}
  </fieldset>;
}
