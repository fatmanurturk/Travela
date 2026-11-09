import { Link } from "react-router-dom";
import SiteHeader from "../components/SiteHeader";
import "./NotFoundPage.css";

function NotFoundPage() {
  return (
    <div className="travela-site">
      <SiteHeader />
      <main className="not-found-page" aria-labelledby="not-found-heading">
        <section className="not-found-card">
          <span className="not-found-code" aria-hidden="true">404</span>
          <h1 id="not-found-heading">Sayfa bulunamadı</h1>
          <p>
            Aradığınız sayfa taşınmış olabilir veya adresi yanlış yazılmış olabilir.
            Ana sayfaya dönebilir ya da yeni bir sefer arayabilirsiniz.
          </p>
          <div className="not-found-actions">
            <Link to="/" className="site-button primary">
              Ana sayfaya dön
            </Link>
            <Link to="/#sefer-ara" className="site-button secondary" reloadDocument>
              Sefer ara
            </Link>
          </div>
        </section>
      </main>
    </div>
  );
}

export default NotFoundPage;
