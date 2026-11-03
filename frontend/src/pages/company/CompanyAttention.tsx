import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";
import { dateTime, reportHeaders } from "./reporting";
import "./CompanyAttention.css";

type Attention = { generatedAt: string; groups: { id: string; title: string; count: number; items: { id: string; title: string; description: string; href: string; date: string | null }[] }[] };
export default function CompanyAttention({ revision }: { revision: number }) {
 const [data, setData] = useState<Attention | null>(null);
 const [error, setError] = useState(""); const [loading, setLoading] = useState(true); const [retry, setRetry] = useState(0);
 const navigate = useNavigate();
 useEffect(() => {
  const controller = new AbortController(); setLoading(true); setError("");
  void (async () => {
   try {
    const response = await fetch(apiUrl("/api/company/attention"), { headers: reportHeaders(), signal: controller.signal });
    if (response.status === 401) { navigate("/login", { replace: true }); return; }
    if (!response.ok) throw new Error("İşlem gerektiren durumlar alınamadı.");
    const next = await response.json() as Attention;
    if (!controller.signal.aborted) setData(next);
   } catch (cause) { if (!controller.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı." : cause instanceof Error ? cause.message : "Durum bilgileri alınamadı."); }
   finally { if (!controller.signal.aborted) setLoading(false); }
  })();
  return () => controller.abort();
 }, [revision, retry, navigate]);
 return <section className="company-panel company-attention" aria-labelledby="company-attention-title">
  <h2 id="company-attention-title">İşlem gerektiren durumlar</h2>
  <p>Firmanın güncel kayıtlarıdır; aşağıdaki raporun tarih ve sefer filtrelerinden bağımsızdır.</p>
  {loading ? <p role="status">Güncel durumlar yükleniyor...</p> : error ? <div role="alert"><p>{error}</p><button type="button" className="secondary-button" onClick={() => setRetry(v => v + 1)}>Durumları tekrar yükle</button></div> : data && <>
   {data.groups.every(group => group.count === 0) ? <p>Şu anda işlem gerektiren bir durum bulunmuyor.</p> : <div className="attention-grid">{data.groups.filter(group => group.count > 0).map(group => <section key={group.id} className={`attention-group attention-${group.id}`}>
    <h3>{group.title} <span className="attention-count">{group.count}</span></h3>
    <ul>{group.items.map(item => <li key={item.id}><Link to={item.href}>{item.title}</Link><p>{item.description}</p>{item.date && <time dateTime={item.date}>{dateTime.format(new Date(item.date))} (Türkiye)</time>}</li>)}</ul>
    {group.count > group.items.length && <p>Öncelikli {group.items.length} / {group.count} kayıt gösteriliyor. <Link to={group.id === "refunds" ? "/company/reservations" : "/company/trips"}>İlgili listeyi aç</Link></p>}
   </section>)}</div>}
   <small>Son kontrol: {dateTime.format(new Date(data.generatedAt))} (Türkiye)</small>
  </>}
 </section>;
}
