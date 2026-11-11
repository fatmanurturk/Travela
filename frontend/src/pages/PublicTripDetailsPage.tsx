import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import SeatMap from "../components/SeatMap";
import JourneyStops from "../components/JourneyStops";
import type { Journey, SeatPosition } from "../seatPlans";
import { seatConflict } from "../seatCompatibility";
import type { Gender, SeatOccupancy, SunExposure } from "../seatCompatibility";

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
  seatOccupancies: SeatOccupancy[];
  sunExposure: SunExposure;
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
  const [genders, setGenders] = useState<Record<number, Gender | "">>({});
  const [selectionGender, setSelectionGender] = useState<Gender | "">("");
  const removeSeat = (seat: number) => { setSelectedSeats(current => current.filter(number => number !== seat)); setGenders(current => { const next = { ...current }; delete next[seat]; return next; }); };
  const seatErrors = Object.fromEntries(selectedSeats.map(seat => [seat, trip?.occupiedSeats.includes(seat) ? "Koltuk başka biri tarafından alınmış veya geçici olarak rezerve edilmiş. Koltuğu çıkarınız." : seatConflict(trip?.seats ?? [], seat, genders[seat], trip?.seatOccupancies ?? [], genders)]));
  const [saving, setSaving] = useState(false);
  const [bookingError, setBookingError] = useState("");
  const [revision, setRevision] = useState(0);
  const refresh = () => {
    setLoading(true); setError(""); setSelectedSeats([]); setGenders({}); setRevision(v => v + 1);
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
      <main className="trip-detail-page public-trip-detail-page">
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
                <label>Yeni seçilecek yolcunun cinsiyeti <select aria-label="Yeni yolcunun cinsiyeti" value={selectionGender} disabled={saving} onChange={event => setSelectionGender(event.target.value as Gender | "")}><option value="">Yolcu formunda seçeceğim</option><option value="Female">Kadın</option><option value="Male">Erkek</option></select></label>
                <p>Her koltuğun yolcu formunda cinsiyeti ayrı ayrı seçebilirsiniz. Yan yana eş koltuklar aynı cinsiyette olmalıdır.</p>
                {!trip.canReserve && <p role="status">{trip.departureHasPassed ? "Bu seferin kalkış zamanı geçtiği için bilet alınamaz." : tripStatusLabel(trip.status) === "İptal edildi" ? "Bu sefer iptal edildiği için bilet alınamaz." : !trip.seatPlanAvailable ? "Koltuk bilgileri kontrol ediliyor. Lütfen daha sonra deneyin." : trip.availableSeats === 0 ? "Bu seferde boş koltuk kalmadı." : "Bu sefer bilet satışına kapalı."}</p>}
                {!trip.seatPlanAvailable && <p role="status">Koltuk planı bulunamıyor veya doğrulanmamış. Firma ya da yönetici fiziksel koltuk planını tanımlamalıdır.</p>}
                {trip.seatPlanAvailable && <SeatMap seats={trip.seats ?? []} layout={trip.seatLayout ?? "Unspecified"} occupied={trip.occupiedSeats} occupancies={trip.seatOccupancies} genders={genders} selectionGender={selectionGender} sun={trip.sunExposure} onIncompatible={setBookingError} selected={selectedSeats} disabled={!trip.canReserve || saving}
                  onChoose={seat => { if (selectedSeats.includes(seat)) removeSeat(seat); else if (selectedSeats.length < 10) { setSelectedSeats(current => [...current, seat]); setGenders(current => ({ ...current, [seat]: selectionGender })); } else { setBookingError("En fazla 10 koltuk seçebilirsiniz."); return; } setBookingError(selectionGender ? "" : "Uygunluk kontrolü için her yolcunun cinsiyetini seçiniz."); }} />}
                {!trip.seatPlanAvailable && <p>{trip.sunExposure?.message ?? "Güneş yönü bilgisi hesaplanamadı."}</p>}
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
                <PassengerCheckoutForm tripId={trip.id} seats={selectedSeats} genders={genders} seatErrors={seatErrors} onGender={(seat, gender) => setGenders(current => ({ ...current, [seat]: gender }))} disabled={!trip.canReserve || saving} onBusy={setSaving} onError={setBookingError} onConflict={() => setRevision(value => value + 1)} onRemove={removeSeat} />
              </aside>
            </section>
          </>
        ) : null}
      </main>
    </div>
  );
}

export default PublicTripDetailsPage;
