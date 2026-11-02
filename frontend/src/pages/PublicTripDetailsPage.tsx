import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";

import { currentRole, tripStatusLabel } from "../reservations";
import { safeCheckoutPath } from "../payments";

type PublicTrip = {
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
const CURRENT_YEAR = new Date().getFullYear();

function PublicTripDetailsPage() {
  const { id } = useParams();
  return <TripDetails key={id} id={id} />;
}

function TripDetails({ id }: { id: string | undefined }) {
  const [trip, setTrip] = useState<PublicTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [selectedSeat, setSelectedSeat] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [bookingError, setBookingError] = useState("");
  const requestKey = useRef(crypto.randomUUID());
  const [revision, setRevision] = useState(0);
  const navigate = useNavigate();
  const token = sessionStorage.getItem("travela_token");
  const role = currentRole();
  const loginPath = `/login?returnTo=${encodeURIComponent(`/trips/${id}`)}`;
  const refresh = () => {
    setLoading(true); setError(""); setSelectedSeat(null); setRevision(v => v + 1);
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

  const reserve = async () => {
    if (!token) { navigate(loginPath); return; }
    if (!trip || selectedSeat === null || saving) return;
    setSaving(true); setBookingError("");
    try {
      const response = await fetch(apiUrl("/api/tickets/checkout"), {
        method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
        body: JSON.stringify({ tripId: trip.id, seatNumber: selectedSeat, requestKey: requestKey.current }),
      });
      if (response.status === 401) {
        sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
        navigate(loginPath); return;
      }
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 409) refresh();
        throw new Error(response.status === 403 ? "Bilet satın almak için Passenger (yolcu) hesabıyla giriş yapınız." : data?.message ?? "Ödeme adımı açılamadı. Lütfen tekrar deneyiniz.");
      }
      const path = safeCheckoutPath(data.checkoutUrl);
      if (!path) throw new Error("Ödeme adresi alınamadı. Biletlerim sayfasını kontrol ediniz.");
      navigate(path);
    } catch (cause) {
      setBookingError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Yeniden denemeden önce Biletlerim sayfasını kontrol ediniz." : cause instanceof Error ? cause.message : "Ödeme sonucu alınamadı. Yeniden denemeden önce Biletlerim sayfasını kontrol ediniz.");
    } finally { setSaving(false); }
  };

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
              <p className="booking-notice">Ödeme doğrulanmadan bilet düzenlenmez. Geliştirme ortamındaki işlemler yalnızca testtir.</p>
              <Link to="/#sefer-ara" className="site-button secondary">Başka sefer ara</Link>
            </section>
            <section className="seat-booking" aria-labelledby="seat-heading">
              <div>
                <h2 id="seat-heading">Koltuğunuzu seçin</h2>
                <p>{trip.seatCapacity} koltuk · Sefer durumu: {tripStatusLabel(trip.status)}</p>
                <p className="seat-legend"><span>□ Boş</span><span>▧ Dolu</span><span>✓ Seçili</span></p>
                <p className="booking-notice">Numaralı plan kapasiteye göre hazırlanmıştır; otobüsün fiziksel yerleşimini temsil etmez.</p>
                {!trip.canReserve && <p role="status">{trip.departureHasPassed ? "Bu seferin kalkış zamanı geçtiği için bilet alınamaz." : tripStatusLabel(trip.status) === "İptal edildi" ? "Bu sefer iptal edildiği için bilet alınamaz." : !trip.seatPlanAvailable ? "Bu seferin eski kontenjan bilgileri koltuk kayıtlarıyla eşleşmiyor. Firma doğrulaması bekleniyor." : trip.availableSeats === 0 ? "Bu seferde boş koltuk kalmadı." : "Bu sefer bilet satışına kapalı."}</p>}
                {trip.seatPlanAvailable && <div className="seat-plan" role="group" aria-label="Numaralı koltuk planı">
                  <div className="bus-front" aria-hidden="true">Otobüsün önü ↑</div>
                  {Array.from({ length: trip.seatCapacity }, (_, index) => index + 1).map(seat => {
                    const occupied = trip.occupiedSeats.includes(seat);
                    const selected = selectedSeat === seat;
                    return <button key={seat} type="button" className={`seat ${occupied ? "occupied" : selected ? "selected" : "available"}`}
                      aria-label={`Koltuk ${seat}, ${occupied ? "dolu" : selected ? "seçili" : "boş"}`} aria-pressed={selected}
                      disabled={occupied || !trip.canReserve || saving}
                      onClick={() => { setSelectedSeat(selected ? null : seat); requestKey.current = crypto.randomUUID(); setBookingError(""); }}>
                      {seat}<small>{occupied ? "Dolu" : selected ? "✓" : "Boş"}</small>
                    </button>;
                  })}
                </div>}
                <button type="button" className="site-button secondary" disabled={saving} onClick={refresh}>Koltuk planını yenile</button>
              </div>
              <aside className="booking-summary" aria-live="polite">
                <h2>Sipariş özeti</h2>
                {selectedSeat !== null ? <>
                  <p>{trip.companyName} · Sefer #{trip.id}</p>
                  <p>{trip.departureCity} → {trip.arrivalCity}</p>
                  <p>{dateTimeFormatter.format(new Date(trip.departureTime))}</p>
                  <p><strong>Koltuk {selectedSeat}</strong></p>
                  <p>Bilet fiyatı: <strong>{currencyFormatter.format(trip.price)}</strong></p>
                </> : <p>Devam etmek için boş bir koltuk seçiniz.</p>}
                <p className="booking-notice">Ödeme doğrulanmadan bilet düzenlenmez. Geliştirme ortamındaki işlemler yalnızca testtir. Seçim yapmak koltuğu ayırmaz. Ödeme adımında en fazla 10 dakikalık dahili tutma uygulanır.</p>
                {token && role !== "Passenger" ? <p role="status">Bilet satın almak için Passenger (yolcu) hesabıyla giriş yapmalısınız.</p> :
                  <button type="button" className="site-button primary" disabled={selectedSeat === null || !trip.canReserve || saving}
                    onClick={() => void reserve()}>{saving ? "Ödeme adımı hazırlanıyor..." : token ? "Test ödeme adımına geç" : "Giriş yap ve devam et"}</button>}
              </aside>
            </section>
          </>
        ) : null}
      </main>
      <footer className="site-footer"><Link to="/" className="site-logo">Travela<span>.</span></Link><small>© {CURRENT_YEAR} Travela</small></footer>
    </div>
  );
}

export default PublicTripDetailsPage;
