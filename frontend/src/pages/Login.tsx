import { useState } from "react";
import "./Login.css";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";

function Login() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  const handleSubmit = async (e: React.SubmitEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError("");
    setLoading(true);

    try {
      const response = await fetch(apiUrl("/api/auth/login"), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          email,
          password,
        }),
      });

      const data = await response.json().catch(() => null);

      if (!response.ok) {
        setError((data && typeof data.message === "string") ? data.message : "E-posta veya şifre hatalı.");
        return;
      }

      if (data?.requiresPasswordChange) {
        sessionStorage.setItem("travela_pending_email", email.trim().toLowerCase());
        navigate("/first-login-password-change", { replace: true });
        return;
      }

      if (!data?.token) {
        setError("Giriş yanıtı eksik. Lütfen tekrar deneyin.");
        return;
      }

      sessionStorage.setItem("travela_token", data.token);
      sessionStorage.setItem("travela_user", JSON.stringify(data.user));

      if (data.user.role === "Admin") {
        navigate("/admin", { replace: true });
      } else if (data.user.role === "Company") {
        navigate("/company", { replace: true });
      } else if (data.user.role === "Passenger") {
        navigate("/", { replace: true });
      } else {
        navigate("/", { replace: true });
      }
    } catch (error) {
      console.error("API bağlantı hatası:", error);
      setError("Sunucuya bağlanılamadı. Lütfen daha sonra tekrar deneyin.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <h1>Travela</h1>
        <h2>Hesabına Giriş Yap</h2>
        <p>Yolculuğuna kaldığın yerden devam et.</p>

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="email">E-posta</label>
            <input
              id="email"
              type="email"
              placeholder="E-posta adresiniz"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="password">Şifre</label>
            <input
              id="password"
              type="password"
              placeholder="Şifreniz"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>

          {error && <p className="register-error" role="alert">{error}</p>}

          <button type="submit" className="login-submit-btn" disabled={loading}>
            {loading ? "Giriş yapılıyor..." : "Giriş Yap"}
          </button>
        </form>

        <p>
          Hesabın yok mu? <a href="/register">Kayıt Ol</a>
        </p>
      </div>
    </div>
  );
}

export default Login;