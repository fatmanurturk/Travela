import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Details, Identifier } from "../../components/UiDetails";
import { apiUrl } from "../../api";
import { dateTime, money, reservationStatusLabel } from "../../reservations";
import { paymentLabel, refundLabel, statusTone } from "../../payments";
import type { Reservation } from "../../reservations";

type CompanyReservation = Reservation & { passengerName: string; kind: string; pnr: string | null; paymentStatus: string; refundStatus: string; refundAmount: number; isTest: boolean; lastError: string | null; needsAttention: boolean; refundAttempts: number };
type Filters = { tripId: string; dateFrom: string; dateTo: string; status: string; orderId: string };
const emptyFilters: Filters = { tripId: "", dateFrom: "", dateTo: "", status: "", orderId: "" };
const initialFilters = (): Filters => ({ ...emptyFilters, tripId: new URLSearchParams(location.search).get("tripId") ?? "", orderId: new URLSearchParams(location.search).get("orderId") ?? "" });

export default function CompanyReservations() {
  const [items, setItems] = useState<CompanyReservation[]>([]);
  const [draft, setDraft] = useState<Filters>(initialFilters);
  const [filters, setFilters] = useState<Filters>(initialFilters);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const navigate = useNavigate();
  const pageSize = 20;
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const query = new URLSearchParams({ pageNumber: String(page), pageSize: String(pageSize) });
        for (const [key, value] of Object.entries(filters)) if (value) query.set(key, value);
        const response = await fetch(apiUrl(`/api/reservations/company?${query}`), {
          headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token") ?? ""}` }, signal: controller.signal,
        });
        if (response.status === 401) {
          sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true }); return;
        }
        const data = await response.json().catch(() => null);
        if (!response.ok) throw new Error(response.status === 403 ? "Bu sayfa için aktif bir firma hesabı gereklidir." : data?.message ?? "Bilet ve ödeme işlemleri alınamadı.");
        setItems(data.items as CompanyReservation[]); setTotal(data.totalCount as number);
      } catch (cause) {
        if (!controller.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Lütfen tekrar deneyiniz." : cause instanceof Error ? cause.message : "Bilet ve ödeme işlemleri alınamadı.");
      } finally { if (!controller.signal.aborted) setLoading(false); }
    })();
    return () => controller.abort();
  }, [filters, page, revision, navigate]);
  const reload = () => { setLoading(true); setError(""); setRevision(v => v + 1); };
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  return <div className="company-dashboard">
    <div className="company-dashboard-header"><h1>Bilet ve ödeme işlemleri</h1></div>
    <section className="company-panel">
      <form className="company-reservation-filters" onSubmit={event => { event.preventDefault(); setFilters({ ...draft }); setPage(1); reload(); }}>
        {draft.orderId && <label>İşlem filtresi<Identifier value={draft.orderId} /><button className="link-button" type="button" onClick={() => setDraft({ ...draft, orderId: "" })}>İşlem filtresini kaldır</button></label>}
        <label>Sefer numarası<input type="number" min="1" step="1" value={draft.tripId} onChange={e => setDraft({ ...draft, tripId: e.target.value })} placeholder="Tüm seferler" /></label>
        <label>Kalkış tarihi başlangıcı<input type="date" value={draft.dateFrom} max={draft.dateTo || undefined} onChange={e => setDraft({ ...draft, dateFrom: e.target.value })} /></label>
        <label>Kalkış tarihi bitişi<input type="date" value={draft.dateTo} min={draft.dateFrom || undefined} onChange={e => setDraft({ ...draft, dateTo: e.target.value })} /></label>
        <label>Kayıt durumu<select value={draft.status} onChange={e => setDraft({ ...draft, status: e.target.value })}><option value="">Tümü</option><option value="Active">Aktif</option><option value="Cancelled">İptal Edildi</option></select></label>
        <div className="company-form-actions"><button className="primary-button" disabled={loading} type="submit">Filtrele</button><button className="secondary-button" disabled={loading} type="button" onClick={() => { setDraft(emptyFilters); setFilters(emptyFilters); setPage(1); reload(); }}>Temizle</button></div>
      </form>
      {loading ? <p className="company-empty-state" role="status">Bilet ve ödeme işlemleri yükleniyor...</p> : error ? <div className="company-profile-error" role="alert"><p>{error}</p><button type="button" className="secondary-button" onClick={reload}>Tekrar dene</button></div> : <>
        <p role="status">Toplam {total} kayıt</p>
        {items.length === 0 ? <div className="company-empty-state">Bu dönemde işlem bulunmuyor</div> : <>{items.some(item => item.kind !== "Legacy") ? <ReservationTable items={items.filter(item => item.kind !== "Legacy")} /> : <p>Bu dönemde işlem bulunmuyor</p>}{items.some(item => item.kind === "Legacy") && <Details title="Eski ücretsiz kayıtlar"><p>Bu kayıtlar satın alınmış bilet değildir.</p><ReservationTable items={items.filter(item => item.kind === "Legacy")} /></Details>}</>}
        <div className="company-form-actions"><span>Sayfa {page} / {totalPages}</span><button className="secondary-button" type="button" disabled={page <= 1} onClick={() => { setLoading(true); setError(""); setPage(v => v - 1); }}>Önceki</button><button className="secondary-button" type="button" disabled={page >= totalPages} onClick={() => { setLoading(true); setError(""); setPage(v => v + 1); }}>Sonraki</button><button className="secondary-button" type="button" onClick={reload}>Yenile</button></div>
      </>}
    </section>
  </div>;
}

function ReservationTable({ items }: { items: CompanyReservation[] }) { return <div className="table-wrap"><table className="data-table company-reservation-table"><thead><tr><th>PNR / İşlem / Sefer</th><th>Yolcu</th><th>Güzergâh</th><th>Kalkış</th><th>Koltuk</th><th>Fiyat</th><th>Durum</th><th>Ödeme</th><th>İade</th></tr></thead><tbody>{items.map(item => <tr key={item.reservationNumber}>
          <td><span className="reservation-number">{item.pnr ?? <Identifier value={item.reservationNumber} />}</span>{item.kind === "Legacy" ? <p>Eski ücretsiz kayıt</p> : item.kind === "Hold" ? <p>Ödeme işlemi</p> : null}<br />Sefer #{item.tripId}</td><td>{item.passengerName}</td><td>{item.departureCity} → {item.arrivalCity}</td><td>{dateTime.format(new Date(item.departureTime))}</td><td>{item.seatNumber}</td><td>{money.format(item.price)}</td><td><strong className={`reservation-state ${item.status === "Cancelled" ? "cancelled" : "active"}`}>{reservationStatusLabel(item.status)}</strong>{item.cancelledAt && <p>{dateTime.format(new Date(item.cancelledAt))}</p>}{item.cancellationReason && <Details title="İptal nedeni"><p>{item.cancellationReason}</p></Details>}</td><td><span className={`status-text ${statusTone(item.paymentStatus)}`}>{paymentLabel(item.paymentStatus)}</span></td><td><span className={`status-text ${statusTone(item.refundStatus)}`}>{refundLabel(item.refundStatus)}</span>{item.refundAmount > 0 && <p>{money.format(item.refundAmount)}</p>}{item.lastError && <Details title="İade açıklaması"><p>{item.lastError}</p></Details>}{item.needsAttention && <strong>Destek takibi gerekiyor</strong>}</td>
        </tr>)}</tbody></table></div>; }
