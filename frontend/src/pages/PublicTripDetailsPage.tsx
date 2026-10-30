import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";

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
  const [trip, setTrip] = useState<PublicTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    const loadTrip = async () => {
      try {
        const response = await fetch(apiUrl(`/api/trips/public/${id}`), { signal: controller.signal });
        if (!response.ok) {
          if (response.status === 404) throw new Error("Sefer bulunamadı veya artık satışta değil.");
          throw new Error("Sefer bilgileri şu anda alınamıyor.");
        }
        setTrip(await response.json() as PublicTrip);
      } catch (cause) {
        if (cause instanceof DOMException && cause.name === "AbortError") return;
        setError(cause instanceof Error ? cause.message : "Sefer bilgileri alınamadı.");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    };
    void loadTrip();
    return () => controller.abort();
  }, [id]);

  return (
    <div className="travela-site">
      <SiteHeader />
      <main className="trip-detail-page">
        <Link to="/" className="back-home">← Ana sayfaya dön</Link>
        {loading ? <div className="site-empty-state">Sefer bilgileri yükleniyor...</div> : error ? (
          <div className="results-message error" role="alert"><p>{error}</p><Link to="/search?all=true" className="site-button primary">Diğer seferleri gör</Link></div>
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
              <p className="booking-notice">Bu sefer için fiyat ve koltuk bilgileri mevcut. Koltuk seçimi ve bilet rezervasyonu henüz platformda sunulmuyor.</p>
              <Link to="/#sefer-ara" className="site-button secondary">Başka sefer ara</Link>
            </section>
          </>
        ) : null}
      </main>
      <footer className="site-footer"><Link to="/" className="site-logo">Travela<span>.</span></Link><small>© {CURRENT_YEAR} Travela</small></footer>
    </div>
  );
}

export default PublicTripDetailsPage;
