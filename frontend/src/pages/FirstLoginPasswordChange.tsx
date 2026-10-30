import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import "./Login.css";

function FirstLoginPasswordChange() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const pendingEmail = sessionStorage.getItem("travela_pending_email") || "";
    if (!pendingEmail) {
      navigate("/login", { replace: true });
      return;
    }

    setEmail(pendingEmail);
  }, [navigate]);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");

    if (!currentPassword || !newPassword || !confirmPassword) {
      setError("Tüm şifre alanları zorunludur.");
      return;
    }

    if (newPassword.length < 8) {
      setError("Yeni şifre en az 8 karakter olmalıdır.");
      return;
    }

    if (newPassword !== confirmPassword) {
      setError("Yeni şifre ile tekrarı eşleşmiyor.");
      return;
    }

    setLoading(true);
    try {
      const response = await fetch(apiUrl("/api/auth/change-password"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email,
          currentPassword,
          newPassword,
          confirmPassword,
        }),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error((data && typeof data.message === "string") ? data.message : "Şifre değiştirilemedi.");
      }

      sessionStorage.removeItem("travela_pending_email");
      sessionStorage.setItem("travela_token", data.token);
      sessionStorage.setItem("travela_user", JSON.stringify(data.user));

      if (data.user.role === "Company") {
        navigate("/company", { replace: true });
      } else {
        navigate("/login", { replace: true });
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Şifre değiştirilemedi.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <h1>Travela</h1>
        <h2>İlk Giriş Şifresi</h2>
        <p>Firma hesabınız için güvenli bir yeni şifre belirleyin.</p>

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="first-email">E-posta</label>
            <input id="first-email" type="email" value={email} readOnly />
          </div>

          <div className="form-group">
            <label htmlFor="current-password">Mevcut Şifre</label>
            <input id="current-password" type="password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required />
          </div>

          <div className="form-group">
            <label htmlFor="new-password">Yeni Şifre</label>
            <input id="new-password" type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} minLength={8} required />
          </div>

          <div className="form-group">
            <label htmlFor="confirm-new-password">Yeni Şifre Tekrar</label>
            <input id="confirm-new-password" type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} minLength={8} required />
          </div>

          {error && <p className="register-error" role="alert">{error}</p>}

          <button type="submit" className="login-submit-btn" disabled={loading}>
            {loading ? "Şifre değiştiriliyor..." : "Şifreyi Değiştir"}
          </button>
        </form>
      </div>
    </div>
  );
}

export default FirstLoginPasswordChange;
