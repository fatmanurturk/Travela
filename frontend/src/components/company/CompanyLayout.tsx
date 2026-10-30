import { useEffect, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import type { ReactNode } from "react";
import { apiUrl } from "../../api";
import "./CompanyLayout.css";

const COMPANY_PROFILE_API_URL = apiUrl("/api/company/profile");

type SessionUser = {
  name?: string;
  companyName?: string;
  company?: { name?: string };
};

type CompanyProfile = {
  name: string;
};

type IconName =
  | "dashboard"
  | "buses"
  | "trips"
  | "reservations"
  | "reports"
  | "profile"
  | "logout";

function MenuIcon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    dashboard: (
      <>
        <rect x="3" y="3" width="7" height="7" rx="1" />
        <rect x="14" y="3" width="7" height="7" rx="1" />
        <rect x="3" y="14" width="7" height="7" rx="1" />
        <rect x="14" y="14" width="7" height="7" rx="1" />
      </>
    ),
    buses: (
      <>
        <rect x="4" y="3" width="16" height="15" rx="3" />
        <path d="M4 11h16M8 18v3M16 18v3M8 7h2M14 7h2" />
      </>
    ),
    trips: (
      <>
        <rect x="3" y="5" width="18" height="16" rx="2" />
        <path d="M7 3v4M17 3v4M3 10h18M8 15h3" />
      </>
    ),
    reservations: (
      <>
        <path d="M3 7a2 2 0 0 0 2-2h14v5a2 2 0 0 0 0 4v5H5a2 2 0 0 0-2-2zM12 5v14" />
      </>
    ),
    reports: (
      <>
        <path d="M4 20V10M10 20V4M16 20v-8M22 20H2" />
      </>
    ),
    profile: (
      <>
        <circle cx="12" cy="8" r="4" />
        <path d="M4 20c1.5-3 5-5 8-5s6.5 2 8 5" />
      </>
    ),
    logout: (
      <>
        <path d="M10 17l5-5-5-5M15 12H3" />
        <path d="M12 3h6a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3h-6" />
      </>
    ),
  };

  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths[name]}
    </svg>
  );
}

const menuItems = [
  { path: "/company", label: "Genel Bakış", icon: "dashboard" as const, end: true },
  { path: "/company/buses", label: "Otobüs Yönetimi", icon: "buses" as const },
  { path: "/company/trips", label: "Sefer Yönetimi", icon: "trips" as const },
  { path: "/company/reservations", label: "Bilet ve Rezervasyonlar", icon: "reservations" as const },
  { path: "/company/reports", label: "Raporlar", icon: "reports" as const },
  { path: "/company/profile", label: "Firma Bilgileri", icon: "profile" as const },
];

function CompanyLayout() {
  const navigate = useNavigate();
  const storedUser = sessionStorage.getItem("travela_user");
  let user: SessionUser | null = null;
  try {
    user = storedUser ? JSON.parse(storedUser) as SessionUser : null;
  } catch {
    user = null;
  }
  const [companyName, setCompanyName] = useState(
    user?.companyName || user?.company?.name || "Firma",
  );
  const [companyError, setCompanyError] = useState("");

  useEffect(() => {
    const loadCompanyProfile = async () => {
      try {
        const token = sessionStorage.getItem("travela_token");
        const response = await fetch(COMPANY_PROFILE_API_URL, {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });

        if (response.status === 401) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }
        if (!response.ok) {
          throw new Error(`Firma bilgisi alınamadı (${response.status}).`);
        }

        const profile = await response.json() as CompanyProfile;
        setCompanyName(profile.name);
      } catch (err) {
        setCompanyError(err instanceof Error ? err.message : "Firma bilgisi alınamadı.");
      }
    };

    void loadCompanyProfile();
  }, [navigate]);

  const handleLogout = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  return (
    <div className="company-layout">
      <aside className="company-sidebar">
        <div className="company-brand">
          <div className="company-brand-icon">T</div>
          <div>
            <h2 className="company-logo">Travela</h2>
            <p className="company-sidebar-title">Firma Paneli</p>
          </div>
        </div>

        <div className="company-menu-label">YÖNETİM</div>

        <nav className="company-menu" aria-label="Firma menüsü">
          {menuItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.end}
              className={({ isActive }) =>
                `company-menu-link ${isActive ? "active" : ""}`
              }
            >
              <MenuIcon name={item.icon} />
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="company-sidebar-footer">
          <div className="company-footer-avatar">F</div>
          <div>
            <strong>{companyName}</strong>
            <span>Firma Hesabı</span>
          </div>
        </div>
      </aside>

      <div className="company-content">
        <header className="company-header">
          <div className="company-header-info">
            <span className="company-header-eyebrow">TRAVELA PLATFORMU</span>
            <h3>Firma Yönetim Merkezi</h3>
          </div>

          <div className="company-header-actions">
            <span className="company-header-company">{companyName}</span>
            <span className="company-header-user">{user?.name || "Firma Kullanıcısı"}</span>

            <button
              type="button"
              className="company-logout-button"
              onClick={handleLogout}
            >
              <MenuIcon name="logout" />
              <span>Çıkış Yap</span>
            </button>
          </div>
        </header>

        <main className="company-main">
          {companyError && <p role="alert" className="company-profile-error">{companyError}</p>}
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export default CompanyLayout;
