import { Link } from "react-router-dom";
import type { ReactNode } from "react";
import { Details } from "../../components/UiDetails";
import CompanyAttention from "./CompanyAttention";
import { count, dateLabel, dateTime, money, orderLink } from "./reporting";
import type { Report } from "./reporting";
import "./CompanyOverviewView.css";

type IconName = "ticket" | "active" | "cancel" | "wallet" | "bus" | "activity" | "refund" | "refresh" | "seats";
function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    ticket: <><path d="M4 5h16v5a2 2 0 0 0 0 4v5H4v-5a2 2 0 0 0 0-4z" /><path d="M14 5v3m0 3v2m0 3v3" /></>,
    active: <><rect x="3" y="5" width="18" height="14" rx="3" /><path d="m8 12 3 3 5-6" /></>,
    cancel: <><rect x="3" y="5" width="18" height="14" rx="3" /><path d="m9 9 6 6m0-6-6 6" /></>,
    wallet: <><path d="M4 6h14v4M4 6l12-3v3M3 6v13h18V9H3" /><path d="M21 12h-6v4h6m-3-2h.01" /></>,
    bus: <><rect x="5" y="3" width="14" height="16" rx="3" /><path d="M5 11h14M8 19v2m8-2v2M8 15h1m6 0h1" /></>,
    activity: <><path d="M4 16V5h16v14H9m-5 0 5-5m-5 0v5h5" /><path d="M8 8h8m-8 3h5" /></>,
    refund: <><path d="M4 10a8 8 0 1 1 1 8M4 10V4m0 6h6" /><path d="M12 8v8m3-6h-4a1.5 1.5 0 0 0 0 3h2a1.5 1.5 0 0 1 0 3H9" /></>,
    refresh: <><path d="M20 7v5h-5M4 17v-5h5M6 7a7 7 0 0 1 12-1l2 6M4 12l2 6a7 7 0 0 0 12-1" /></>,
    seats: <><path d="M7 4v9h10V8M5 13v5h14v-5M7 18v3m10-3v3M7 9h7V4H7" /></>,
  };
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}
const reference = (value: string | null) => value ? value.length > 14 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value : "Ödeme işlemi";
function Empty({ icon, title, description }: { icon: IconName; title: string; description: string }) {
  return <div className="overview-empty"><span className="overview-empty-icon"><Icon name={icon} /></span><strong>{title}</strong><p>{description}</p></div>;
}

export default function CompanyOverviewView({ report, loading, error, filters, refresh, revision, query, cards }: {
  report: Report | null; loading: boolean; error: string; filters: ReactNode; refresh: () => void; revision: number; query: string; cards: string[][];
}) {
  const metrics = report?.metrics;
  const primary: { title: string; value: string; icon: IconName; tone: string; description: string }[] = metrics ? [
    { title: "Satılan bilet", value: count.format(metrics.sold), icon: "ticket", tone: "blue", description: "Seçili kapsamdaki bilet satışları" },
    { title: "Aktif bilet", value: count.format(metrics.active), icon: "active", tone: "green", description: "İptal edilmemiş dönem biletleri" },
    { title: "İptal edilen bilet", value: count.format(metrics.cancelled), icon: "cancel", tone: "rose", description: "Kesinleşen bilet iptalleri" },
    { title: "Doğrulanmış tahsilat", value: money.format(metrics.collected), icon: "wallet", tone: "indigo", description: "Başarılı ve doğrulanmış ödemeler" },
  ] : [];
  const finance = metrics ? [
    ["Tamamlanan iade", money.format(metrics.refunded)], ["Bekleyen iade tutarı", money.format(metrics.pendingRefund)],
    ["Başarısız iade", `${count.format(metrics.failedRefundCount)} adet · ${money.format(metrics.failedRefundAmount)}`],
    ["Tahsilat − tamamlanan iade", money.format(metrics.collectedMinusRefunded)],
  ] : [];
  const occupancy = report?.occupancy;
  const occupancyPercent = occupancy?.percent == null ? null : Math.max(0, Math.min(100, occupancy.percent));
  const periodText = report ? `${dateLabel(report.scope.from)} – ${dateLabel(report.scope.to)}` : "Seçili dönem";

  return <div className="company-dashboard company-reporting company-overview">
    <header className="overview-header company-dashboard-header"><div><span className="overview-eyebrow">FİRMA PERFORMANSI</span><h1>Genel Bakış</h1><p>Satışlarını, seferlerini ve işlem bekleyen kayıtlarını tek yerden takip et.</p></div><div className="overview-header-actions"><button type="button" className="secondary-button" disabled={loading} onClick={refresh}><Icon name="refresh" />Yenile</button><Link className="primary-button" to={`/company/reports${query ? `?${query}` : ""}`}>Raporları görüntüle <span aria-hidden="true">↗</span></Link></div></header>
    <div className="overview-filters">{filters}</div>
    {report && <section className="report-scope overview-scope"><div className="overview-period"><span className="overview-period-dot" aria-hidden="true" /><strong>{periodText}</strong><span>{report.scope.basis === "departure" ? "Sefer kalkış tarihi" : "İşlem tarihi"}</span></div><div className="overview-scope-tools"><span>Son güncelleme: {dateTime.format(new Date(report.scope.generatedAt))} (Türkiye)</span><Details title="Rapor hakkında"><p>{report.scope.dateExplanation}</p><p>Tarihler Türkiye saatindedir. Başlangıç ve bitiş günleri dahildir.</p><ul>{cards.map(([title, , description]) => <li key={title}><strong>{title}: </strong>{description}</li>)}</ul><p>Doluluk, aktif satılmış koltukların toplam kapasiteye oranıdır. Eski ücretsiz kayıtlar satış sayılmaz. Süresi dolmuş tutmalar boş koltuk sayılır.</p><p>{report.scope.basis === "transaction" ? "Koltuk bilgileri, bu dönemde işlem yapılan seferlerin güncel durumudur." : "Koltuk bilgileri, seçili tarihlerde kalkan seferlerin güncel durumudur."}</p><p>Güzergâh önerileri ilk 500 kaydı içerir; numara da yazabilirsiniz. CSV tüm filtreli seferleri kapsar.</p>{report.unknownRefundDateCount > 0 && <p>{report.unknownRefundDateCount} eski iadenin ({money.format(report.unknownRefundDateAmount)}) durum tarihi bilinmiyor. İşlem tarihi toplamlarına katılmaz; sefer tarihi kapsamına dahildir.</p>}</Details></div></section>}

    {loading ? <div className="overview-loading" role="status"><span>Genel bakış yükleniyor...</span><div className="overview-skeleton-grid">{[0, 1, 2, 3].map(i => <div key={i} className="overview-skeleton" />)}</div></div> : error ? <div className="company-panel overview-error" role="alert"><p>{error}</p><button type="button" className="secondary-button" onClick={refresh}>Tekrar dene</button></div> : report && <>
      <div className="report-card-grid overview-kpis">{primary.map(metric => <section className={`company-stat-card overview-metric overview-${metric.tone}`} key={metric.title}><div className="overview-metric-top"><h2>{metric.title}</h2><span className="overview-icon"><Icon name={metric.icon} /></span></div><strong className="report-card-value overview-metric-value">{metric.value}</strong><p>{metric.description}</p></section>)}</div>
      <div className="overview-finance-grid"><section className="company-panel overview-finance"><div className="overview-panel-heading"><div><span className="overview-section-label">MALİ ÖZET</span><h2>Tahsilat ve iadeler</h2></div><span className="overview-panel-icon"><Icon name="refund" /></span></div><dl className="overview-finance-list">{finance.map(([title, value], index) => <div className={index === 3 ? "overview-finance-total" : ""} key={title}><dt>{title}</dt><dd>{value}</dd></div>)}</dl><p className="overview-finance-note">Tahsilat ile tamamlanan iade farkı kârı ifade etmez.</p></section>
        <section className="company-panel overview-occupancy"><div className="overview-panel-heading"><div><span className="overview-section-label">KOLTUK KULLANIMI</span><h2>Doluluk</h2></div><span className="overview-panel-icon"><Icon name="seats" /></span></div><strong className="overview-occupancy-value">{occupancyPercent == null ? "Hesaplanamadı" : `%${count.format(occupancy!.percent!)}`}</strong>{occupancyPercent != null && <div className="overview-progress" role="meter" aria-label="Doluluk oranı" aria-valuemin={0} aria-valuemax={100} aria-valuenow={occupancyPercent}><span style={{ width: `${occupancyPercent}%` }} /></div>}<div className="overview-occupancy-counts"><div><strong>{count.format(occupancy!.activeSoldSeats)}</strong><span>aktif satılmış koltuk</span></div><div><strong>{count.format(occupancy!.capacity)}</strong><span>toplam kapasite</span></div></div><p className="overview-finance-note">{occupancy!.invalidTrips > 0 ? `${occupancy!.invalidTrips} seferin koltuk bilgisi kontrol edilmeli.` : occupancy!.capacity === 0 ? "Seçili kapsamda koltuk kapasitesi bulunmuyor." : "Seçili kapsamdaki seferlerin güncel koltuk durumu."}</p></section>
      </div>
    </>}

    <CompanyAttention revision={revision} />

    {!loading && !error && report && <>
      <div className="overview-feed-grid"><section className="company-panel overview-upcoming"><div className="overview-panel-heading"><div><span className="overview-section-label">SEFER TAKVİMİ</span><h2>Yaklaşan seferler</h2></div><Link to="/company/trips">Seferleri gör <span aria-hidden="true">→</span></Link></div><p className="overview-panel-subtitle">Seçili rapor kapsamındaki yaklaşan kalkışlar.</p>{report.upcoming.length === 0 ? <Empty icon="bus" title="Bu kapsamda yaklaşan sefer yok" description="Farklı bir dönem seçebilir veya Sefer Yönetimi’nden tüm seferlerini inceleyebilirsin." /> : <ul className="overview-feed">{report.upcoming.slice(0, 5).map(trip => <li key={trip.id}><span className="overview-feed-icon"><Icon name="bus" /></span><div className="overview-feed-content"><Link to={`/company/trips?detail=${trip.id}`}>{trip.departureCity} <span aria-hidden="true">→</span> {trip.arrivalCity}</Link><span>{dateTime.format(new Date(trip.departureTime))} · Sefer #{trip.id}</span><small>{trip.plate} · {trip.activeSoldSeats} satılmış / {trip.capacity} koltuk</small>{trip.dataWarning && <p className="overview-warning">{trip.dataWarning}</p>}</div></li>)}</ul>}</section>
        <section className="company-panel overview-activity"><div className="overview-panel-heading"><div><span className="overview-section-label">İŞLEM AKIŞI</span><h2>Son satışlar ve iptaller</h2></div><Link to="/company/reservations">Tüm işlemler <span aria-hidden="true">→</span></Link></div><p className="overview-panel-subtitle">Seçili kapsamdaki en son işlemler.</p>{report.recent.length === 0 ? <Empty icon="activity" title="Henüz işlem bulunmuyor" description="Bu kapsamdaki satış ve iptaller burada listelenir." /> : <ul className="overview-feed">{report.recent.slice(0, 5).map((activity, index) => <li key={`${activity.orderId}-${index}`}><span className={`overview-feed-icon ${activity.type === "Sale" ? "overview-sale" : "overview-cancel"}`}><Icon name={activity.type === "Sale" ? "ticket" : "cancel"} /></span><div className="overview-feed-content"><Link to={orderLink(activity.orderId, activity.tripId)} aria-label={`${activity.type === "Sale" ? "Satış" : "İptal"} · ${activity.pnr ?? "Ödeme işlemi"}`}><strong>{activity.type === "Sale" ? "Bilet satışı" : "Bilet iptali"}</strong><span className="overview-reference" title={activity.pnr ?? undefined}>{reference(activity.pnr)}</span></Link><span>{dateTime.format(new Date(activity.date))}</span></div><strong className="overview-feed-amount">{money.format(activity.amount)}</strong></li>)}</ul>}{report.recent.length > 5 && <p className="overview-list-note">Son 5 işlem gösteriliyor.</p>}</section>
      </div>
      <section className={`company-panel overview-refunds ${report.failedRefunds.length === 0 ? "overview-refunds-clear" : ""}`}><div className="overview-panel-heading"><div className="overview-refund-heading"><span className="overview-refund-symbol" aria-hidden="true">{report.failedRefunds.length === 0 ? "✓" : "!"}</span><div><h2>Başarısız iadeler</h2>{report.failedRefunds.length === 0 && <p>Seçili kapsamda başarısız iade bulunmuyor.</p>}</div></div>{report.failedRefunds.length > 0 && <span className="overview-attention-badge">Kontrol gerekiyor</span>}</div>{report.failedRefunds.length > 0 && <ul className="overview-refund-list">{report.failedRefunds.map(refund => <li key={refund.orderId}><div><Link to={orderLink(refund.orderId, refund.tripId)} title={refund.pnr ?? undefined}>{reference(refund.pnr)}</Link><span>{refund.statusDate ? dateTime.format(new Date(refund.statusDate)) : "Durum tarihi bilinmiyor"}</span></div><strong>{money.format(refund.amount)}</strong>{refund.error && <Details title="Hata açıklaması"><p>{refund.error}</p></Details>}</li>)}</ul>}</section>
    </>}
  </div>;
}
