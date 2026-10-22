import { useState } from "react";
import "./Login.css";
import { useNavigate } from "react-router-dom";

function Login() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const navigate = useNavigate();

 const handleSubmit = async (e: React.SubmitEvent<HTMLFormElement>) => {
  e.preventDefault();

  try {
    const response = await fetch("http://localhost:5062/api/auth/login", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        email: email,
        password: password,
      }),
    });

    if (!response.ok) {
      alert("E-posta veya şifre hatalı.");
      return;
    }

  const data = await response.json();

// JWT token'ını oturum boyunca sakla
sessionStorage.setItem("travela_token", data.token);

// Kullanıcının temel bilgilerini sakla
sessionStorage.setItem(
  "travela_user",
  JSON.stringify(data.user)
);

console.log("Giriş başarılı. Kullanıcı rolü:", data.user.role);

if (data.user.role === "Admin") {
  navigate("/admin");
} else if (data.user.role === "Company") {
  navigate("/company");
} else if (data.user.role === "Passenger") {
  navigate("/");
}
  } catch (error) {
    console.error("API bağlantı hatası:", error);
    alert("Sunucuya bağlanılamadı.");
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

          <button type="submit" className="login-submit-btn">
            Giriş Yap
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