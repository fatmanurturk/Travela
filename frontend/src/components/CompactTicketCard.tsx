import { Link } from "react-router-dom";
import { dateTime, money } from "../reservations";
import { isIssuedTicket, paymentLabel, refundLabel, refundMessage, safeCheckoutPath, statusTone } from "../payments";
import type { Purchase } from "../payments";
import "./CompactTicketCard.css";

export type TicketCardProps = { item: Purchase; onCancel?: (item: Purchase) => void; busy?: boolean; showCheckout?: boolean };
const shortDate = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short", timeZone: "Europe/Istanbul" });

export default function CompactTicketCard({ item, onCancel, busy = false, showCheckout = true }: TicketCardProps) {
  const issued = isIssuedTicket(item);
  const checkout = safeCheckoutPath(item.checkoutUrl);
  const status = issued ? item.ticketStatus === "Active" ? "Aktif bilet" : "İptal edildi" : paymentLabel(item.paymentStatus);
  const tone = statusTone(issued ? item.ticketStatus : item.paymentStatus);
  return <article className="reservation-card ticket-compact" aria-label={`${item.departureCity} → ${item.arrivalCity}, koltuk ${item.seatNumber}`}>
    <header className="ticket-compact-header">
      <div><h2>{item.departureCity} <span aria-hidden="true">→</span> {item.arrivalCity}</h2><p className="ticket-compact-meta">{item.companyName} <span>· Sefer #{item.tripId}</span>{item.pnr && <span title={item.pnr}> · PNR {item.pnr.length > 14 ? `${item.pnr.slice(0, 8)}…${item.pnr.slice(-4)}` : item.pnr}</span>}</p></div>
      <span className={`ticket-compact-badge ${tone}`}>{status}</span>
    </header>
    <dl className="ticket-compact-facts">
      <div><dt>Kalkış <span>(Türkiye)</span></dt><dd>{shortDate.format(new Date(item.departureTime))}</dd></div>
      <div><dt>Varış <span>(Türkiye)</span></dt><dd>{shortDate.format(new Date(item.arrivalTime))}</dd></div>
      <div><dt>Koltuk</dt><dd>{item.seatNumber}</dd></div>
      <div><dt>Bilet tutarı</dt><dd className="ticket-compact-price">{money.format(item.amount)}</dd></div>
      {!!item.discountAmount && <div><dt>{item.campaignName ?? "Kampanya indirimi"}</dt><dd>−{money.format(item.discountAmount)}</dd></div>}
    </dl>
    {(item.refundStatus !== "None" || item.needsAttention) && <div className="ticket-compact-notices">
      {item.refundStatus !== "None" && <p className={`status-text ${statusTone(item.refundStatus)}`}>{refundMessage(item.refundStatus)} · {money.format(item.refundAmount)}</p>}
      {item.needsAttention && <p role="status">Bu işlem destek takibi gerektiriyor.</p>}
    </div>}
    {issued && item.ticketStatus === "Active" && !item.canCancel && <p className="ticket-compact-note">Bu bilet için yolcu iptal süresi doldu.</p>}
    {item.changeCancellationUntil && item.canCancel && <p className="ticket-compact-note">Sefer değişikliği nedeniyle yeni kalkışa kadar tam iadeyle iptal hakkınız var.</p>}
    <div className="ticket-compact-footer">
      <details className="ticket-extra"><summary>İşlem ayrıntıları</summary><dl className="ticket-extra-facts">
        {item.pnr && <div><dt>PNR / Bilet numarası</dt><dd className="reservation-number">{item.pnr}</dd></div>}
        <div><dt>Ödeme durumu</dt><dd>{paymentLabel(item.paymentStatus)}</dd></div>
        <div><dt>İade durumu</dt><dd>{refundLabel(item.refundStatus)}{item.refundStatus !== "None" && ` · ${money.format(item.refundAmount)}`}</dd></div>
        {item.pnr && <div><dt>Yolcu iptali son zamanı (Türkiye)</dt><dd>{dateTime.format(new Date(item.cancelDeadline))}</dd></div>}
        {!item.pnr && item.holdExpiresAt && <div><dt>Ödeme için koltuk tutma sonu</dt><dd>{dateTime.format(new Date(item.holdExpiresAt))}</dd></div>}
        {item.cancelledAt && <div><dt>İptal zamanı</dt><dd>{dateTime.format(new Date(item.cancelledAt))}</dd></div>}
      </dl>
        {item.cancellationReason && <div className="ticket-extra-note"><strong>İptal nedeni</strong><p>{item.cancellationReason}</p></div>}
        {item.lastError && <div className="ticket-extra-note"><strong>İşlem açıklaması</strong><p>{item.lastError}</p></div>}
      </details>
      <div className="ticket-compact-actions">
        <Link to={`/trips/${item.tripId}`} className="site-button secondary">Sefer detayı</Link>
        {item.purchaseGroupId && <Link to={`/purchase/${item.purchaseGroupId}`} className="site-button secondary">Ortak sipariş</Link>}
        {issued && <Link to={`/tickets/${item.pnr}`} className="site-button primary">Bilet detayını gör</Link>}
        {showCheckout && checkout && <Link to={checkout} className="site-button primary">Ödemeye devam et</Link>}
        {onCancel && item.canCancel && <button type="button" className="site-button reservation-cancel" disabled={busy} onClick={() => onCancel(item)}>Bileti İptal Et</button>}
      </div>
    </div>
  </article>;
}
