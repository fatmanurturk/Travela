import { Link, useNavigate } from "react-router-dom";

function SiteHeader() {
  const navigate = useNavigate();
  const token = sessionStorage.getItem("travela_token");
  const storedUser = sessionStorage.getItem("travela_user");
  let user: { role?: string } | null = null;

  try {
    user = storedUser ? JSON.parse(storedUser) as { role?: string } : null;
  } catch {
    user = null;
  }

  const isLoggedIn = Boolean(token && user);

  const signOut = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/");
  };

  return (
    <header className="site-header">
      <Link to="/" className="site-logo" aria-label="Travela ana sayfa">Travela<span>.</span></Link>
      <nav className="site-nav" aria-label="Ana menü">
        <Link to="/">Ana Sayfa</Link>
        <Link to="/#sefer-ara">Sefer Ara</Link>
      </nav>
      <div className="site-actions">
        {isLoggedIn ? (
          <>
            {user?.role === "Admin" && <button className="site-button secondary" onClick={() => navigate("/admin")}>Yönetici Paneli</button>}
            {user?.role === "Company" && <button className="site-button secondary" onClick={() => navigate("/company")}>Firma Paneli</button>}
            <button className="site-button primary" onClick={signOut}>Çıkış Yap</button>
          </>
        ) : (
          <>
            <button className="site-button secondary" onClick={() => navigate("/login")}>Giriş Yap</button>
            <button className="site-button primary" onClick={() => navigate("/register")}>Kayıt Ol</button>
          </>
        )}
      </div>
    </header>
  );
}

export default SiteHeader;
