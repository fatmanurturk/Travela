import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { apiUrl } from "../../api";
import { paymentLabel, refundLabel } from "../../payments";
import { money } from "../../reservations";
import "./AdminTickets.css";

type Ticket = {
  id: number; orderId: string | null; reservationNumber: string; pnr: string | null; kind: string; tripId: number;
  companyId: number; companyName: string; passengerId: number; accountName: string; passengerName: string;
  passengerEmail: string | null; passengerPhone: string | null; passengerSnapshotSource: string | null; passengerCapturedAt: string | null;
  departureCity: string; arrivalCity: string; departureTime: string; seatNumber: number; amount: number;
  paymentStatus: string; refundStatus: string; ticketStatus: string; isTest: boolean | null; needsAttention: boolean; createdAt: string;
};
type History = { id: number; type: string; createdAt: string; paymentStatus: string; refundStatus: string; refundAmount: number; actorName: string | null; actorUserId: number | null; reason: string | null };
type Detail = { ticket: Ticket; cancellation: { cancelledAt: string | null; cancellationReason: string | null }; payment: null | {
  provider: string; providerReference: string | null; paidAt: string | null; refundAmount: number; refundAttempts: number; refundReference: string | null;
  refundRequestedAt: string | null; refundedAt: string | null; nextRefundAttemptAt: string | null; lastReconciledAt: string | null;
  nextReconciliationAt: string | null; reconciliationAttempts: number; lastError: string | null; canRetryRefund: boolean; canReconcile: boolean;
}; history: { items: History[]; totalCount: number; pageNumber: number; pageSize: number } };
type Filters = { search: string; companyId: string; paymentStatus: string; refundStatus: string; needsAttention: string; from: string; to: string };
const empty: Filters = { search: "", companyId: "", paymentStatus: "", refundStatus: "", needsAttention: "", from: "", to: "" };
type Request = <T>(path: string, options?: RequestInit) => Promise<T>;
const dates = new Intl.DateTimeFormat("tr-TR", { dateStyle: "short", timeStyle: "short", timeZone: "Europe/Istanbul" });
const date = (value: string | null) => value ? dates.format(new Date(value)) : "—";
const historyLabels: Record<string, string> = {
  ImportedState: "Geçişte kaydedilen durum", CheckoutCreated: "Ödeme adımı açıldı", TicketIssued: "Ödeme doğrulandı, bilet düzenlendi",
  PaymentFailed: "Ödeme başarısız", RefundRequested: "İade talebi kaydedildi", RefundAttemptStarted: "İade denemesi başladı",
  PaymentConfirmed: "Ödeme sağlayıcıdan doğrulandı", PaymentPending: "Sağlayıcıda ödeme sonucu bekleniyor",
  RefundSucceeded: "İade tamamlandı", RefundPending: "İade sonucu bekleniyor", RefundFailed: "İade denemesi başarısız",
  ManualRefundRetryQueued: "Yönetici iadeyi yeniden denemeye aldı", ManualReconciliationQueued: "Yönetici ödeme sorgulaması istedi",
};

export default function AdminTickets() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const selected = Number(params.get("detail")) || 0;
  const [draft, setDraft] = useState<Filters>(empty);
  const [filters, setFilters] = useState<Filters>(empty);
  const [companies, setCompanies] = useState<{ id: number; name: string }[]>([]);
  const [items, setItems] = useState<Ticket[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const request: Request = useCallback(async <T,>(path: string, options?: RequestInit) => {
    const response = await fetch(apiUrl(path), { ...options, cache: "no-store", headers: { "Content-Type": "application/json", Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` } });
    const data = await response.json().catch(() => null);
    if (response.status === 401) {
      sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); navigate("/login", { replace: true });
      throw new Error("Oturumunuz sona erdi. Yeniden giriş yapın.");
    }
    if (!response.ok) throw new Error(data?.message ?? (response.status === 403 ? "Bu işlem için yönetici yetkisi gerekli." : "İşlem tamamlanamadı. Lütfen tekrar deneyin."));
    return data as T;
  }, [navigate]);
  const refresh = useCallback(() => { setLoading(true); setError(""); setRevision(value => value + 1); }, []);
  useEffect(() => {
    const abort = new AbortController();
    request<{ id: number; name: string }[]>("/api/admin/companies", { signal: abort.signal }).then(setCompanies)
      .catch(cause => { if (!abort.signal.aborted) setError(cause instanceof Error ? cause.message : "Firma seçenekleri alınamadı."); });
    return () => abort.abort();
  }, [request]);
  useEffect(() => {
    const abort = new AbortController();
    const query = new URLSearchParams({ pageNumber: String(page), pageSize: "20" });
    Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, value); });
    request<{ items: Ticket[]; totalCount: number }>(`/api/admin/tickets?${query}`, { signal: abort.signal })
      .then(data => { if (!abort.signal.aborted) { setItems(data.items); setTotal(data.totalCount); } })
      .catch(cause => { if (!abort.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı." : cause.message); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [filters, page, revision, request]);
  const change = (key: keyof Filters, value: string) => setDraft(current => ({ ...current, [key]: value }));
  const pages = Math.max(1, Math.ceil(total / 20));
  return <div className="admin-dashboard admin-tickets">
    <div className="dashboard-heading"><h1>Bilet ve ödeme yönetimi</h1><p>Tüm firmaların biletlerini, ödeme ve iade işlemlerini inceleyin.</p></div>
    <form className="dashboard-section" onSubmit={event => { event.preventDefault(); setFilters({ ...draft }); setPage(1); setLoading(true); setError(""); }}>
      <div className="form-grid">
        <label>PNR, sipariş, yolcu veya firma<input type="search" maxLength={200} value={draft.search} onChange={event => change("search", event.target.value)} /></label>
        <label>Firma<select value={draft.companyId} onChange={event => change("companyId", event.target.value)}><option value="">Tüm firmalar</option>{companies.map(company => <option key={company.id} value={company.id}>{company.name}</option>)}</select></label>
        <label>Ödeme durumu<select value={draft.paymentStatus} onChange={event => change("paymentStatus", event.target.value)}><option value="">Tümü</option>{["Pending", "Paid", "Failed", "Unpaid"].map(value => <option key={value} value={value}>{paymentLabel(value)}</option>)}</select></label>
        <label>İade durumu<select value={draft.refundStatus} onChange={event => change("refundStatus", event.target.value)}><option value="">Tümü</option>{["None", "Pending", "Failed", "Succeeded"].map(value => <option key={value} value={value}>{refundLabel(value)}</option>)}</select></label>
        <label>Destek takibi<select value={draft.needsAttention} onChange={event => change("needsAttention", event.target.value)}><option value="">Tümü</option><option value="true">İnceleme gerekenler</option><option value="false">Diğer işlemler</option></select></label>
        <label>Kayıt başlangıcı<input type="date" value={draft.from} onChange={event => change("from", event.target.value)} /></label>
        <label>Kayıt bitişi<input type="date" value={draft.to} min={draft.from || undefined} onChange={event => change("to", event.target.value)} /></label>
      </div><div className="detail-actions"><button className="primary-button" disabled={loading} type="submit">Ara</button><button className="secondary-button" type="button" onClick={() => { setDraft(empty); setFilters({ ...empty }); setPage(1); refresh(); }}>Temizle</button><button className="secondary-button" type="button" onClick={refresh}>Yenile</button></div>
    </form>
    {error && <p role="alert" className="form-alert error">{error}</p>}
    <section className="dashboard-section"><p role="status">{loading ? "İşlemler yükleniyor..." : `${total} kayıt bulundu`}</p>
      {!loading && !error && (items.length ? <div className="table-wrap"><table className="data-table"><thead><tr><th>PNR / İşlem</th><th>Yolcu / Firma</th><th>Sefer</th><th>Tutar</th><th>Ödeme / İade</th><th>İşlem</th></tr></thead><tbody>{items.map(item => <tr key={item.id}>
        <td><strong>{item.pnr ?? item.reservationNumber}</strong><small>{item.kind === "Legacy" ? "Eski ücretsiz kayıt" : item.kind === "Hold" ? "Ödeme işlemi" : "Bilet"}{item.isTest ? " · TEST" : ""}</small></td>
        <td>{item.passengerName}<small>{item.companyName}</small>{item.needsAttention && <strong>İnceleme gerekiyor</strong>}</td>
        <td>{item.departureCity} → {item.arrivalCity}<small>Sefer #{item.tripId} · Koltuk {item.seatNumber}</small><small>{date(item.departureTime)}</small></td>
        <td>{money.format(item.amount)}<small>{item.ticketStatus === "Cancelled" ? "İptal edildi" : "Aktif"}</small></td>
        <td>{paymentLabel(item.paymentStatus)}<small>{refundLabel(item.refundStatus)}</small></td>
        <td><button type="button" className="link-button" onClick={() => setParams({ detail: String(item.id) })}>İncele</button></td>
      </tr>)}</tbody></table></div> : <p>Bu filtrelerle eşleşen kayıt bulunamadı.</p>)}
      <div className="pagination-row"><button type="button" disabled={loading || page <= 1} onClick={() => { setLoading(true); setPage(value => value - 1); }}>Önceki</button><span>{page} / {pages}</span><button type="button" disabled={loading || page >= pages} onClick={() => { setLoading(true); setPage(value => value + 1); }}>Sonraki</button></div>
    </section>
    {selected > 0 && <TicketDetail key={selected} id={selected} request={request} changed={refresh} close={() => setParams({})} />}
  </div>;
}

function TicketDetail({ id, request, changed, close }: { id: number; request: Request; changed: () => void; close: () => void }) {
  const [data, setData] = useState<Detail | null>(null);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [reason, setReason] = useState("");
  const [action, setAction] = useState<"retry-refund" | "reconcile">("retry-refund");
  const [historyPage, setHistoryPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const pendingKey = useRef<{ action: string; reason: string; key: string } | null>(null);
  useEffect(() => {
    const abort = new AbortController();
    const load = () => request<Detail>(`/api/admin/tickets/${id}?historyPage=${historyPage}`, { signal: abort.signal })
      .then(value => { if (!abort.signal.aborted) { setData(value); setError(""); } })
      .catch(cause => { if (!abort.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı." : cause.message); });
    void load(); const timer = window.setInterval(() => void load(), 10000);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [id, request, revision, historyPage]);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const trimmed = reason.trim();
    if (trimmed.length < 10) { setError("En az 10 karakterlik işlem gerekçesi yazın."); return; }
    if (!pendingKey.current || pendingKey.current.action !== selectedAction || pendingKey.current.reason !== trimmed)
      pendingKey.current = { action: selectedAction, reason: trimmed, key: crypto.randomUUID() };
    setBusy(true); setError(""); setMessage("");
    try {
      const response = await request<{ message: string }>(`/api/admin/tickets/${id}/${selectedAction}`, { method: "POST", body: JSON.stringify({ reason: trimmed, requestKey: pendingKey.current.key }) });
      setMessage(response.message); setReason(""); pendingKey.current = null; setHistoryPage(1); setRevision(value => value + 1); changed();
    } catch (cause) { setError(cause instanceof TypeError ? "Yanıt alınamadı. Geçmişi kontrol edin; aynı gerekçeyle yeniden denerseniz aynı işlem anahtarı kullanılır." : cause instanceof Error ? cause.message : "İşlem tamamlanamadı."); }
    finally { setBusy(false); }
  }
  const payment = data?.payment;
  const selectedAction = payment?.canRetryRefund && payment.canReconcile ? action : payment?.canRetryRefund ? "retry-refund" : "reconcile";
  return <section className="dashboard-section admin-ticket-detail" aria-labelledby="admin-ticket-title">
    <div className="dashboard-heading"><h2 id="admin-ticket-title">İşlem detayı #{id}</h2><button className="secondary-button" type="button" disabled={busy} onClick={close}>Detayı kapat</button></div>
    {error && <p role="alert" className="form-alert error">{error}</p>}{message && <p role="status" className="form-alert success">{message}</p>}
    {!data ? <p>İşlem detayları yükleniyor...</p> : <>
      <dl className="admin-ticket-facts"><div><dt>PNR</dt><dd>{data.ticket.pnr ?? "Düzenlenmedi"}</dd></div><div><dt>Sipariş</dt><dd>{data.ticket.orderId ?? "Ödeme kaydı yok"}</dd></div>
        <div><dt>Bilette kayıtlı yolcu</dt><dd>{data.ticket.passengerName}</dd></div><div><dt>Güncel hesap sahibi</dt><dd>{data.ticket.accountName} · #{data.ticket.passengerId}</dd></div>
        <div><dt>Bilet iletişim e-postası</dt><dd>{data.ticket.passengerEmail ?? "Kaydedilmemiş"}</dd></div><div><dt>Bilet iletişim telefonu</dt><dd>{data.ticket.passengerPhone ?? "Kaydedilmemiş"}</dd></div>
        <div><dt>Firma</dt><dd>{data.ticket.companyName}</dd></div><div><dt>Tutar</dt><dd>{money.format(data.ticket.amount)}{data.ticket.isTest ? " · TEST" : ""}</dd></div>
        <div><dt>Ödeme</dt><dd>{paymentLabel(data.ticket.paymentStatus)}</dd></div><div><dt>İade</dt><dd>{refundLabel(data.ticket.refundStatus)}</dd></div>
        {payment && <><div><dt>Sağlayıcı / Tahsilat referansı</dt><dd>{payment.provider} · {payment.providerReference ?? "—"}</dd></div><div><dt>İade referansı</dt><dd>{payment.refundReference ?? "—"}</dd></div>
          <div><dt>Ödeme zamanı</dt><dd>{date(payment.paidAt)}</dd></div><div><dt>İade talebi / Tamamlanması</dt><dd>{date(payment.refundRequestedAt)} / {date(payment.refundedAt)}</dd></div>
          <div><dt>İade denemeleri / Sonraki deneme</dt><dd>{payment.refundAttempts} / {date(payment.nextRefundAttemptAt)}</dd></div><div><dt>Son ödeme sorgulaması</dt><dd>{date(payment.lastReconciledAt)}</dd></div></>}
      </dl>
      {data.ticket.passengerSnapshotSource === "Migration" && <p>Eski kaydın yolcu bilgileri sistem geçişinde mevcut hesaptan sabitlenmiştir; satın alma anındaki özgün bilgiler doğrulanamamaktadır.</p>}
      {data.cancellation.cancellationReason && <p>İptal: {data.cancellation.cancellationReason} · {date(data.cancellation.cancelledAt)}</p>}
      {payment?.lastError && <p role="note">Son hata: {payment.lastError}</p>}
      {payment && (payment.canRetryRefund || payment.canReconcile) && <form className="admin-ticket-operation" onSubmit={submit}>
        <h3>Gerekçeli işlem</h3><p>Talep kuyruğa alınır. Ödeme veya iade sonucu sağlayıcıdan doğrulanmadan tamamlandı sayılmaz.</p>
        <label>Yapılacak işlem<select value={selectedAction} onChange={event => setAction(event.target.value as typeof action)} disabled={busy}>
          {payment.canRetryRefund && <option value="retry-refund">İadeyi yeniden denemeye al</option>}{payment.canReconcile && <option value="reconcile">Ödemeyi tekrar sorgula</option>}
        </select></label><label>İşlem gerekçesi<textarea required minLength={10} maxLength={1000} value={reason} disabled={busy} onChange={event => setReason(event.target.value)} /></label>
        <button className="primary-button" type="submit" disabled={busy}>{busy ? "Kaydediliyor..." : "Gerekçeyle kuyruğa al"}</button>
      </form>}
      <h3>Ödeme ve iade geçmişi</h3>
      {data.history.items.length ? <ol className="admin-payment-history">{data.history.items.map(entry => <li key={entry.id}><strong>{historyLabels[entry.type] ?? "İşlem kaydı"}</strong><time>{date(entry.createdAt)}</time>
        <p>{paymentLabel(entry.paymentStatus)} · {refundLabel(entry.refundStatus)}{entry.refundAmount > 0 ? ` · ${money.format(entry.refundAmount)}` : ""}</p>
        {entry.actorUserId && <p>İşlemi yapan: {entry.actorName ?? "Yönetici"} · #{entry.actorUserId}</p>}{entry.reason && <p>Gerekçe: {entry.reason}</p>}
      </li>)}</ol> : <p>Bu kayıt için ödeme hareketi bulunmuyor.</p>}
      <div className="pagination-row"><button type="button" disabled={historyPage <= 1} onClick={() => setHistoryPage(value => value - 1)}>Önceki hareketler</button><span>Geçmiş {historyPage} / {Math.max(1, Math.ceil(data.history.totalCount / 20))}</span><button type="button" disabled={historyPage * 20 >= data.history.totalCount} onClick={() => setHistoryPage(value => value + 1)}>Diğer hareketler</button></div>
    </>}
  </section>;
}
