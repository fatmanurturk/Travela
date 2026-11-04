import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { apiUrl } from "../../api";
import CompanyOverviewView from "./CompanyOverviewView";
import { Details } from "../../components/UiDetails";
import { count, dateLabel, dateTime, money, orderLink, reportHeaders, tripStatus } from "./reporting";
import type { Report, ReportDay } from "./reporting";

const defaults: Record<string, string> = { period: "month", basis: "transaction", from: "", to: "", tripId: "", routeId: "", sort: "departure", direction: "asc", routeSort: "sold", page: "1", pageSize: "20" };
function Chart({ days, cash, label }: { days: ReportDay[]; cash?: boolean; label: string }) {
 const keys = cash ? ["collected", "refunded"] as const : ["sold", "cancelled"] as const;
 const names = cash ? ["Doğrulanmış tahsilat", "Tamamlanan iade"] : ["Düzenlenmiş bilet", "İptal"];
 const max = Math.max(1, ...days.flatMap(d => keys.map(k => d[k])));
 const points = (key: typeof keys[number]) => days.map((d, i) => `${45 + i * 630 / Math.max(1, days.length - 1)},${175 - 145 * d[key] / max}`).join(" ");
 return <section className="company-panel report-chart"><h2>{cash ? "Tahsilat ve iade" : "Günlük bilet satışları"}</h2>
  <p><span className="report-series-one">● {names[0]}</span> · <span className="report-series-two">● {names[1]}</span> ({cash ? "TRY" : "adet"})</p>
  <svg viewBox="0 0 720 210" role="img" aria-label={`${label}: ${days.length} günlük ${cash ? "tahsilat ve iade tutarı" : "satış ve iptal adedi"}; ayrıntılar aşağıdaki tabloda.`}>
   <text x="2" y="20" fontSize="12">{count.format(max)}</text><text x="22" y="180" fontSize="12">0</text><path d="M45 25V175H675" fill="none" stroke="#b8c2cc" />
   {keys.map((key, i) => <g key={key}><polyline points={points(key)} fill="none" stroke={i ? "#b45309" : "#176f89"} strokeWidth="3" />{days.length === 1 && <circle cx="45" cy={175 - 145 * days[0][key] / max} r="4" fill={i ? "#b45309" : "#176f89"} />}</g>)}
   <text x="45" y="200" fontSize="12">{dateLabel(days[0]?.date ?? "")}</text><text x="675" y="200" textAnchor="end" fontSize="12">{dateLabel(days.at(-1)?.date ?? "")}</text>
  </svg>
  <p>{names[0]} toplamı: {cash ? money.format(days.reduce((a, d) => a + d.collected, 0)) : count.format(days.reduce((a, d) => a + d.sold, 0))}; {names[1]} toplamı: {cash ? money.format(days.reduce((a, d) => a + d.refunded, 0)) : count.format(days.reduce((a, d) => a + d.cancelled, 0))}.</p>
  <details><summary>Grafik verileri</summary><div className="table-wrap"><table className="data-table"><caption>{label} · {cash ? "TRY tutarları" : "Bilet adetleri"}</caption><thead><tr><th>Tarih</th>{names.map(n => <th key={n}>{n}</th>)}</tr></thead><tbody>{days.map(d => <tr key={d.date}><td>{dateLabel(d.date)}</td>{keys.map(k => <td key={k}>{cash ? money.format(d[k]) : count.format(d[k])}</td>)}</tr>)}</tbody></table></div></details>
 </section>;
}

export default function CompanyReportingPage({ overview = false }: { overview?: boolean }) {
 const [params, setParams] = useSearchParams();
 const read = () => Object.fromEntries(Object.entries(defaults).map(([k, v]) => [k, params.get(k) ?? v]));
 const [draft, setDraft] = useState(read); const [report, setReport] = useState<Report | null>(null);
 const [loading, setLoading] = useState(true); const [error, setError] = useState(""); const [formError, setFormError] = useState("");
 const [exporting, setExporting] = useState(false); const [revision, setRevision] = useState(0);
 const [routes, setRoutes] = useState<{ id: number; departureCity: string; arrivalCity: string }[]>([]);
 const navigate = useNavigate(); const requestParams = new URLSearchParams(params); requestParams.delete("environment"); const query = requestParams.toString();
 useEffect(() => { setDraft(read()); }, [query]); // eslint-disable-line react-hooks/exhaustive-deps
 useEffect(() => {
  const c = new AbortController();
  fetch(apiUrl("/api/company/reports/options"), { headers: reportHeaders(), signal: c.signal }).then(r => r.ok ? r.json() : []).then(data => { if (!c.signal.aborted) setRoutes(data); }).catch(() => {});
  return () => c.abort();
 }, []);
 useEffect(() => {
  const c = new AbortController(); setLoading(true); setError(""); setReport(null);
  void (async () => {
   try {
    const response = await fetch(apiUrl(`/api/company/reports?${query}`), { headers: reportHeaders(), signal: c.signal });
    if (response.status === 401) { navigate("/login", { replace: true }); return; }
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(response.status === 403 ? "Raporlar için aktif bir firma hesabı gereklidir." : data?.message ?? "Rapor verileri alınamadı.");
    if (!c.signal.aborted) setReport(data);
   } catch (cause) { if (!c.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Tekrar deneyiniz." : cause instanceof Error ? cause.message : "Rapor alınamadı."); }
   finally { if (!c.signal.aborted) setLoading(false); }
  })(); return () => c.abort();
 }, [query, revision, navigate]);
 const change = (key: string, value: string) => setDraft(d => ({ ...d, [key]: value }));
 const apply = () => {
  if (draft.period === "custom") {
   const a = Date.parse(draft.from), b = Date.parse(draft.to);
   if (!Number.isFinite(a) || !Number.isFinite(b) || b < a || (b - a) / 86400000 >= 366 || draft.from < "2000-01-01" || draft.to > "2100-12-31") { setFormError("Geçerli bir başlangıç ve bitiş seçiniz. Aralık en fazla 366 gün olmalı ve bitiş başlangıçtan önce olmamalıdır."); return; }
  }
  setFormError(""); setParams(Object.fromEntries(Object.entries({ ...draft, page: "1" }).filter(([, v]) => v !== ""))); setRevision(r => r + 1);
 };
 const page = Number(params.get("page") ?? 1), pageSize = Number(params.get("pageSize") ?? 20);
 const setPage = (v: number) => { const next = new URLSearchParams(params); next.set("page", String(v)); setParams(next); };
 async function download() {
  setExporting(true); setFormError("");
  try {
   const r = await fetch(apiUrl(`/api/company/reports/csv?${query}`), { headers: reportHeaders() });
   if (!r.ok) { const data = await r.json().catch(() => null); throw new Error(data?.message ?? `CSV indirilemedi (${r.status}).`); }
   const url = URL.createObjectURL(await r.blob()), a = document.createElement("a"); a.href = url;
   a.download = `Travela-sefer-raporu-${report?.scope.from}-${report?.scope.to}.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (cause) { setFormError(cause instanceof Error ? cause.message : "CSV indirilemedi."); } finally { setExporting(false); }
 }
 const label = "Firma işlemleri";
 const cards = report ? [
  ["Satılan bilet", count.format(report.metrics.sold), "Doğrulanmış ödeme sonrası düzenlenen; iptaller satış geçmişinde kalır."],
  ["Aktif bilet", count.format(report.metrics.active), "Bu kapsamda düzenlenen ve halen iptal edilmemiş biletler."],
  ["İptal edilen bilet", count.format(report.metrics.cancelled), "Kesinleşmiş iptaller; işlem modunda iptal tarihi esas alınır."],
  ["Doğrulanmış tahsilat", money.format(report.metrics.collected), "Yalnızca doğrulanmış başarılı ödemeler."],
  ["Tamamlanan iade", money.format(report.metrics.refunded), "Yalnızca başarıyla tamamlanan iadeler."],
  ["Bekleyen iade tutarı", money.format(report.metrics.pendingRefund), "Halen bekleyen; işlem modunda bu duruma geçiş tarihi."],
  ["Başarısız iade", `${report.metrics.failedRefundCount} adet · ${money.format(report.metrics.failedRefundAmount)}`, "Halen başarısız; işlem modunda bu duruma geçiş tarihi."],
  ["Tahsilat − tamamlanan iade", money.format(report.metrics.collectedMinusRefunded), "Bu iki metriğin farkıdır; kâr değildir."],
 ] : [];
 const filters = (<section className="company-panel"><form className="report-filters" onSubmit={e => { e.preventDefault(); apply(); }}>
   <label>Rapor kapsamı<select value={draft.basis} onChange={e => change("basis", e.target.value)}><option value="transaction">İşlem tarihi</option><option value="departure">Sefer kalkış tarihi</option></select></label>
   <label>Dönem<select value={draft.period} onChange={e => change("period", e.target.value)}><option value="today">Bugün</option><option value="week">Son 7 Gün</option><option value="month">Bu Ay</option><option value="custom">Özel Tarih Aralığı</option></select></label>
   {draft.period === "custom" && <><label>Başlangıç<input type="date" required value={draft.from} min="2000-01-01" max={draft.to || "2100-12-31"} onChange={e => change("from", e.target.value)} /></label><label>Bitiş (gün dahil)<input type="date" required value={draft.to} min={draft.from || "2000-01-01"} max="2100-12-31" onChange={e => change("to", e.target.value)} /></label></>}
   <label>Sefer numarası<input type="number" min="1" step="1" placeholder="Tüm seferler" value={draft.tripId} onChange={e => change("tripId", e.target.value)} /></label>
   <label>Güzergâh numarası<input type="number" list="report-routes" min="1" step="1" placeholder="Tüm güzergâhlar" value={draft.routeId} onChange={e => change("routeId", e.target.value)} /><datalist id="report-routes">{routes.map(r => <option key={r.id} value={r.id}>{r.departureCity} → {r.arrivalCity}</option>)}</datalist></label>
   {!overview && <><label>Sefer sıralaması<select value={draft.sort} onChange={e => change("sort", e.target.value)}>{[["departure", "Kalkış"], ["sold", "Satılan bilet"], ["collected", "Tahsilat"], ["refunded", "Tamamlanan iade"], ["occupancy", "Doluluk"], ["available", "Satışa açık koltuk"]].map(([v, n]) => <option key={v} value={v}>{n}</option>)}</select></label><label>Sıra<select value={draft.direction} onChange={e => change("direction", e.target.value)}><option value="asc">Artan</option><option value="desc">Azalan</option></select></label><label>Güzergâh sıralaması<select value={draft.routeSort} onChange={e => change("routeSort", e.target.value)}><option value="sold">Satılan bilet</option><option value="collected">Tahsilat</option><option value="refunded">Tamamlanan iade</option></select></label></>}
   <div className="company-form-actions"><button className="primary-button" type="submit">Filtreleri uygula</button><button className="secondary-button" type="button" onClick={() => { setParams({}); setFormError(""); }}>Temizle</button></div>
  </form>{formError && <p role="alert" className="company-profile-error">{formError}</p>}</section>);
 if (overview) return <CompanyOverviewView report={report} loading={loading} error={error} filters={filters} refresh={() => setRevision(r => r + 1)} revision={revision} query={query} cards={cards} />;
 return <div className="company-dashboard company-reporting">
  <header className="company-dashboard-header"><h1>Raporlar</h1></header>
  {filters}
  <div className="report-toolbar"><button className="secondary-button" disabled={loading} onClick={() => setRevision(r => r + 1)}>Yenile</button>{report && <span>Son güncelleme: {dateTime.format(new Date(report.scope.generatedAt))} (Türkiye)</span>}</div>
  {loading ? <p role="status">Raporlar yükleniyor...</p> : error ? <div role="alert" className="company-profile-error"><p>{error}</p><button className="secondary-button" onClick={() => setRevision(r => r + 1)}>Tekrar dene</button></div> : report && <>
   <section className="company-panel report-scope">
    <p className="report-summary-line">{label} · {dateLabel(report.scope.from)} – {dateLabel(report.scope.to)} · {report.scope.basis === "departure" ? "Sefer kalkış tarihi" : "İşlem tarihi"}</p>
    <Details title="Rapor hakkında"><p>{report.scope.dateExplanation}</p><p>Tarihler Türkiye saatindedir. Başlangıç ve bitiş günleri dahildir.</p>
     <ul>{cards.map(([title, , description]) => <li key={title}><strong>{title}: </strong>{description}</li>)}</ul>
     <p>Doluluk, aktif satılmış koltukların toplam kapasiteye oranıdır. Eski ücretsiz kayıtlar satış sayılmaz. Süresi dolmuş tutmalar boş koltuk sayılır.</p>
     <p>{report.scope.basis === "transaction" ? "Koltuk bilgileri, bu dönemde işlem yapılan seferlerin güncel durumudur." : "Koltuk bilgileri, seçili tarihlerde kalkan seferlerin güncel durumudur."}</p>
     <p>Güzergâh önerileri ilk 500 kaydı içerir; numara da yazabilirsiniz. CSV tüm filtreli seferleri kapsar.</p>
     {report.unknownRefundDateCount > 0 && <p>{report.unknownRefundDateCount} eski iadenin ({money.format(report.unknownRefundDateAmount)}) durum tarihi bilinmiyor. İşlem tarihi toplamlarına katılmaz; sefer tarihi kapsamına dahildir.</p>}
    </Details>
    {report.metrics.sold === 0 && report.metrics.collected === 0 && report.metrics.refunded === 0 && report.metrics.cancelled === 0 && report.metrics.pendingRefund === 0 && report.metrics.failedRefundCount === 0 && <p>Bu dönemde işlem bulunmuyor</p>}
   </section>
   <div className="report-card-grid">{cards.map(([title, value]) => <section className="company-stat-card" key={title}><h2>{title}</h2><strong className="report-card-value">{value}</strong></section>)}
    <section className="company-stat-card"><h2>Doluluk</h2><strong className="report-card-value">{report.occupancy.percent == null ? "Hesaplanamadı" : `%${count.format(report.occupancy.percent)}`}</strong><p>{report.occupancy.activeSoldSeats} koltuk / {report.occupancy.capacity} kapasite</p>{report.occupancy.invalidTrips > 0 && <p role="status">{report.occupancy.invalidTrips} seferin koltuk bilgisi kontrol edilmeli.</p>}</section>
   </div>
   <>

    <div className="report-chart-grid"><Chart days={report.daily} label={label} /><Chart days={report.daily} cash label={label} /></div>
    <section className="company-panel"><h2>En çok bilet satılan güzergâhlar</h2><p>Seçili sıralamaya göre ilk 20 / {report.routeCount} güzergâh.</p>{report.routes.length === 0 ? <p>Bu dönemde işlem bulunmuyor</p> : <div className="table-wrap"><table className="data-table"><thead><tr><th>Güzergâh</th><th>Satılan</th><th>Aktif</th><th>İptal</th><th>Tahsilat</th><th>Tamamlanan iade</th></tr></thead><tbody>{report.routes.map(r => <tr key={r.routeId}><td>{r.departureCity} → {r.arrivalCity}</td><td>{r.sold}</td><td>{r.active}</td><td>{r.cancelled}</td><td>{money.format(r.collected)}</td><td>{money.format(r.refunded)}</td></tr>)}</tbody></table></div>}</section>
    <section className="company-panel"><h2>Sefer raporu</h2>
     <button className="secondary-button" disabled={exporting} onClick={() => void download()}>{exporting ? "CSV hazırlanıyor..." : "CSV indir"}</button>
     {report.trips.length === 0 ? <p>Bu dönemde işlem bulunmuyor{page > 1 && <button className="link-button" onClick={() => setPage(1)}>İlk sayfaya dön</button>}</p> : <div className="table-wrap report-trip-wrap"><table className="data-table report-trip-table"><thead><tr>{["Sefer", "Güzergâh", "Kalkış (Türkiye)", "Plaka / Kapasite", "Aktif satılmış", "Geçici ödeme tutması", "Eski ücretsiz blokaj", "Süresi dolmuş tutma", "Satışa açık", "Doluluk", "Dönem satışı", "İptal", "Tahsilat", "Tamamlanan iade", "Bekleyen iade", "Başarısız iade", "Sefer durumu"].map(t => <th key={t}>{t}</th>)}</tr></thead><tbody>{report.trips.map(t => <tr key={t.id}><td><Link to={`/company/trips?detail=${t.id}`}>#{t.id} Detay</Link>{t.dataWarning && <p role="status">{t.dataWarning}</p>}</td><td>{t.departureCity} → {t.arrivalCity}</td><td>{dateTime.format(new Date(t.departureTime))}</td><td>{t.plate} / {t.capacity}</td><td>{t.activeSoldSeats}</td><td>{t.heldSeats}</td><td>{t.legacyBlockedSeats}</td><td>{t.expiredHolds}</td><td>{t.availableForSale ?? "Hesaplanamadı"}</td><td>{t.occupancyPercent == null ? "Hesaplanamadı" : `%${count.format(t.occupancyPercent)}`}</td><td>{t.sold}</td><td>{t.cancelled}</td><td>{money.format(t.collected)}</td><td>{money.format(t.refunded)}</td><td>{money.format(t.pendingRefund)}</td><td>{t.failedRefundCount} · {money.format(t.failedRefundAmount)}</td><td>{tripStatus(t.status)}</td></tr>)}</tbody></table></div>}
     <div className="company-form-actions"><span>{report.totalTrips} sefer · Sayfa {page} / {Math.max(1, Math.ceil(report.totalTrips / pageSize))}</span><button className="secondary-button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Önceki</button><button className="secondary-button" disabled={page * pageSize >= report.totalTrips} onClick={() => setPage(page + 1)}>Sonraki</button></div>
    </section>
   </>
   <section className="company-panel"><h2>Başarısız iadeler</h2>{report.failedRefunds.length === 0 ? <p>Bu dönemde işlem bulunmuyor</p> : <ul className="report-list">{report.failedRefunds.map(r => <li key={r.orderId}><Link to={orderLink(r.orderId, r.tripId)}>{r.pnr ?? "Ödeme işlemi"} · {money.format(r.amount)}</Link><p>İade tamamlanamadı.</p>{r.error && <Details title="Hata açıklaması"><p>{r.error}</p></Details>}<span>{r.statusDate ? dateTime.format(new Date(r.statusDate)) : "Durum tarihi bilinmiyor"}</span></li>)}</ul>}</section>
  </>}
 </div>;
}

