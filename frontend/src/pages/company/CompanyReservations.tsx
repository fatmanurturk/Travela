import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";

type TripItem = {
  id: number;
  companyId: number;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  status: string;
  busCapacity: number;
  availableSeats: number;
};

function CompanyReservations() {
  const navigate = useNavigate();
  const [trips, setTrips] = useState<TripItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const getHeaders = (): HeadersInit => ({
    Authorization: `Bearer ${sessionStorage.getItem("travela_token") || ""}`,
  });

  useEffect(() => {
    const loadTrips = async () => {
      try {
        const response = await fetch(`${apiUrl("/api/trips")}?pageNumber=1&pageSize=100`, {
          headers: getHeaders(),
        });

        if (response.status === 401) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }

        if (!response.ok) {
          throw new Error("Sefer bilgileri alınamadı.");
        }

        const data = (await response.json()) as { items?: TripItem[] };
        if (!Array.isArray(data.items)) {
          throw new Error("Sefer verileri API tarafından beklenen biçimde döndürülmedi.");
        }
        setTrips(data.items);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Rezervasyon verileri yüklenemedi.");
      } finally {
        setLoading(false);
      }
    };

    void loadTrips();
  }, [navigate]);

  const stats = useMemo(() => {
    if (trips.length === 0) {
      return {
        totalTrips: "Henüz Kullanılamıyor",
        occupiedSeats: "Henüz Kullanılamıyor",
        occupancyRate: "Henüz Kullanılamıyor",
        reservationStatus: "Henüz Kullanılamıyor",
      };
    }

    const totalCapacity = trips.reduce((sum, trip) => sum + (trip.busCapacity || 0), 0);
    const reservedSeats = trips.reduce((sum, trip) => sum + Math.max((trip.busCapacity || 0) - (trip.availableSeats || 0), 0), 0);
    const occupancyRate = totalCapacity > 0 ? Math.round((reservedSeats / totalCapacity) * 100) : 0;
    const activeTrips = trips.filter((trip) => ["Scheduled", "InProgress", "Planlandı", "Devam Ediyor"].includes(trip.status)).length;

    return {
      totalTrips: String(trips.length),
      occupiedSeats: `${reservedSeats} koltuk`,
      occupancyRate: `${occupancyRate}%`,
      reservationStatus: activeTrips > 0 ? `${activeTrips} aktif sefer` : "Beklemede",
    };
  }, [trips]);

  return (
    <div className="company-dashboard">
      <div className="company-dashboard-header">
        <h1>Bilet ve Rezervasyonlar</h1>
        <p>Sefer bazlı doluluk ve rezervasyon görünümünü takip edin.</p>
      </div>

      {error && <p role="alert" style={{ color: "#dc2626", marginBottom: 20 }}>{error}</p>}

      <div className="company-stat-grid">
        <div className="company-stat-card">
          <p>Toplam Sefer</p>
          <h2>{loading ? "Yükleniyor..." : stats.totalTrips}</h2>
        </div>
        <div className="company-stat-card">
          <p>Dolu Koltuk</p>
          <h2>{loading ? "Yükleniyor..." : stats.occupiedSeats}</h2>
        </div>
        <div className="company-stat-card">
          <p>Doluluk Oranı</p>
          <h2>{loading ? "Yükleniyor..." : stats.occupancyRate}</h2>
        </div>
        <div className="company-stat-card">
          <p>Rezervasyon Durumu</p>
          <h2>{loading ? "Yükleniyor..." : stats.reservationStatus}</h2>
        </div>
      </div>

      <section className="company-panel">
        <h3>Sefer Bazlı Doluluk</h3>
        {trips.length === 0 ? (
          <div className="company-empty-state">
            Rezervasyon verisi henüz mevcut değil. Konaklama ve bilet sistemi entegre edilince bu bölüm güncellenecektir.
          </div>
        ) : (
          <ul className="company-list">
            {trips.slice(0, 8).map((trip) => {
              const reserved = Math.max((trip.busCapacity || 0) - (trip.availableSeats || 0), 0);
              const occupancyRate = trip.busCapacity > 0 ? Math.round((reserved / trip.busCapacity) * 100) : 0;

              return (
                <li key={trip.id}>
                  <div>
                    <strong>
                      {trip.departureCity} → {trip.arrivalCity}
                    </strong>
                    <div className="muted">
                      {new Date(trip.departureTime).toLocaleString("tr-TR")}
                    </div>
                  </div>
                  <div style={{ textAlign: "right" }}>
                    <div className="muted">{occupancyRate}% doluluk</div>
                    <span className="pill">{trip.status}</span>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}

export default CompanyReservations;
