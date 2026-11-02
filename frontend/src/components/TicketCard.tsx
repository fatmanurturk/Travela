import { Link } from "react-router-dom";
import { dateTime, money } from "../reservations";
import { paymentLabel, refundLabel, safeCheckoutPath } from "../payments";
import type { Purchase } from "../payments";

export default function TicketCard({ item, onCancel, busy = false }: { item: Purchase; onCancel?: (p: Purchase) => void; busy?: boolean }) {
  const checkout = safeCheckoutPath(item.checkoutUrl);
  return <article className="reservation-card">
    {item.isTest && <p className="test-payment-badge">YEREL TEST — Gerçek tahsilat veya iade yoktur.</p>}
    <h2>{item.departureCity} → {item.arrivalCity}</h2>
    <p>{item.companyName} · Sefer #{item.tripId} · Koltuk {item.seatNumber}</p>
    <dl className="reservation-facts">
      <div><dt>PNR / Bilet numarası</dt><dd className="reservation-number">{item.pnr ?? "Bilet düzenlenmedi"}</dd></div>
      <div><dt>Bilet durumu</dt><dd>{item.pnr ? item.ticketStatus === "Active" ? "Aktif" : "İptal Edildi" : item.ticketStatus === "Active" ? "Ödeme işlemi sürüyor" : "İşlem kapandı"}</dd></div>
      <div><dt>Kalkış (Türkiye saati)</dt><dd>{dateTime.format(new Date(item.departureTime))}</dd></div>
      <div><dt>Sunucunun hesapladığı tutar</dt><dd>{money.format(item.amount)}</dd></div>
      <div><dt>{item.isTest ? "Test ödeme durumu" : "Ödeme durumu"}</dt><dd>{paymentLabel(item.paymentStatus)}</dd></div>
      <div><dt>{item.isTest ? "Test iade durumu" : "İade durumu"}</dt><dd>{refundLabel(item.refundStatus)}{item.refundStatus !== "None" && ` · ${money.format(item.refundAmount)}`}</dd></div>
      {item.pnr && <div><dt>Yolcu iptali son zamanı (Türkiye)</dt><dd>{dateTime.format(new Date(item.cancelDeadline))}</dd></div>}
      {!item.pnr && item.holdExpiresAt && <div><dt>Ödeme için koltuk tutma sonu</dt><dd>{dateTime.format(new Date(item.holdExpiresAt))}</dd></div>}
      {item.cancelledAt && <div><dt>İptal zamanı</dt><dd>{dateTime.format(new Date(item.cancelledAt))}</dd></div>}
    </dl>
    {item.pnr && item.ticketStatus === "Active" && !item.canCancel && <p>Kalkışa 2 saatten az kaldığı için yolcu iptali ve iade yapılamaz.</p>}
    {item.cancellationReason && <p>{item.cancellationReason}</p>}
    {item.refundStatus === "Pending" && <p>İade talebi kaydedildi; sağlayıcının sonucu bekleniyor. Bankaya yansıma süresi için bir tarih verilemiyor.</p>}
    {item.refundStatus === "Failed" && <p role="status">İade henüz tamamlanmadı. Güvenli yeniden deneme ve takip süreci devam ediyor.</p>}
    {item.lastError && <p role="status">{item.lastError}</p>}
    {item.needsAttention && <p role="status">Bu işlem destek takibi gerektiriyor.</p>}
    <Link to={`/trips/${item.tripId}`} className="site-button secondary">Sefer detayı</Link>
    {checkout && <Link to={checkout} className="site-button primary">Test ödeme adımına dön</Link>}
    {onCancel && item.canCancel && <button className="site-button reservation-cancel" disabled={busy} onClick={() => onCancel(item)}>Bileti İptal Et</button>}
  </article>;
}
