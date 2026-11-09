import { Link } from "react-router-dom";
import "./SiteFooter.css";

const CURRENT_YEAR = new Date().getFullYear();

const footerLinks = [
  { to: "/hakkimizda", label: "Hakkımızda" },
  { to: "/iletisim", label: "İletişim" },
  { to: "/yardim", label: "Yardım" },
  { to: "/gizlilik-ve-kvkk", label: "Gizlilik ve KVKK Aydınlatma Metni" },
  { to: "/mesafeli-satis-sozlesmesi", label: "Mesafeli Satış Sözleşmesi" },
  { to: "/iade-ve-iptal", label: "İade ve İptal Koşulları" },
  { to: "/cerez-politikasi", label: "Çerez Politikası" },
];

function SiteFooter() {
  return (
    <footer className="site-footer shared-site-footer">
      <div className="site-footer-brand">
        <Link to="/" className="site-logo" aria-label="Travela ana sayfa">
          Travela<span>.</span>
        </Link>
        <p>Yolculuğunu planlamanın sade yolu.</p>
        <small>© {CURRENT_YEAR} Travela</small>
      </div>

      <nav className="site-footer-navigation" aria-label="Alt menü">
        <ul>
          {footerLinks.map(({ to, label }) => (
            <li key={to}>
              <Link to={to}>{label}</Link>
            </li>
          ))}
        </ul>
      </nav>
    </footer>
  );
}

export default SiteFooter;
