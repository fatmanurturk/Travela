import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { getTodayInTurkey } from "../dateUtils";
import SiteHeader from "../components/SiteHeader";
import TripSearchForm from "../components/TripSearchForm";

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

type FeaturedRoute = {
  routeId: number;
  departureCity: string;
  arrivalCity: string;
  upcomingTripCount: number;
  startingPrice: number;
};

const dateTimeFormatter = new Intl.DateTimeFormat("tr-TR", {
  weekday: "short",
  day: "numeric",
  month: "long",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "Europe/Istanbul",
});
const currencyFormatter = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const CURRENT_YEAR = new Date().getFullYear();

function HomePage() {
  const navigate = useNavigate();
  const [upcomingTrips, setUpcomingTrips] = useState<PublicTrip[]>([]);
  const [featuredRoutes, setFeaturedRoutes] = useState<FeaturedRoute[]>([]);
  const [loading, setLoading] = useState(true);
  const [tripsError, setTripsError] = useState("");
  const [routesError, setRoutesError] = useState("");
  const [selectedRoute, setSelectedRoute] = useState<FeaturedRoute | null>(null);
  const [routeSelectionVersion, setRouteSelectionVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const loadHomeData = async () => {
      const [tripResult, routeResult] = await Promise.allSettled([
        fetch(apiUrl("/api/trips/upcoming?count=6"), { signal: controller.signal }),
        fetch(apiUrl("/api/trips/featured-routes?count=6"), { signal: controller.signal }),
      ]);

      if (controller.signal.aborted) return;
      if (tripResult.status === "fulfilled") {
        try {
          if (!tripResult.value.ok) throw new Error("Yaklaşan seferler yüklenemedi.");
          const data = (await tripResult.value.json()) as PublicTrip[];
          if (!Array.isArray(data)) throw new Error("Sefer verileri beklenen biçimde alınamadı.");
          setUpcomingTrips(data);
        } catch (error) {
          setTripsError(error instanceof Error ? error.message : "Yaklaşan seferler yüklenemedi.");
        }
      } else {
        setTripsError("Yaklaşan seferler şu anda yüklenemiyor.");
      }

      if (routeResult.status === "fulfilled") {
        try {
          if (!routeResult.value.ok) throw new Error("Öne çıkan güzergâhlar yüklenemedi.");
          const data = (await routeResult.value.json()) as FeaturedRoute[];
          if (!Array.isArray(data)) throw new Error("Güzergâh verileri beklenen biçimde alınamadı.");
          setFeaturedRoutes(data);
        } catch (error) {
          setRoutesError(error instanceof Error ? error.message : "Öne çıkan güzergâhlar yüklenemedi.");
        }
      } else {
        setRoutesError("Öne çıkan güzergâhlar şu anda yüklenemiyor.");
      }

      setLoading(false);
    };

    void loadHomeData();
    return () => controller.abort();
  }, []);

  const showAllTrips = () => {
    navigate(`/search?${new URLSearchParams({
      from: "",
      to: "",
      date: getTodayInTurkey(),
      passengers: "1",
      tripType: "one-way",
      all: "true",
    })}`);
  };

  return (
    <div className="travela-site">
      <SiteHeader />
      <main>
        <section className="home-hero">
          <div className="hero-copy">
            <span className="eyebrow">Yolculuğunu kolayca planla</span>
            <h1>Yeni yollar, <span>güzel hikâyeler.</span></h1>
            <p>Türkiye'nin farklı şehirleri arasındaki seferleri karşılaştır, sana uygun yolculuğu bul.</p>
          </div>
          <div className="search-panel" id="sefer-ara">
            <div className="search-panel-heading">
              <div>
                <span className="eyebrow">Planını oluştur</span>
                <h2>Otobüs bileti ara</h2>
              </div>
              <span className="search-panel-icon" aria-hidden="true">⌕</span>
            </div>
            <TripSearchForm
              key={`${selectedRoute?.routeId ?? "home-search"}-${routeSelectionVersion}`}
              initialDeparture={selectedRoute?.departureCity}
              initialArrival={selectedRoute?.arrivalCity}
            />
          </div>
          <a className="hero-scroll-link" href="#yaklasan-seferler">Yaklaşan seferleri keşfet <span aria-hidden="true">↓</span></a>
        </section>

        <section className="site-section" id="yaklasan-seferler">
          <div className="section-heading">
            <div>
              <span className="eyebrow">Yolculuğunu planla</span>
              <h2>Yaklaşan seferler</h2>
              <p>Kalkış zamanı en yakın olan seferlere göz at.</p>
            </div>
            <button type="button" className="text-link-button" onClick={showAllTrips}>Tüm seferleri gör <span aria-hidden="true">→</span></button>
          </div>
          {loading ? (
            <p className="site-status">Seferler yükleniyor...</p>
          ) : tripsError ? (
            <p className="site-status error" role="alert">{tripsError}</p>
          ) : upcomingTrips.length === 0 ? (
            <div className="site-empty-state">Henüz listelenecek sefer bulunmuyor.</div>
          ) : (
            <div className="upcoming-grid">
              {upcomingTrips.map((trip) => (
                <article className="upcoming-card" key={trip.id}>
                  <div className="upcoming-card-top"><span className="company-tag">{trip.companyName}</span><span className="seat-note">{trip.availableSeats} boş koltuk</span></div>
                  <h3>{trip.departureCity}<span aria-hidden="true"> → </span>{trip.arrivalCity}</h3>
                  <p className="trip-date">{dateTimeFormatter.format(new Date(trip.departureTime))}</p>
                  <div className="upcoming-card-bottom">
                    <strong>{currencyFormatter.format(trip.price)}<small> / kişi</small></strong>
                    <button type="button" onClick={() => navigate(`/trips/${trip.id}`)}>Seferi incele</button>
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>

        <section className="site-section route-section">
          <div className="section-heading">
            <div>
              <span className="eyebrow">Gerçek sefer verilerine göre</span>
              <h2>Öne çıkan güzergâhlar</h2>
              <p>Yaklaşan seferi bulunan güzergâhları ve başlangıç fiyatlarını incele.</p>
            </div>
          </div>
          {routesError ? (
            <p className="site-status error" role="alert">{routesError}</p>
          ) : loading && featuredRoutes.length === 0 ? (
            <p className="site-status">Güzergâhlar yükleniyor...</p>
          ) : featuredRoutes.length === 0 ? (
            <div className="site-empty-state">Şu anda öne çıkarılabilecek güzergâh bulunmuyor.</div>
          ) : (
            <div className="route-grid">
              {featuredRoutes.map((route) => (
                <button
                  type="button"
                  className={`route-card${selectedRoute?.routeId === route.routeId ? " active" : ""}`}
                  key={route.routeId}
                  onClick={() => {
                    setSelectedRoute(route);
                    setRouteSelectionVersion((version) => version + 1);
                    document.getElementById("sefer-ara")?.scrollIntoView({ behavior: "smooth" });
                  }}
                >
                  <span className="route-card-icon" aria-hidden="true">↗</span>
                  <span className="route-cities">{route.departureCity}<span aria-hidden="true"> → </span>{route.arrivalCity}</span>
                  <span className="route-card-meta">{route.upcomingTripCount} yaklaşan sefer</span>
                  <strong>{currencyFormatter.format(route.startingPrice)}<small>'den başlayan</small></strong>
                </button>
              ))}
            </div>
          )}
        </section>

        <section className="site-section how-section">
          <div className="section-heading">
            <div><span className="eyebrow">Üç adımda yola çık</span><h2>Nasıl çalışır?</h2></div>
          </div>
          <div className="how-grid">
            <article><span>01</span><h3>Seferini ara</h3><p>Kalkış ve varış şehirlerini, yolculuk tarihini ve kişi sayısını seç.</p></article>
            <article><span>02</span><h3>Seçenekleri karşılaştır</h3><p>Uygun saatleri, firmaları, yolculuk sürelerini ve fiyatları incele.</p></article>
            <article><span>03</span><h3>Yolculuğunu planla</h3><p>Sana uygun seferi seçip yolculuk bilgilerini gözden geçir.</p></article>
          </div>
        </section>

        <section className="site-section faq-section">
          <div className="section-heading">
            <div><span className="eyebrow">Yardım merkezi</span><h2>Sıkça sorulan sorular</h2></div>
          </div>
          <div className="faq-list">
            <details><summary>Sefer aramak için üye olmam gerekir mi?</summary><p>Hayır. Seferleri giriş yapmadan arayabilir ve karşılaştırabilirsiniz.</p></details>
            <details><summary>Arama sonuçları hangi bilgilere göre listeleniyor?</summary><p>Sonuçlar aktif firma, otobüs ve güzergâhlara bağlı, kalkış zamanı gelmemiş ve yeterli boş koltuğu bulunan seferlerden oluşturulur.</p></details>
            <details><summary>Gidiş-dönüş seferlerini birlikte arayabilir miyim?</summary><p>Evet. Arama formunda gidiş-dönüş seçip iki tarih belirleyebilirsiniz. Gidiş ve dönüş seferleri ayrı listelenir.</p></details>
          </div>
        </section>
      </main>
      <footer className="site-footer">
        <div><Link to="/" className="site-logo">Travela<span>.</span></Link><p>Yolculuğunu planlamanın sade yolu.</p></div>
        <div className="footer-links"><Link to="/">Ana Sayfa</Link><Link to="/#sefer-ara">Sefer Ara</Link><Link to="/login">Giriş Yap</Link></div>
        <small>© {CURRENT_YEAR} Travela</small>
      </footer>
    </div>
  );
}

export default HomePage;
