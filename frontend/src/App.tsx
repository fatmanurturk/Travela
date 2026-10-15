import "./App.css";

function App() {
  return (
    <div className="app">
      {/* Navigasyon menüsü */}
      <header className="navbar">
        <div className="logo">Travela</div>

        <nav className="nav-links">
          <a href="/">Ana Sayfa</a>
          <a href="#sefer-ara">Sefer Ara</a>
        </nav>

        <div className="nav-actions">
          <button type="button" className="login-btn">
            Giriş Yap
          </button>

          <button type="button" className="register-btn">
            Kayıt Ol
          </button>
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
  );
}

export default App;