import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import "./Admin.css";

type DashboardStats = {
  totalCompanies: number;
  totalUsers: number;
  totalBuses: number;
};

function Admin() {
  const navigate = useNavigate();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const loadDashboard = async () => {
      try {
        const token = sessionStorage.getItem("travela_token");

        const response = await fetch(
          apiUrl("/api/admin/dashboard"),
          {
            headers: {
              Authorization: `Bearer ${token}`,
            },
          }
        );

        if (response.status === 401) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }

        if (!response.ok) {
          throw new Error(`Dashboard verileri alınamadı: ${response.status}`);
        }

        const data: DashboardStats = await response.json();
        setStats(data);
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Beklenmeyen bir hata oluştu."
        );
      } finally {
        setLoading(false);
      }
    };

    void loadDashboard();
  }, [navigate]);

  const statistics = [
    { title: "Toplam Firma", value: stats?.totalCompanies },
    { title: "Toplam Kullanıcı", value: stats?.totalUsers },
    { title: "Toplam Otobüs", value: stats?.totalBuses },
  ];

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading">
        <h1>Genel Bakış</h1>
        <p>Travela platformunun güncel durumunu buradan takip edebilirsiniz.</p>
      </div>

      {error && (
        <p role="alert" style={{ color: "#dc2626" }}>
          {error}
        </p>
      )}

      <div className="dashboard-stats">
        {statistics.map((stat) => (
          <div className="stat-card" key={stat.title}>
            <p>{stat.title}</p>
            <h2>{loading ? "Yükleniyor..." : stat.value ?? "—"}</h2>
          </div>
        ))}
      </div>

      <section className="dashboard-section">
        <h2>Son İşlemler</h2>
        <p>Son işlemler modülü henüz geliştirilmedi.</p>
      </section>
    </div>
  );
}

export default Admin;