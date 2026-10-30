import { Link, useNavigate } from "react-router-dom";

type StoredUser = {
  fullName?: string;
  role?: string;
};

function SiteHeader() {
  const navigate = useNavigate();
  const token = sessionStorage.getItem("travela_token");
  const storedUser = sessionStorage.getItem("travela_user");

  let user: StoredUser | null = null;

  try {
    user = storedUser
      ? (JSON.parse(storedUser) as StoredUser)
      : null;
  } catch {
    user = null;
  }

  const isLoggedIn = Boolean(token && user);
  const firstName = user?.fullName?.trim().split(/\s+/)[0];

  const signOut = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/", { replace: true });
  };

  return (
    <header className="site-header">
      <Link
        to="/"
        className="site-logo"
        aria-label="Travela ana sayfa"
      >
        Travela<span>.</span>
      </Link>

      <nav className="site-nav" aria-label="Ana menü">
        <Link to="/">Ana Sayfa</Link>
        <Link to="/#sefer-ara">Sefer Ara</Link>
      </nav>

      <div className="site-actions">
        {isLoggedIn ? (
          <>
            {user?.role === "Passenger" && (
  <>
    <span className="site-user-name">
      {firstName ? `Merhaba, ${firstName}` : "Hoş geldiniz"}
    </span>

    <Link to="/profile" className="site-button secondary">
      Profilim
    </Link>
  </>
)}
            {user?.role === "Admin" && (
              <button
                type="button"
                className="site-button secondary"
                onClick={() => navigate("/admin")}
              >
                Yönetici Paneli
              </button>
            )}

            {user?.role === "Company" && (
              <button
                type="button"
                className="site-button secondary"
                onClick={() => navigate("/company")}
              >
                Firma Paneli
              </button>
            )}

            <button
              type="button"
              className="site-button primary"
              onClick={signOut}
            >
              Çıkış Yap
            </button>
          </>
        ) : (
          <>
            <button
              type="button"
              className="site-button secondary"
              onClick={() => navigate("/login")}
            >
              Giriş Yap
            </button>

            <button
              type="button"
              className="site-button primary"
              onClick={() => navigate("/register")}
            >
              Kayıt Ol
            </button>
          </>
        )}
      </div>
    </header>
  );
}

export default SiteHeader;