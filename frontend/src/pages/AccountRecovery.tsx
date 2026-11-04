import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import { apiUrl } from "../api";
import "./Login.css";

const genericMessage = "Bilgileriniz uygun bir hesapla eşleşiyorsa e-posta bağlantısı gönderilecektir. Gelen kutunuzu ve gereksiz e-posta klasörünü kontrol edin.";

export function EmailLinkRequestPage({ activation = false }: { activation?: boolean }) {
  const location = useLocation();
  const initial = location.state as { email?: string; registered?: boolean } | null;
  const [email, setEmail] = useState(typeof initial?.email === "string" ? initial.email : "");
  const [message, setMessage] = useState(initial?.registered ? "Hesabınız oluşturuldu. E-posta adresinize gönderilen aktivasyon bağlantısını açın. Gerekirse aşağıdan yeni bağlantı isteyebilirsiniz." : "");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    setBusy(true); setError(""); setMessage("");
    try {
      const response = await fetch(apiUrl(`/api/auth/${activation ? "resend-activation" : "forgot-password"}`), {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: email.trim() }), cache: "no-store",
      });
      const body = await response.json().catch(() => null);
      if (!response.ok) throw new Error(body?.message ?? (response.status === 429 ? "Çok fazla istek gönderildi. Bir dakika sonra tekrar deneyin." : "Bağlantı isteği alınamadı. Lütfen tekrar deneyin."));
      setMessage(body?.message ?? genericMessage);
    } catch (cause) { setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. Lütfen tekrar deneyin." : cause instanceof Error ? cause.message : "İstek tamamlanamadı."); }
    finally { setBusy(false); }
  }
  return <div className="login-page"><main className="login-card">
    <h1>Travela</h1><h2>{activation ? "Hesap aktivasyonu" : "Şifremi unuttum"}</h2>
    <p>{activation ? "Aktivasyon bağlantınız gelmediyse veya süresi dolduysa yeni bir bağlantı isteyin." : "Hesabınızın e-posta adresini girin. Yeni şifre belirlemeniz için bağlantı hazırlayacağız."}</p>
    <form onSubmit={submit}><div className="form-group"><label htmlFor="recovery-email">E-posta</label><input id="recovery-email" type="email" autoComplete="email" required maxLength={254} value={email} disabled={busy} onChange={event => setEmail(event.target.value)} /></div>
      {error && <p className="register-error" role="alert">{error}</p>}
      {message && <p className="account-link-message" role="status">{message}</p>}
      <button className="login-submit-btn" type="submit" disabled={busy}>{busy ? "İstek gönderiliyor..." : "Bağlantı gönder"}</button>
    </form><p>Yeni bağlantı istediğinizde önceki bağlantı geçersizleşebilir.</p><p><Link to="/login">Girişe dön</Link></p>
    <p><Link to={activation ? "/forgot-password" : "/resend-activation"}>{activation ? "Şifremi unuttum" : "Aktivasyon bağlantısı iste"}</Link></p>
  </main></div>;
}

export function AccountLinkPage({ activation = false }: { activation?: boolean }) {
  const location = useLocation();
  return <AccountLinkContent key={location.key} activation={activation} />;
}

function AccountLinkContent({ activation }: { activation: boolean }) {
  const [token] = useState(() => {
    const url = new URL(window.location.href);
    return new URLSearchParams(url.hash.slice(1)).get("token") ?? url.searchParams.get("token") ?? "";
  });
  const [checking, setChecking] = useState(Boolean(token));
  const [valid, setValid] = useState(false);
  const [requiresPassword, setRequiresPassword] = useState(!activation);
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState(token ? "" : "Bağlantı eksik. E-posta adresinize yeni bir bağlantı isteyin.");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    // Keep the secret in component memory; it never goes in a URL sent to the API or in storage.
    window.history.replaceState(window.history.state, "", window.location.pathname);
    const abort = new AbortController();
    if (!token) return;
    void (async () => {
      try {
        const response = await fetch(apiUrl("/api/auth/account-link"), { method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ token }), signal: abort.signal, cache: "no-store", referrerPolicy: "no-referrer" });
        const data = await response.json().catch(() => null);
        if (!response.ok) throw new Error(data?.message ?? "Bağlantı doğrulanamadı. Yeni bir bağlantı isteyin.");
        if (data?.purpose !== (activation ? "activation" : "reset")) throw new Error("Bu bağlantı bu işlem için geçerli değil. E-postanızdaki bağlantıyı yeniden açın.");
        if (!abort.signal.aborted) { setRequiresPassword(data.requiresPassword); setValid(true); }
      } catch (cause) { if (!abort.signal.aborted) setError(cause instanceof TypeError ? "Sunucuya bağlanılamadı. E-postanızdaki bağlantıyı yeniden açarak deneyin." : cause instanceof Error ? cause.message : "Bağlantı doğrulanamadı."); }
      finally { if (!abort.signal.aborted) setChecking(false); }
    })();
    return () => abort.abort();
  }, [token, activation]);

  async function complete(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !valid) return;
    setError("");
    if (requiresPassword && (newPassword.trim().length === 0 || newPassword.length < 8 || newPassword.length > 128 || newPassword !== confirmPassword)) {
      setError("Şifreniz 8–128 karakter olmalı ve tekrarıyla eşleşmelidir."); return;
    }
    setBusy(true);
    try {
      const response = await fetch(apiUrl(`/api/auth/${activation ? "activate-account" : "reset-password"}`), {
        method: "POST", headers: { "Content-Type": "application/json" }, cache: "no-store", referrerPolicy: "no-referrer",
        body: JSON.stringify({ token, ...(requiresPassword ? { newPassword, confirmPassword } : {}) }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 400 || response.status === 401) setValid(false);
        throw new Error(data?.message ?? "İşlem tamamlanamadı. Yeni bir bağlantı isteyin.");
      }
      sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); sessionStorage.removeItem("travela_pending_email");
      setSuccess(data?.message ?? "İşlem tamamlandı. Giriş yapabilirsiniz."); setNewPassword(""); setConfirmPassword(""); setValid(false);
    } catch (cause) { setError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Giriş yapmayı deneyin; gerekirse yeni bir bağlantı isteyin." : cause instanceof Error ? cause.message : "İşlem tamamlanamadı."); }
    finally { setBusy(false); }
  }
  return <div className="login-page"><main className="login-card">
    <h1>Travela</h1><h2>{activation ? "Hesabınızı aktifleştirin" : "Yeni şifre belirleyin"}</h2>
    {checking && <p role="status">Bağlantı kontrol ediliyor...</p>}
    {error && <p className="register-error" role="alert">{error}</p>}
    {success ? <><p className="account-link-message" role="status">{success}</p><Link className="login-submit-btn account-link-button" to="/login">Giriş yap</Link></> : valid && <form onSubmit={complete}>
      {requiresPassword ? <><div className="form-group"><label htmlFor="new-account-password">Yeni şifre</label><input id="new-account-password" type="password" autoComplete="new-password" required minLength={8} maxLength={128} value={newPassword} disabled={busy} onChange={event => setNewPassword(event.target.value)} /></div>
        <div className="form-group"><label htmlFor="confirm-account-password">Yeni şifre tekrar</label><input id="confirm-account-password" type="password" autoComplete="new-password" required minLength={8} maxLength={128} value={confirmPassword} disabled={busy} onChange={event => setConfirmPassword(event.target.value)} /></div></> : <p>E-posta adresinizi doğrulamak için aşağıdaki düğmeye basın. Kayıt sırasında belirlediğiniz şifre korunacak.</p>}
      <button className="login-submit-btn" type="submit" disabled={busy}>{busy ? "İşlem yapılıyor..." : activation ? "Hesabımı aktifleştir" : "Şifremi yenile"}</button>
    </form>}
    {!success && <p><Link to={activation ? "/resend-activation" : "/forgot-password"}>Yeni bağlantı iste</Link></p>}
    {!success && <p><Link to="/login">Girişe dön</Link></p>}
  </main></div>;
}
