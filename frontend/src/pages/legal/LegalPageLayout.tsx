// Hukuki inceleme gerektirir.
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import SiteHeader from "../../components/SiteHeader";
import "./LegalPages.css";

type Props = {
  title: string;
  version: string;
  updatedAt: string;
  children: ReactNode;
};

export default function LegalPageLayout({ title, version, updatedAt, children }: Props) {
  const formattedDate = new Date(`${updatedAt}T12:00:00+03:00`).toLocaleDateString("tr-TR", {
    day: "numeric",
    month: "long",
    year: "numeric",
    timeZone: "Europe/Istanbul",
  });

  return (
    <div className="travela-site">
      <SiteHeader />
      <main className="legal-page" aria-labelledby="legal-page-title">
        <Link to="/" className="back-home">← Ana sayfaya dön</Link>
        <article>
          <p className="legal-draft-notice" role="note">
            <strong>Hukuki inceleme gerektirir.</strong> Bu metin taslaktır.
            Yer tutucular gerçek işletmeci bilgileriyle doldurulmalı ve metin yayından önce incelenmelidir.
          </p>
          <h1 id="legal-page-title">{title}</h1>
          <p className="legal-page-meta">
            Son güncelleme: <time dateTime={updatedAt}>{formattedDate}</time>
            {" · "}Belge sürümü: {version}
          </p>
          {children}
          <nav className="legal-related" aria-label="İlgili hukuki sayfalar">
            <Link to="/iletisim">İletişim</Link>
            <Link to="/gizlilik-ve-kvkk">Gizlilik ve KVKK</Link>
            <Link to="/iade-ve-iptal">İade ve iptal</Link>
            <Link to="/kullanim-kosullari">Kullanım koşulları</Link>
          </nav>
        </article>
      </main>
    </div>
  );
}
