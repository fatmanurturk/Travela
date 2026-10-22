
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import type { ReactNode } from "react";
import "./AdminLayout.css";

type IconName =
  | "dashboard"
  | "companies"
  | "users"
  | "buses"
  | "trips"
  | "tickets"
  | "reports"
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
    companies: (
      <>
        <rect x="4" y="3" width="16" height="18" rx="2" />
        <path d="M9 21v-5h6v5M8 8h2M14 8h2M8 12h2M14 12h2" />
      </>
    ),
    users: (
      <>
        <circle cx="9" cy="8" r="3" />
        <path d="M3 20v-2a6 6 0 0 1 12 0v2M17 5a3 3 0 0 1 0 6M18 15a5 5 0 0 1 3 5" />
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
    tickets: (
      <>
        <path d="M3 7a2 2 0 0 0 2-2h14v5a2 2 0 0 0 0 4v5H5a2 2 0 0 0-2-2zM12 5v14" />
      </>
    ),
    reports: (
      <>
        <path d="M4 20V10M10 20V4M16 20v-8M22 20H2" />
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
      width="19"
      height="19"
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

const menuItems: {
  path: string;
  label: string;
  icon: IconName;
  end?: boolean;
}[] = [
  { path: "/admin", label: "Genel Bakış", icon: "dashboard", end: true },
  { path: "/admin/companies", label: "Firma Yönetimi", icon: "companies" },
  { path: "/admin/users", label: "Kullanıcı Yönetimi", icon: "users" },
  { path: "/admin/buses", label: "Otobüs Yönetimi", icon: "buses" },
  { path: "/admin/trips", label: "Sefer Yönetimi", icon: "trips" },
  { path: "/admin/tickets", label: "Bilet ve Rezervasyonlar", icon: "tickets" },
  { path: "/admin/reports", label: "Raporlar", icon: "reports" },
];

function AdminLayout() {
  const navigate = useNavigate();

  const handleLogout = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  return (
    <div className="admin-layout">
      <aside className="admin-sidebar">
        <div className="admin-brand">
          <div className="admin-brand-icon">T</div>
          <div>
            <h2 className="admin-logo">Travela</h2>
            <p className="admin-sidebar-title">Yönetim Paneli</p>
          </div>
        </div>

        <div className="admin-menu-label">YÖNETİM</div>

        <nav className="admin-menu" aria-label="Yönetim menüsü">
          {menuItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.end}
              className={({ isActive }) =>
                `admin-menu-link ${isActive ? "active" : ""}`
              }
            >
              <MenuIcon name={item.icon} />
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="admin-sidebar-footer">
          <div className="admin-footer-avatar">A</div>
          <div>
            <strong>Yönetici Paneli</strong>
            <span>Travela Admin</span>
          </div>
        </div>
      </aside>

      <div className="admin-content">
        <header className="admin-header">
          <div className="admin-header-info">
            <span className="admin-header-eyebrow">TRAVELA PLATFORMU</span>
            <h3>Yönetim Merkezi</h3>
          </div>

          <div className="admin-header-actions">
            <span className="admin-header-role">Yönetici</span>

            <button
              type="button"
              className="admin-logout-button"
              onClick={handleLogout}
            >
              <MenuIcon name="logout" />
              <span>Çıkış Yap</span>
            </button>
          </div>
        </header>

        <main className="admin-main">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export default AdminLayout;
