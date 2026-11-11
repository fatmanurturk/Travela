import { useEffect, useRef, useState } from "react";
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
  const [pendingSeat, setPendingSeat] = useState<number | null>(null);
  const genderDialog = useRef<HTMLDialogElement>(null);
  const removeSeat = (seat: number) => {
    setSelectedSeats(current => current.filter(number => number !== seat));
    setGenders(current => {
      const next = { ...current };
      delete next[seat];
      return next;
    });
  };
  const seatErrors = Object.fromEntries(selectedSeats.map(seat => {
    const occupied = trip?.occupiedSeats.includes(seat) || trip?.seatOccupancies?.some(item => item.seatNumber === seat);
    return [seat, occupied
      ? "Koltuk başka biri tarafından alınmış veya geçici olarak rezerve edilmiş. Koltuğu çıkarınız."
      : seatConflict(trip?.seats ?? [], seat, genders[seat], trip?.seatOccupancies ?? [], genders)];
  }));
  const [saving, setSaving] = useState(false);
  const [bookingError, setBookingError] = useState("");
  const [revision, setRevision] = useState(0);
  const genderOptions = ([['Female', 'Kadın', '♀'], ['Male', 'Erkek', '♂']] as const).map(([value, label, icon]) => ({
    value, label, icon,
    conflict: pendingSeat === null ? "" : seatConflict(
      trip?.seats ?? [], pendingSeat, value, trip?.seatOccupancies ?? [], genders
    ),
  }));
  const closeGenderDialog = () => {
    genderDialog.current?.close();
    setPendingSeat(null);
  };
  const cancelGenderSelection = () => {
    if (pendingSeat !== null) removeSeat(pendingSeat);
    closeGenderDialog();
  };
  const openGenderDialog = (seatNumber: number) => {
    if (!trip?.canReserve || saving) return;
    const seat = trip.seats?.find(item => item.number === seatNumber);
    if (!seat || seat.isBlocked || trip.occupiedSeats.includes(seatNumber) ||
      trip.seatOccupancies?.some(item => item.seatNumber === seatNumber)) return;
    if (!selectedSeats.includes(seatNumber) && selectedSeats.length >= 10) {
      setBookingError("En fazla 10 koltuk seçebilirsiniz.");
      return;
    }
    setBookingError("");
    setPendingSeat(seatNumber);
  };
  const chooseGender = (gender: Gender) => {
    if (pendingSeat === null || !trip?.canReserve || saving) return;
    const seat = trip.seats?.find(item => item.number === pendingSeat);
    if (!seat || seat.isBlocked || trip.occupiedSeats.includes(pendingSeat) ||
      trip.seatOccupancies?.some(item => item.seatNumber === pendingSeat)) {
      setBookingError("Koltuk artık seçilemiyor. Lütfen koltuk planını yenileyiniz.");
      cancelGenderSelection();
      return;
    }
    const conflict = seatConflict(trip.seats ?? [], pendingSeat, gender, trip.seatOccupancies ?? [], genders);
    if (conflict) return;
    if (!selectedSeats.includes(pendingSeat) && selectedSeats.length >= 10) {
      setBookingError("En fazla 10 koltuk seçebilirsiniz.");
      closeGenderDialog();
      return;
    }
    setGenders(current => ({ ...current, [pendingSeat]: gender }));
    setSelectedSeats(current => current.includes(pendingSeat) ? current : [...current, pendingSeat]);
    setBookingError("");
    closeGenderDialog();
  };
  const refresh = () => {
    closeGenderDialog();
    setLoading(true); setError(""); setSelectedSeats([]); setGenders({}); setRevision(v => v + 1);
  };

  useEffect(() => {
    const dialog = genderDialog.current;
    if (pendingSeat === null || !dialog) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    dialog.showModal();
    const currentChoice = dialog.querySelector<HTMLButtonElement>('[data-gender][aria-pressed="true"]:not(:disabled)');
    const firstChoice = dialog.querySelector<HTMLButtonElement>('[data-gender]:not(:disabled)');
    (currentChoice ?? firstChoice)?.focus();
    return () => {
      if (dialog.open) dialog.close();
      document.body.style.overflow = previousOverflow;
    };
  }, [pendingSeat]);

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
                <p>Boş bir koltuğa tıklayıp yolcu cinsiyetini seçin. Değiştirmek veya koltuğu bırakmak için
                  seçili koltuğa tekrar tıklayın. Yan yana eş koltuklar aynı cinsiyette olmalıdır.</p>
                {!trip.canReserve && <p role="status">{trip.departureHasPassed ? "Bu seferin kalkış zamanı geçtiği için bilet alınamaz." : tripStatusLabel(trip.status) === "İptal edildi" ? "Bu sefer iptal edildiği için bilet alınamaz." : !trip.seatPlanAvailable ? "Koltuk bilgileri kontrol ediliyor. Lütfen daha sonra deneyin." : trip.availableSeats === 0 ? "Bu seferde boş koltuk kalmadı." : "Bu sefer bilet satışına kapalı."}</p>}
                {!trip.seatPlanAvailable && <p role="status">Koltuk planı bulunamıyor veya doğrulanmamış. Firma ya da yönetici fiziksel koltuk planını tanımlamalıdır.</p>}
                {trip.seatPlanAvailable && <SeatMap
                  seats={trip.seats ?? []} layout={trip.seatLayout ?? "Unspecified"}
                  occupied={trip.occupiedSeats} occupancies={trip.seatOccupancies} genders={genders}
                  sun={trip.sunExposure} onIncompatible={setBookingError} selected={selectedSeats}
                  disabled={!trip.canReserve || saving} onChoose={openGenderDialog}
                />}
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
                <PassengerCheckoutForm
                  tripId={trip.id} seats={selectedSeats} genders={genders} seatErrors={seatErrors}
                  disabled={!trip.canReserve || saving} onBusy={setSaving} onError={setBookingError}
                  onConflict={() => setRevision(value => value + 1)} onRemove={removeSeat}
                />
              </aside>
            </section>
          </>
        ) : null}
      </main>
      <dialog
        ref={genderDialog} className="reservation-confirm seat-gender-dialog"
        aria-labelledby="seat-gender-heading" aria-describedby="seat-gender-description" aria-modal="true"
        onCancel={event => { event.preventDefault(); cancelGenderSelection(); }}
        onKeyDown={event => {
          if (event.key !== "Tab") return;
          const buttons = event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
          const first = buttons[0];
          const last = buttons[buttons.length - 1];
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
          }
        }}
        onClick={event => {
          if (event.target !== event.currentTarget) return;
          const bounds = event.currentTarget.getBoundingClientRect();
          if (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom) cancelGenderSelection();
        }}
      >
        {pendingSeat !== null && <>
          <div className="seat-gender-header">
            <span className="seat-gender-number" aria-hidden="true">{pendingSeat}</span>
            <button type="button" className="seat-gender-close" aria-label="Cinsiyet seçimini iptal et"
              onClick={cancelGenderSelection}>×</button>
          </div>
          <h2 id="seat-gender-heading">Koltuk {pendingSeat} – Yolcu Cinsiyeti</h2>
          <p id="seat-gender-description">Bu koltukta seyahat edecek yolcunun cinsiyetini seçiniz.</p>
          <div className="seat-gender-options">
            {genderOptions.map(option => <button
              key={option.value} type="button" data-gender={option.value}
              className={"seat-gender-option " + option.value.toLowerCase()}
              aria-label={option.label} aria-pressed={genders[pendingSeat] === option.value}
              aria-describedby={option.conflict ? "seat-gender-restriction" : undefined}
              disabled={!!option.conflict || saving || !trip?.canReserve}
              onClick={() => chooseGender(option.value)}
            >
              <span aria-hidden="true">{option.icon}</span>
              <strong>{option.label}</strong>
              {genders[pendingSeat] === option.value && <small aria-hidden="true">✓ Mevcut seçim</small>}
            </button>)}
          </div>
          {genderOptions.some(option => option.conflict) && <p id="seat-gender-restriction"
            className="seat-gender-restriction">{genderOptions.find(option => option.conflict)?.conflict}</p>}
          {selectedSeats.includes(pendingSeat) && <p className="seat-gender-cancel-note">
            İptal ederseniz bu koltuk ve geçici yolcu bilgileri kaldırılır.
          </p>}
          <button type="button" className="site-button secondary seat-gender-cancel" onClick={cancelGenderSelection}>
            {selectedSeats.includes(pendingSeat) ? "Koltuğu bırak" : "Vazgeç"}
          </button>
        </>}
      </dialog>
    </div>
  );
}

export default PublicTripDetailsPage;
