import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import "./Login.css";
import "../components/LegalConsent.css";
import { legalAcceptances } from "../legal";

function Register() {
  const navigate = useNavigate();
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [privacyRead, setPrivacyRead] = useState(false);
  const [termsAccepted, setTermsAccepted] = useState(false);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (!privacyRead || !termsAccepted) {
      setError("Devam etmek için aydınlatma metnini okuyup kullanım koşullarını kabul edin.");
      return;
    }

    if (password.length < 8) {
      setError("Şifre en az 8 karakter olmalıdır.");
      return;
    }

    if (password !== confirmPassword) {
      setError("Şifre ile tekrarı eşleşmiyor.");
      return;
    }

    setLoading(true);
    try {
      const response = await fetch(apiUrl("/api/auth/register"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          fullName,
          email,
          password,
          confirmPassword,
          legalAcceptances: legalAcceptances("PrivacyNotice", "TermsOfUse"),
        }),
      });
      if (!response.ok) {
        const body = await response.json() as { message?: string };
        throw new Error(body.message ?? "Kayıt oluşturulamadı.");
      }
      navigate("/resend-activation", { replace: true, state: { email: email.trim(), registered: true } });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Sunucuya bağlanılamadı.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <h1>Travela</h1>
        <h2>Yolculuğa katıl</h2>
        <p>Seferleri kolayca bulmak için yolcu hesabını oluştur.</p>
        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="full-name">Ad soyad</label>
            <input id="full-name" value={fullName} onChange={(event) => setFullName(event.target.value)} autoComplete="name" required maxLength={120} />
          </div>
          <div className="form-group">
            <label htmlFor="register-email">E-posta</label>
            <input id="register-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="email" required maxLength={254} />
          </div>
          <div className="form-group">
            <label htmlFor="register-password">Şifre</label>
            <input id="register-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} minLength={8} maxLength={128} autoComplete="new-password" required />
          </div>
          <div className="form-group">
            <label htmlFor="register-confirm-password">Şifre Tekrar</label>
            <input id="register-confirm-password" type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} minLength={8} maxLength={128} autoComplete="new-password" required />
          </div>
          {error && <p className="register-error" role="alert">{error}</p>}
          <label className="legal-consent">
            <input
              type="checkbox"
              required
              checked={privacyRead}
              disabled={loading}
              onChange={event => setPrivacyRead(event.target.checked)}
              aria-label="Gizlilik ve KVKK aydınlatma metnini okudum"
            />
            <span>
              <Link to="/gizlilik-ve-kvkk" target="_blank" rel="noopener noreferrer">
                Gizlilik ve KVKK aydınlatma metnini
              </Link>{" "}okudum.
            </span>
          </label>
          <label className="legal-consent">
            <input
              type="checkbox"
              required
              checked={termsAccepted}
              disabled={loading}
              onChange={event => setTermsAccepted(event.target.checked)}
              aria-label="Kullanım koşullarını kabul ediyorum"
            />
            <span>
              <Link to="/kullanim-kosullari" target="_blank" rel="noopener noreferrer">
                Kullanım koşullarını
              </Link>{" "}kabul ediyorum.
            </span>
          </label>
          <button
            type="submit"
            className="login-submit-btn"
            disabled={loading || !privacyRead || !termsAccepted}
          >
            {loading ? "Hesap oluşturuluyor..." : "Kayıt Ol"}
          </button>
        </form>
        <p>Zaten hesabın var mı? <Link to="/login">Giriş Yap</Link></p>
      </div>
    </div>
  );
}

export default Register;
