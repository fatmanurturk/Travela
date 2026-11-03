import { Link } from "react-router-dom";
import { dateTime, money } from "../reservations";
import { isIssuedTicket, paymentLabel, refundLabel, refundMessage, safeCheckoutPath, statusTone } from "../payments";
import { Details } from "./UiDetails";
import type { Purchase } from "../payments";

export default function TicketCard({ item, onCancel, busy = false, showCheckout = true }: { item: Purchase; onCancel?: (p: Purchase) => void; busy?: boolean; showCheckout?: boolean }) {
  const checkout = safeCheckoutPath(item.checkoutUrl);
  return <article className="reservation-card">
    <h2>{item.departureCity} → {item.arrivalCity}</h2>
    <p>{item.companyName} · Sefer #{item.tripId} · Koltuk {item.seatNumber}</p>
    <dl className="reservation-facts">
      {item.pnr && <div><dt>PNR / Bilet numarası</dt><dd className="reservation-number">{item.pnr}</dd></div>}
      <div><dt>Bilet durumu</dt><dd className={`status-text ${statusTone(item.pnr ? item.ticketStatus : item.paymentStatus)}`}>{item.pnr ? item.ticketStatus === "Active" ? "Aktif" : "İPTAL EDİLDİ" : item.ticketStatus === "Active" ? "Henüz düzenlenmedi" : "İşlem kapandı"}</dd></div>
      <div><dt>Kalkış (Türkiye saati)</dt><dd>{dateTime.format(new Date(item.departureTime))}</dd></div>
      <div><dt>Varış (Türkiye saati)</dt><dd>{dateTime.format(new Date(item.arrivalTime))}</dd></div>
      <div><dt>Bilet tutarı</dt><dd>{money.format(item.amount)}</dd></div>
      <div><dt>Ödeme durumu</dt><dd className={`status-text ${statusTone(item.paymentStatus)}`}>{paymentLabel(item.paymentStatus)}</dd></div>
      <div><dt>İade durumu</dt><dd className={`status-text ${statusTone(item.refundStatus)}`}>{item.refundStatus === "None" ? refundLabel(item.refundStatus) : refundMessage(item.refundStatus)}{item.refundStatus !== "None" && ` · ${money.format(item.refundAmount)}`}</dd></div>
      {item.pnr && <div><dt>Yolcu iptali son zamanı (Türkiye)</dt><dd>{dateTime.format(new Date(item.cancelDeadline))}</dd></div>}
      {!item.pnr && item.holdExpiresAt && <div><dt>Ödeme için koltuk tutma sonu</dt><dd>{dateTime.format(new Date(item.holdExpiresAt))}</dd></div>}
      {item.cancelledAt && <div><dt>İptal zamanı</dt><dd>{dateTime.format(new Date(item.cancelledAt))}</dd></div>}
    </dl>
    {item.pnr && item.ticketStatus === "Active" && !item.canCancel && <p>Kalkışa 2 saatten az kaldığı için yolcu iptali ve iade yapılamaz.</p>}
    {item.cancellationReason && <Details title="İptal nedeni"><p>{item.cancellationReason}</p></Details>}
    {item.lastError && <Details title="İşlem açıklaması"><p>{item.lastError}</p></Details>}
    {item.needsAttention && <p role="status">Bu işlem destek takibi gerektiriyor.</p>}
    <Link to={`/trips/${item.tripId}`} className="site-button secondary">Sefer detayı</Link>
    {isIssuedTicket(item) && <Link to={`/tickets/${item.pnr}`} className="site-button primary">Bilet detayını gör</Link>}
    {showCheckout && checkout && <Link to={checkout} className="site-button primary">Ödemeye devam et</Link>}
    {onCancel && item.canCancel && <button className="site-button reservation-cancel" disabled={busy} onClick={() => onCancel(item)}>Bileti İptal Et</button>}
  </article>;
}
