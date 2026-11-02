import { Link } from "react-router-dom";
import { dateTime, money, reservationNotice, reservationStatusLabel, tripStatusLabel } from "../reservations";
import type { Reservation } from "../reservations";

export default function ReservationSummary({ reservation, onCancel, cancelling = false }: { reservation: Reservation; onCancel?: (reservation: Reservation) => void; cancelling?: boolean }) {
  return <article className="reservation-card">
    <h2>{reservation.departureCity} → {reservation.arrivalCity}</h2>
    <p>{reservation.companyName} · Sefer #{reservation.tripId}</p>
    <dl className="reservation-facts">
      <div><dt>Rezervasyon numarası</dt><dd className="reservation-number">{reservation.reservationNumber}</dd></div>
      <div><dt>Rezervasyon durumu</dt><dd className={`reservation-state ${reservation.status === "Cancelled" ? "cancelled" : "active"}`}>{reservationStatusLabel(reservation.status)}</dd></div>
      <div><dt>Koltuk</dt><dd>{reservation.seatNumber}</dd></div>
      <div><dt>Kalkış</dt><dd>{dateTime.format(new Date(reservation.departureTime))}</dd></div>
      <div><dt>Varış</dt><dd>{dateTime.format(new Date(reservation.arrivalTime))}</dd></div>
      <div><dt>Rezervasyon fiyatı</dt><dd>{money.format(reservation.price)}</dd></div>
      <div><dt>Sefer durumu</dt><dd>{tripStatusLabel(reservation.tripStatus)}</dd></div>
      <div><dt>Oluşturulma</dt><dd>{dateTime.format(new Date(reservation.createdAt))}</dd></div>
      <div><dt>Ödeme durumu</dt><dd>Ödeme alınmadı</dd></div>
      {reservation.cancelledAt && <div><dt>İptal zamanı</dt><dd>{dateTime.format(new Date(reservation.cancelledAt))}</dd></div>}
      {reservation.cancellationReason && <div><dt>İptal nedeni</dt><dd>{reservation.cancellationReason}</dd></div>}
    </dl>
    <p className="booking-notice">{reservationNotice}</p>
    <Link to={`/trips/${reservation.tripId}`} className="site-button secondary">Sefer detayını gör</Link>
    {onCancel && reservation.status === "Active" && reservation.canCancel && <button type="button" className="site-button reservation-cancel" disabled={cancelling} onClick={() => onCancel(reservation)}>{cancelling ? "İptal ediliyor..." : "Rezervasyonu İptal Et"}</button>}
  </article>;
}
