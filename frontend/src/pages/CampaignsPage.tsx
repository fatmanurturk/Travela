import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import SiteHeader from "../components/SiteHeader";
import { apiUrl } from "../api";
import { dateTime, money } from "../reservations";
import { discountLabel } from "../campaigns";
import type { Campaign } from "../campaigns";
import "../campaigns.css";

export default function CampaignsPage() {
  const [searchParams] = useSearchParams(); const rawTripId = searchParams.get("tripId");
  const tripId = rawTripId && /^[1-9][0-9]*$/.test(rawTripId) ? rawTripId : null;
  const [items, setItems] = useState<Campaign[]>([]); const [error, setError] = useState(""); const [loadedKey, setLoadedKey] = useState<string | null>(null);
  const [revision, setRevision] = useState(0); const [message, setMessage] = useState(""); const [filter, setFilter] = useState("");
  const requestKey = `${tripId ?? "all"}:${revision}`; const loading = loadedKey !== requestKey;
  useEffect(() => { const abort = new AbortController();
    void fetch(apiUrl(`/api/campaigns/public${tripId ? `?tripId=${tripId}` : ""}`), { signal: abort.signal }).then(async r => { if (!r.ok) throw new Error("Kampanyalar yüklenemedi."); return r.json(); })
      .then(data => { if (!abort.signal.aborted) { setItems(data); setError(""); } }).catch(e => { if (!abort.signal.aborted) setError(e.message); })
      .finally(() => { if (!abort.signal.aborted) setLoadedKey(requestKey); }); return () => abort.abort();
  }, [revision, tripId, requestKey]);
  const companies = [...new Map(items.map(c => [c.companyId, c.companyName])).entries()];
  const visible = items.filter(c => !filter || String(c.companyId) === filter);
  return <div className="travela-site"><SiteHeader /><main className="trip-detail-page campaign-public">
    <section className="campaign-hero"><span className="eyebrow">DAHA AVANTAJLI YOLCULUKLAR</span><h1>Yolun açık, fırsatın bol.</h1><p>Firmanı seç, kampanya koşullarını incele ve bir sonraki yolculuğunda indirim kazan.</p><Link to="/#sefer-ara" className="site-button primary">Sefer ara</Link></section>
    <div className="campaign-toolbar"><div><h2>{tripId ? `Sefer #${tripId} için kampanyalar` : "Güncel kampanyalar"}</h2>{tripId && <><Link to={`/trips/${tripId}`}>← Seferime dön</Link> · <Link to="/campaigns">Tüm kampanyaları gör</Link></>}</div><label>Firma<select value={filter} onChange={e => setFilter(e.target.value)}><option value="">Tüm firmalar</option>{companies.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label></div>
    {message && <p role="status">{message}</p>}{loading && <p role="status">Kampanyalar yükleniyor...</p>}{error && <p role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></p>}
    {!loading && !error && !visible.length && <div className="site-empty-state"><h3>Yeni fırsatlar yolda</h3><p>Şu anda bu seçim için yayınlanan kampanya bulunmuyor.</p><Link to="/#sefer-ara">Seferleri incele</Link></div>}
    <div className="campaign-grid">{!loading && !error && visible.map(c => <article className="campaign-card" key={c.id}>
      <div className="campaign-card-top"><span>{c.companyName}</span><span className="campaign-pill">{c.code ? "Kupon" : "Otomatik"}</span></div>
      <strong className="campaign-discount">{discountLabel(c)}</strong><h2>{c.name}</h2><p>{c.description}</p>
      <dl><div><dt>Güzergâh</dt><dd>{c.routeName ?? "Firmanın tüm güzergâhları"}</dd></div><div><dt>Minimum sepet</dt><dd>{money.format(c.minimumSubtotal)}</dd></div>
        {c.maximumDiscount != null && <div><dt>İndirim tavanı</dt><dd>{money.format(c.maximumDiscount)}</dd></div>}
        <div><dt>Son kullanım (Türkiye)</dt><dd>{dateTime.format(new Date(c.endsAt))}</dd></div><div><dt>Hesap başına</dt><dd>{c.perUserLimit} sipariş</dd></div>
        {c.departureFrom && <div><dt>İlk kalkış</dt><dd>{dateTime.format(new Date(c.departureFrom))}</dd></div>}
        {c.departureUntil && <div><dt>Son kalkış sınırı</dt><dd>{dateTime.format(new Date(c.departureUntil))} öncesi</dd></div>}</dl>
      {c.code ? <button type="button" className="campaign-code" onClick={async () => { try { await navigator.clipboard.writeText(c.code!); setMessage(`${c.code} kopyalandı. Sefer detayındaki kupon alanında kullanabilirsiniz.`); } catch { setMessage(`Kupon kodunuz: ${c.code}`); } }}><span>{c.code}</span><small>Kodu kopyala</small></button> : <p className="campaign-auto">Uygun sepete otomatik uygulanır</p>}
      <Link to={tripId ? `/trips/${tripId}` : "/#sefer-ara"} className="site-button secondary">{tripId ? "Bu seferde kullan →" : "Yolculuğunu planla →"}</Link>
    </article>)}</div>
    <p className="campaign-muted">Kampanyalar kontenjan ve hesap limitlerine tabidir. Bir siparişte tek indirim uygulanır; sabit TL indirimi sepetin tamamı içindir. İptal edilen düzenlenmiş biletlerde kullanım hakkı geri yüklenmez. Kesin tutar ödeme öncesinde gösterilir.</p>
  </main></div>;
}
