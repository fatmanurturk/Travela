import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";

type BusItem = {
  id: number;
  companyId: number;
  plateNumber: string;
  seatCapacity: number;
  isActive: boolean;
};

type TripItem = {
  id: number;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  status: string;
  availableSeats: number;
  busCapacity: number;
};

function CompanyReports() {
  const navigate = useNavigate();
  const [buses, setBuses] = useState<BusItem[]>([]);
  const [trips, setTrips] = useState<TripItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const getHeaders = (): HeadersInit => ({
    Authorization: `Bearer ${sessionStorage.getItem("travela_token") || ""}`,
  });

  useEffect(() => {
    const loadData = async () => {
      try {
        const [busResponse, tripResponse] = await Promise.all([
          fetch(`${apiUrl("/api/buses")}?pageNumber=1&pageSize=100`, {
            headers: getHeaders(),
          }),
          fetch(`${apiUrl("/api/trips")}?pageNumber=1&pageSize=100`, {
            headers: getHeaders(),
          }),
        ]);

        if (busResponse.status === 401 || tripResponse.status === 401) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }

        if (!busResponse.ok || !tripResponse.ok) {
          throw new Error("Rapor verileri alınamadı.");
        }

        const busData = (await busResponse.json()) as { items?: BusItem[] };
        const tripData = (await tripResponse.json()) as { items?: TripItem[] };

        if (!Array.isArray(busData.items) || !Array.isArray(tripData.items)) {
          throw new Error("Rapor verileri API tarafından beklenen biçimde döndürülmedi.");
        }
        setBuses(busData.items);
        setTrips(tripData.items);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Raporlar yüklenemedi.");
      } finally {
        setLoading(false);
      }
    };

    void loadData();
  }, [navigate]);

  const reportStats = useMemo(() => {
    if (buses.length === 0 && trips.length === 0) {
      return {
        totalBuses: "Henüz Kullanılamıyor",
        totalTrips: "Henüz Kullanılamıyor",
        avgOccupancy: "Henüz Kullanılamıyor",
        strongestRoute: "Henüz Kullanılamıyor",
      };
    }

    const totalCapacity = trips.reduce((sum, trip) => sum + (trip.busCapacity || 0), 0);
    const reservedSeats = trips.reduce((sum, trip) => sum + Math.max((trip.busCapacity || 0) - (trip.availableSeats || 0), 0), 0);
    const avgOccupancy = totalCapacity > 0 ? `${Math.round((reservedSeats / totalCapacity) * 100)}%` : "Henüz Kullanılamıyor";

    const routeMap = new Map<string, number>();
    trips.forEach((trip) => {
      const routeKey = `${trip.departureCity} → ${trip.arrivalCity}`;
      routeMap.set(routeKey, (routeMap.get(routeKey) ?? 0) + 1);
    });
    const strongestRoute = routeMap.size > 0 ? [...routeMap.entries()].sort((a, b) => b[1] - a[1])[0][0] : "Henüz Kullanılamıyor";

    return {
      totalBuses: String(buses.length),
      totalTrips: String(trips.length),
      avgOccupancy,
      strongestRoute,
    };
  }, [buses, trips]);

  return (
    <div className="company-dashboard">
      <div className="company-dashboard-header">
        <h1>Raporlar</h1>
        <p>Firma operasyonlarınızın kısa vadeli görünümünü izleyin.</p>
      </div>

      {error && <p role="alert" style={{ color: "#dc2626", marginBottom: 20 }}>{error}</p>}

      <div className="company-stat-grid">
        <div className="company-stat-card">
          <p>Toplam Otobüs</p>
          <h2>{loading ? "Yükleniyor..." : reportStats.totalBuses}</h2>
        </div>
        <div className="company-stat-card">
          <p>Toplam Sefer</p>
          <h2>{loading ? "Yükleniyor..." : reportStats.totalTrips}</h2>
        </div>
        <div className="company-stat-card">
          <p>Ortalama Doluluk</p>
          <h2>{loading ? "Yükleniyor..." : reportStats.avgOccupancy}</h2>
        </div>
        <div className="company-stat-card">
          <p>En Yoğun Hat</p>
          <h2>{loading ? "Yükleniyor..." : reportStats.strongestRoute}</h2>
        </div>
      </div>

      <section className="company-panel">
        <h3>Güzergâh Performansı</h3>
        {trips.length === 0 ? (
          <div className="company-empty-state">
            Raporlanacak sefer verisi bulunmuyor. Seferler oluşturulduğunda bu bölüm otomatik olarak güncellenecektir.
          </div>
        ) : (
          <ul className="company-list">
            {[...new Map(trips.map((trip) => [
              `${trip.departureCity} → ${trip.arrivalCity}`,
              {
                route: `${trip.departureCity} → ${trip.arrivalCity}`,
                count: trips.filter((item) => item.departureCity === trip.departureCity && item.arrivalCity === trip.arrivalCity).length,
              },
            ])).values()].slice(0, 6).map((routeInfo) => (
              <li key={routeInfo.route}>
                <div>
                  <strong>{routeInfo.route}</strong>
                </div>
                <span className="pill">{routeInfo.count} sefer</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

export default CompanyReports;
