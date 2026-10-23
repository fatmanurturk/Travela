import "./App.css";
import { Routes, Route } from "react-router-dom";
import Login from "./pages/Login";
import Company from "./pages/Company";
import ProtectedRoute from "./components/ProtectedRoute";
import Admin from "./pages/Admin";
import { useNavigate } from "react-router-dom";
import AdminLayout from "./components/admin/AdminLayout";
import CompanyManagement from "./pages/admin/CompanyManagement";
import CompanyDetails from "./pages/admin/CompanyDetails";
import UserManagement from "./pages/admin/UserManagement";
import BusManagement from "./pages/admin/BusManagement";

function App() {

  const navigate = useNavigate();

const storedUser = sessionStorage.getItem("travela_user");
const token = sessionStorage.getItem("travela_token");

let user = null;

try {
  user = storedUser ? JSON.parse(storedUser) : null;
} catch {
  user = null;
}

const isLoggedIn = Boolean(token && user);
  return (
    <Routes>
      <Route path="/" element={
        <div className="app">
      {/* Navigasyon menüsü */}
      <header className="navbar">
        <div className="logo">Travela</div>

        <nav className="nav-links">
          <a href="/">Ana Sayfa</a>
          <a href="#sefer-ara">Sefer Ara</a>
        </nav>

       <div className="nav-actions">
  {isLoggedIn ? (
    <>
      {user.role === "Admin" && (
        <button
          type="button"
          className="login-btn"
          onClick={() => navigate("/admin")}
        >
          Yönetici Paneli
        </button>
      )}

      {user.role === "Company" && (
        <button
          type="button"
          className="login-btn"
          onClick={() => navigate("/company")}
        >
          Firma Paneli
        </button>
      )}

      {user.role === "Passenger" && (
        <button
          type="button"
          className="login-btn"
          onClick={() => navigate("/")}
        >
          Biletlerim
        </button>
      )}

      <button
        type="button"
        className="register-btn"
        onClick={() => {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login");
        }}
      >
        Çıkış Yap
      </button>
    </>
  ) : (
    <>
      <button
        type="button"
        className="login-btn"
        onClick={() => navigate("/login")}
      >
        Giriş Yap
      </button>

      <button
        type="button"
        className="register-btn"
        onClick={() => navigate("/register")}
      >
        Kayıt Ol
      </button>
    </>
  )}
</div>
      </header>

      {/* Ana sayfa */}
      <main className="main-content" id="sefer-ara">
        <h1>Yolculuğun Travela ile Başlasın!</h1>

        <p>
          Türkiye'nin dört bir yanına otobüs biletini kolayca bul.
        </p>

        {/* Sefer arama formu */}
        <form
          className="search-form"
          onSubmit={(e) => {
            e.preventDefault();
            alert("Sefer arama özelliği yakında aktif olacak!");
          }}
        >
          <div className="form-group">
            <label htmlFor="departure">Nereden</label>
            <input
              id="departure"
              type="text"
              placeholder="Kalkış şehri"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="arrival">Nereye</label>
            <input
              id="arrival"
              type="text"
              placeholder="Varış şehri"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="date">Gidiş Tarihi</label>
            <input
              id="date"
              type="date"
              required
            />
          </div>

          <button type="submit" className="search-btn">
            Sefer Ara
          </button>
        </form>
      </main>
           </div>
      } />

      <Route path="/login" element={<Login />} />
     <Route
  path="/company"
  element={
    <ProtectedRoute allowedRole="Company">
      <Company />
    </ProtectedRoute>
  }
/>


<Route
  path="/admin"
  element={
    <ProtectedRoute allowedRole="Admin">
      <AdminLayout />
    </ProtectedRoute>
  }
>
  <Route index element={<Admin />} />
  <Route path="companies" element={<CompanyManagement />} />
  <Route path="companies/:id" element={<CompanyDetails />} />
  <Route path="users" element={<UserManagement />} />
  <Route path="buses" element={<BusManagement />} />
</Route>



    </Routes>
  );
}

export default App;