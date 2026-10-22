
import { useEffect, useState } from "react";
import "../Admin.css";

const API_URL = "http://localhost:5062/api/buses";

type Bus = {
  id: number;
  companyId: number;
  companyName: string;
  plateNumber: string;
  brand: string;
  model: string;
  seatCapacity: number;
  isActive: boolean;
};

function BusManagement() {
  const [buses, setBuses] = useState<Bus[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const loadBuses = async () => {
      try {
        const token = sessionStorage.getItem("travela_token");

        const response = await fetch(API_URL, {
          headers: {
            Authorization: `Bearer ${token}`,
          },
        });

        if (!response.ok) {
          throw new Error(
            `Otobüsler alınamadı: ${response.status}`
          );
        }

        const data: Bus[] = await response.json();
        setBuses(data);
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Otobüsler yüklenemedi."
        );
      } finally {
        setLoading(false);
      }
    };

    void loadBuses();
  }, []);

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading">
        <h1>Otobüs Yönetimi</h1>
        <p>
          Travela sistemindeki tüm firmalara ait otobüsleri görüntüleyin.
        </p>
      </div>

      <section className="dashboard-section">
        <h2>Otobüs Listesi</h2>

        {loading && <p>Otobüsler yükleniyor...</p>}

        {error && (
          <p role="alert" style={{ color: "#dc2626" }}>
            {error}
          </p>
        )}

        {!loading && !error && buses.length === 0 && (
          <p>Henüz kayıtlı otobüs bulunmuyor.</p>
        )}

        {!loading && !error && buses.length > 0 && (
          <div style={{ overflowX: "auto" }}>
            <table
              style={{
                width: "100%",
                borderCollapse: "collapse",
                textAlign: "left",
              }}
            >
              <thead>
                <tr>
                  <th>Plaka</th>
                  <th>Firma</th>
                  <th>Marka</th>
                  <th>Model</th>
                  <th>Kapasite</th>
                  <th>Durum</th>
                </tr>
              </thead>

              <tbody>
                {buses.map((bus) => (
                  <tr key={bus.id}>
                    <td>{bus.plateNumber}</td>
                    <td>{bus.companyName}</td>
                    <td>{bus.brand}</td>
                    <td>{bus.model}</td>
                    <td>{bus.seatCapacity}</td>
                    <td>{bus.isActive ? "Aktif" : "Pasif"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

export default BusManagement;
