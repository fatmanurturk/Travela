import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import SeatMap from "../components/SeatMap";
import JourneyStops from "../components/JourneyStops";
import { buildSeats } from "../seatPlans";
import type { Journey, SeatPosition } from "../seatPlans";

import { tripStatusLabel } from "../reservations";
import PassengerCheckoutForm from "../components/PassengerCheckoutForm";

type PublicTrip = Journey & {
  id: number;
  companyName: string;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  durationMinutes: number;
  price: number;
  availableSeats: number;
  seatCapacity: number;
  seatLayout?: string;
  seats?: SeatPosition[];
  occupiedSeats: number[];
  canReserve: boolean;
  seatPlanAvailable: boolean;
  departureHasPassed: boolean;
  status: string;
};

const currencyFormatter = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const dateTimeFormatter = new Intl.DateTimeFormat("tr-TR", {
  weekday: "long",
  day: "numeric",
  month: "long",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "Europe/Istanbul",
});

function PublicTripDetailsPage() {
  const { id } = useParams();
  return <TripDetails key={id} id={id} />;
}

function TripDetails({ id }: { id: string | undefined }) {
  const [trip, setTrip] = useState<PublicTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedSeats, setSelectedSeats] = useState<number[]>([]);
  const [saving, setSaving] = useState(false);
  const [bookingError, setBookingError] = useState("");
  const [revision, setRevision] = useState(0);
  const refresh = () => {
    setLoading(true); setError(""); setSelectedSeats([]); setRevision(v => v + 1);
  };

  useEffect(() => {
    const controller = new AbortController();
    const loadTrip = async () => {
      try {
        const response = await fetch(apiUrl(`/api/trips/public/${id}`), { signal: controller.signal });
        if (!response.ok) {
          if (response.status === 404) throw new Error("Sefer bulunamadı.");
          throw new Error("Sefer bilgileri şu anda alınamıyor.");
        }
        const details = await response.json() as PublicTrip;
        // Older running API builds do not yet return the server-computed time flag.
        details.departureHasPassed ??= new Date(details.departureTime).getTime() <= Date.now();
        setTrip(details);
      } catch (cause) {
        if (cause instanceof DOMException && cause.name === "AbortError") return;
        setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Lütfen tekrar deneyiniz." : cause instanceof Error ? cause.message : "Sefer bilgileri alınamadı.");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    };
    void loadTrip();
    return () => controller.abort();
  }, [id, revision]);

  return (
    <div className="travela-site">
      <SiteHeader />
      <main className="trip-detail-page">
        <Link to="/" className="back-home">← Ana sayfaya dön</Link>
        {bookingError && <div className="results-message error" role="alert">{bookingError} <Link to="/tickets">Biletlerimi kontrol et</Link></div>}
        {loading ? <div className="site-empty-state">Sefer bilgileri yükleniyor...</div> : error ? (
          <div className="results-message error" role="alert"><p>{error}</p><button type="button" className="site-button secondary" onClick={refresh}>Tekrar dene</button> <Link to="/search?all=true" className="site-button primary">Diğer seferleri gör</Link></div>
        ) : trip ? (
          <>
            <span className="eyebrow">{trip.companyName}</span>
            <h1>{trip.departureCity} <span aria-hidden="true">→</span> {trip.arrivalCity}</h1>
            <section className="trip-detail-card">
              <div className="trip-detail-stops">
                <div><span>Kalkış</span><strong>{dateTimeFormatter.format(new Date(trip.departureTime))}</strong><b>{trip.departureCity}</b></div>
                <span className="detail-arrow" aria-hidden="true">→</span>
                <div><span>Varış</span><strong>{dateTimeFormatter.format(new Date(trip.arrivalTime))}</strong><b>{trip.arrivalCity}</b></div>
              </div>
              <div className="trip-detail-facts"><span>{Math.floor(trip.durationMinutes / 60)} sa {trip.durationMinutes % 60} dk yolculuk</span><span>{trip.availableSeats} boş koltuk</span></div>
              <div className="trip-detail-price"><span>Kişi başı</span><strong>{currencyFormatter.format(trip.price)}</strong></div>

              <Link to="/#sefer-ara" className="site-button secondary">Başka sefer ara</Link>
              <Link to={`/campaigns?tripId=${trip.id}`} className="site-button secondary">Bu seferin kampanyaları</Link>
            </section>
            <JourneyStops journey={trip} />
            <section className="seat-booking" aria-labelledby="seat-heading">
              <div>
                <h2 id="seat-heading">Koltuğunuzu seçin</h2>
                <p>{trip.seatCapacity} koltuk · Sefer durumu: {tripStatusLabel(trip.status)}</p>
                <p className="seat-legend"><span>□ Boş</span><span>▧ Dolu</span><span>✓ Seçili</span></p>
                {!trip.canReserve && <p role="status">{trip.departureHasPassed ? "Bu seferin kalkış zamanı geçtiği için bilet alınamaz." : tripStatusLabel(trip.status) === "İptal edildi" ? "Bu sefer iptal edildiği için bilet alınamaz." : !trip.seatPlanAvailable ? "Koltuk bilgileri kontrol ediliyor. Lütfen daha sonra deneyin." : trip.availableSeats === 0 ? "Bu seferde boş koltuk kalmadı." : "Bu sefer bilet satışına kapalı."}</p>}
                {trip.seatPlanAvailable && <SeatMap seats={trip.seats ?? buildSeats("Unspecified", trip.seatCapacity)} layout={trip.seatLayout ?? "Unspecified"} occupied={trip.occupiedSeats} selected={selectedSeats} disabled={!trip.canReserve || saving}
                  onChoose={seat => { setSelectedSeats(current => current.includes(seat) ? current.filter(number => number !== seat) : current.length < 10 ? [...current, seat] : current); setBookingError(""); }} />}
                <button type="button" className="site-button secondary" disabled={saving} onClick={refresh}>Koltuk planını yenile</button>
              </div>
              <aside className="booking-summary" aria-live="polite">
                <h2>Sipariş özeti</h2>
                {selectedSeats.length > 0 ? <>
                  <p>{trip.companyName} · Sefer #{trip.id}</p>
                  <p>{trip.departureCity} → {trip.arrivalCity}</p>
                  <p>{dateTimeFormatter.format(new Date(trip.departureTime))}</p>
                  <p><strong>Koltuklar: {selectedSeats.join(", ")}</strong></p>
                  <p>İndirim öncesi: <strong>{currencyFormatter.format(trip.price * selectedSeats.length)}</strong></p>
                </> : <p>Devam etmek için boş bir koltuk seçiniz.</p>}
                <p className="booking-notice">Biletiniz ödeme tamamlandığında oluşur. Koltuğunuz ödeme adımında 10 dakika ayrılır.</p>
                <PassengerCheckoutForm tripId={trip.id} seats={selectedSeats} disabled={!trip.canReserve || saving} onBusy={setSaving} onError={setBookingError} onConflict={() => setRevision(value => value + 1)} onRemove={seat => setSelectedSeats(current => current.filter(number => number !== seat))} />
              </aside>
            </section>
          </>
        ) : null}
      </main>
    </div>
  );
}

export default PublicTripDetailsPage;
