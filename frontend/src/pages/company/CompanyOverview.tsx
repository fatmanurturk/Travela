import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";

const BUSES_API_URL = apiUrl("/api/buses");
const TRIPS_API_URL = apiUrl("/api/trips");

type SummaryCard = {
  title: string;
  value: string;
};

type UpcomingTrip = {
  id: number;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  status: string;
};

type BusSummary = {
  totalBuses: number;
};

type TripSummary = {
  scheduledTrips: number;
  inProgressTrips: number;
};

type TripListResponse = {
  items: UpcomingTrip[];
};

const unavailableStats = (): SummaryCard[] => [
  { title: "Toplam Otobüs", value: "Henüz Kullanılamıyor" },
  { title: "Aktif Sefer", value: "Henüz Kullanılamıyor" },
  { title: "Toplam Rezervasyon", value: "Henüz Kullanılamıyor" },
  { title: "Doluluk Oranı", value: "Henüz Kullanılamıyor" },
];

function CompanyOverview() {
  const navigate = useNavigate();
  const [stats, setStats] = useState<SummaryCard[]>([]);
  const [upcomingTrips, setUpcomingTrips] = useState<UpcomingTrip[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const getHeaders = (): HeadersInit => {
    const token = sessionStorage.getItem("travela_token");
    return token ? { Authorization: `Bearer ${token}` } : {};
  };

  useEffect(() => {
    const loadCompanyOverview = async () => {
      try {
        const [busResponse, tripSummaryResponse, tripResponse] = await Promise.all([
          fetch(`${BUSES_API_URL}/summary`, { headers: getHeaders() }),
          fetch(`${TRIPS_API_URL}/summary`, { headers: getHeaders() }),
          fetch(`${TRIPS_API_URL}?pageNumber=1&pageSize=100`, { headers: getHeaders() }),
        ]);

        const responses = [busResponse, tripSummaryResponse, tripResponse];
        if (responses.some((response) => response.status === 401)) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }

        const failedResponse = responses.find((response) => !response.ok);
        if (failedResponse) {
          throw new Error(`Firma dashboard verileri alınamadı (${failedResponse.status}).`);
        }

        const [busSummary, tripSummary, tripData] = await Promise.all([
          busResponse.json() as Promise<BusSummary>,
          tripSummaryResponse.json() as Promise<TripSummary>,
          tripResponse.json() as Promise<TripListResponse>,
        ]);

        const trips = Array.isArray(tripData.items) ? tripData.items : [];
        const now = Date.now();
        const upcoming = trips
          .filter((trip) => new Date(trip.departureTime).getTime() >= now)
          .slice(0, 4);
        const activeTrips = tripSummary.scheduledTrips + tripSummary.inProgressTrips;

        setStats([
          { title: "Toplam Otobüs", value: String(busSummary.totalBuses) },
          { title: "Aktif Sefer", value: String(activeTrips) },
          { title: "Toplam Rezervasyon", value: "Henüz Kullanılamıyor" },
          { title: "Doluluk Oranı", value: "Henüz Kullanılamıyor" },
        ]);
        setUpcomingTrips(upcoming);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Dashboard verileri alınamadı.");
        setStats(unavailableStats());
        setUpcomingTrips([]);
      } finally {
        setLoading(false);
      }
    };

    void loadCompanyOverview();
  }, [navigate]);

  return (
    <div className="company-dashboard">
      <div className="company-dashboard-header">
        <h1>Genel Bakış</h1>
        <p>Firma operasyonlarınızı tek ekranda takip edin.</p>
      </div>

      {error && <p role="alert" style={{ color: "#dc2626", marginBottom: 20 }}>{error}</p>}

      <div className="company-stat-grid">
        {(stats.length > 0 ? stats : unavailableStats()).map((stat) => (
          <div className="company-stat-card" key={stat.title}>
            <p>{stat.title}</p>
            <h2>{loading ? "Yükleniyor..." : stat.value}</h2>
          </div>
        ))}
      </div>

      <div className="company-panels-grid">
        <section className="company-panel">
          <h3>Yaklaşan Seferler</h3>
          {loading ? (
            <div className="company-empty-state">Seferler yükleniyor...</div>
          ) : upcomingTrips.length === 0 ? (
            <div className="company-empty-state">Yaklaşan sefer bulunmuyor.</div>
          ) : (
            <ul className="company-list">
              {upcomingTrips.map((trip) => (
                <li key={trip.id}>
                  <div>
                    <strong>{trip.departureCity} → {trip.arrivalCity}</strong>
                    <div className="muted">{new Date(trip.departureTime).toLocaleString("tr-TR")}</div>
                  </div>
                  <span className="pill">{trip.status}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="company-panel">
          <h3>Son İşlemler</h3>
          <div className="company-empty-state">
            İşlem geçmişi API üzerinden sunulmadığı için şu anda kullanılamıyor.
          </div>
        </section>

        <section className="company-panel">
          <h3>Hızlı İşlemler</h3>
          <div className="company-fast-actions">
            <button type="button" className="company-fast-button" onClick={() => navigate("/company/buses")}>Yeni Otobüs Ekle</button>
            <button type="button" className="company-fast-button" onClick={() => navigate("/company/trips")}>Yeni Sefer Oluştur</button>
            <button type="button" className="company-fast-button" onClick={() => navigate("/company/buses")}>Otobüsleri Görüntüle</button>
            <button type="button" className="company-fast-button" onClick={() => navigate("/company/trips")}>Seferleri Görüntüle</button>
          </div>
        </section>
      </div>
    </div>
  );
}

export default CompanyOverview;
