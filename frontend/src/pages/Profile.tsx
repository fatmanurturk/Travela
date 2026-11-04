import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import "./Profile.css";

type Preferences = { tripReminders: boolean; cancellations: boolean; refundUpdates: boolean; email: boolean; sms: boolean };
type UserProfile = { id: number; fullName: string; email: string; phoneNumber: string | null; role: string; createdAt: string; lastLoginAt: string | null; notificationPreferences: Preferences };
type TravelSummary = {
  upcomingCount: number;
  upcomingTrip: { tripId: number; pnr: string; companyName: string; departureCity: string; arrivalCity: string; departureTime: string; arrivalTime: string; seatNumber: number; amount: number; currency: string } | null;
  notifications: { id: string; title: string; description: string; href: string; date: string }[];
};
const emptyPreferences: Preferences = { tripReminders: false, cancellations: false, refundUpdates: false, email: true, sms: false };
const dates = new Intl.DateTimeFormat("tr-TR", { dateStyle: "long", timeZone: "Europe/Istanbul" });
const times = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short", timeZone: "Europe/Istanbul" });
const preferenceFields: { key: keyof Preferences; title: string; description: string }[] = [
  { key: "email", title: "E-posta bildirimleri", description: "Bilet onayı, PNR ve sefer değişikliklerini e-postayla al. Seçtiğin hatırlatma, iptal ve iade bildirimleri de bu kanalı kullanır." },
  { key: "sms", title: "SMS bildirimleri", description: "Bilette kayıtlı telefon numarana bildirim gönderilmesine izin ver. Numaran uluslararası biçimde kayıtlı olmalıdır." },
  { key: "tripReminders", title: "Yolculuk hatırlatmaları", description: "Önümüzdeki 24 saatte kalkacak biletli yolculuklarını profilinde hatırla." },
  { key: "cancellations", title: "Bilet ve sefer iptalleri", description: "Son 30 gündeki bilet iptallerini ve ilgili işlem bağlantılarını gör." },
  { key: "refundUpdates", title: "İade güncellemeleri", description: "Son 30 gündeki bekleyen, tamamlanan ve tamamlanamayan iadelerini takip et." },
];

function PasswordField({ id, label, value, onChange, disabled, isNew = false }: { id: string; label: string; value: string; onChange: (value: string) => void; disabled: boolean; isNew?: boolean }) {
  const [visible, setVisible] = useState(false);
  return <div className="account-field"><label htmlFor={id}>{label}</label><div className="account-password-input"><input id={id} type={visible ? "text" : "password"} autoComplete={isNew ? "new-password" : "current-password"} value={value} onChange={e => onChange(e.target.value)} disabled={disabled} required minLength={isNew ? 8 : undefined} maxLength={isNew ? 128 : 256} /><button type="button" aria-label={`${label} ${visible ? "gizle" : "göster"}`} aria-pressed={visible} onClick={() => setVisible(v => !v)}>{visible ? "Gizle" : "Göster"}</button></div></div>;
}

export default function Profile() {
  const navigate = useNavigate();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(true); const [loadError, setLoadError] = useState(""); const [revision, setRevision] = useState(0);
  const [fullName, setFullName] = useState(""); const [phone, setPhone] = useState("");
  const [preferences, setPreferences] = useState<Preferences>(emptyPreferences);
  const [pending, setPending] = useState<"profile" | "preferences" | "password" | "sessions" | null>(null);
  const [personalMessage, setPersonalMessage] = useState(""); const [personalError, setPersonalError] = useState("");
  const [preferenceMessage, setPreferenceMessage] = useState(""); const [preferenceError, setPreferenceError] = useState("");
  const [securityMessage, setSecurityMessage] = useState(""); const [securityError, setSecurityError] = useState("");
  const [currentPassword, setCurrentPassword] = useState(""); const [newPassword, setNewPassword] = useState(""); const [confirmPassword, setConfirmPassword] = useState(""); const [sessionPassword, setSessionPassword] = useState("");
  const [summary, setSummary] = useState<TravelSummary | null>(null); const [summaryLoading, setSummaryLoading] = useState(true); const [summaryError, setSummaryError] = useState(""); const [summaryRevision, setSummaryRevision] = useState(0);

  const expire = useCallback(() => { sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); navigate("/login?returnTo=%2Fprofile", { replace: true }); }, [navigate]);
  const request = useCallback(async <T,>(path: string, options?: RequestInit): Promise<T> => {
    const token = sessionStorage.getItem("travela_token");
    if (!token) { expire(); throw new Error("Lütfen yeniden giriş yap."); }
    const response = await fetch(apiUrl(path), { ...options, cache: "no-store", headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" } });
    const data = await response.json().catch(() => null);
    if (response.status === 401) { expire(); throw new Error("Oturumun sona erdi. Lütfen yeniden giriş yap."); }
    if (!response.ok) throw new Error(data?.message ?? (response.status === 400 ? "Bilgileri kontrol edip yeniden dene." : "İşlem tamamlanamadı. Lütfen tekrar dene."));
    return data as T;
  }, [expire]);

  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setLoadError("");
    request<UserProfile>("/api/profile", { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) { data.notificationPreferences = { ...emptyPreferences, ...data.notificationPreferences }; setProfile(data); setFullName(data.fullName); setPhone(data.phoneNumber ?? ""); setPreferences(data.notificationPreferences); }
    }).catch(error => { if (!controller.signal.aborted) setLoadError(error instanceof TypeError ? "Sunucuya bağlanılamadı." : error.message); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [request, revision]);

  useEffect(() => {
    const controller = new AbortController(); setSummaryLoading(true); setSummaryError("");
    request<TravelSummary>("/api/profile/travel-summary", { signal: controller.signal }).then(data => { if (!controller.signal.aborted) setSummary(data); })
      .catch(error => { if (!controller.signal.aborted) setSummaryError(error instanceof TypeError ? "Yolculuk bilgileri için sunucuya bağlanılamadı." : error.message); })
      .finally(() => { if (!controller.signal.aborted) setSummaryLoading(false); });
    return () => controller.abort();
  }, [request, summaryRevision]);

  useEffect(() => {
    const refresh = () => { if (document.visibilityState === "visible") setSummaryRevision(v => v + 1); };
    const timer = window.setInterval(refresh, 60000); window.addEventListener("focus", refresh);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, []);

  const busy = pending !== null;
  const changed = profile && (fullName.trim() !== profile.fullName || phone.trim() !== (profile.phoneNumber ?? ""));
  const preferencesChanged = profile && preferenceFields.some(({ key }) => preferences[key] !== profile.notificationPreferences[key]);
  const passwordChecks = [newPassword.length >= 8 && newPassword.length <= 128, Boolean(newPassword && newPassword !== currentPassword), Boolean(confirmPassword && newPassword === confirmPassword)];
  const applySession = (token: string) => { if (!token) { expire(); throw new Error("Lütfen yeniden giriş yap."); } sessionStorage.setItem("travela_token", token); };

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !changed || !profile) return;
    setPending("profile"); setPersonalError(""); setPersonalMessage("");
    try {
      const data = await request<{ fullName: string; phoneNumber: string | null; message: string }>("/api/profile", { method: "PUT", body: JSON.stringify({ fullName: fullName.trim(), phoneNumber: phone.trim() || null }) });
      setProfile({ ...profile, fullName: data.fullName, phoneNumber: data.phoneNumber }); setFullName(data.fullName); setPhone(data.phoneNumber ?? "");
      const stored = JSON.parse(sessionStorage.getItem("travela_user") ?? "{}"); sessionStorage.setItem("travela_user", JSON.stringify({ ...stored, fullName: data.fullName })); setPersonalMessage(data.message);
    } catch (error) { setPersonalError(error instanceof Error ? error.message : "Bilgiler kaydedilemedi."); }
    finally { setPending(null); }
  }
  async function savePreferences(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !profile || !preferencesChanged) return;
    setPending("preferences"); setPreferenceError(""); setPreferenceMessage("");
    try {
      const data = await request<{ message: string; notificationPreferences: Preferences }>("/api/profile/notification-preferences", { method: "PUT", body: JSON.stringify(preferences) });
      setProfile({ ...profile, notificationPreferences: data.notificationPreferences }); setPreferences(data.notificationPreferences); setPreferenceMessage(data.message); setSummaryRevision(v => v + 1);
    } catch (error) { setPreferenceError(error instanceof Error ? error.message : "Tercihler kaydedilemedi."); }
    finally { setPending(null); }
  }
  async function changePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return; setSecurityError(""); setSecurityMessage("");
    if (!currentPassword || !passwordChecks.every(Boolean)) { setSecurityError("Mevcut şifreni gir ve aşağıdaki şifre koşullarını tamamla."); return; }
    setPending("password");
    try {
      const data = await request<{ message: string; token: string }>("/api/profile/change-password", { method: "POST", body: JSON.stringify({ currentPassword, newPassword, confirmPassword }) });
      applySession(data.token); setCurrentPassword(""); setNewPassword(""); setConfirmPassword(""); setSecurityMessage(data.message);
    } catch (error) { setSecurityError(error instanceof Error ? error.message : "Şifre değiştirilemedi."); }
    finally { setPending(null); }
  }
  async function revokeSessions(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return; setPending("sessions"); setSecurityError(""); setSecurityMessage("");
    try {
      const data = await request<{ message: string; token: string }>("/api/profile/revoke-other-sessions", { method: "POST", body: JSON.stringify({ currentPassword: sessionPassword }) });
      applySession(data.token); setSessionPassword(""); setSecurityMessage(data.message);
    } catch (error) { setSecurityError(error instanceof Error ? error.message : "Diğer oturumlar kapatılamadı."); }
    finally { setPending(null); }
  }

  const trip = summary?.upcomingTrip;
  return <div className="travela-site"><SiteHeader /><main className="account-page">
    <Link className="account-back" to="/">← Ana sayfaya dön</Link>
    <header className="account-heading"><span className="eyebrow">Hesabın, yolculukların, tercihlerin</span><h1>Profilim</h1><p>Bilgilerini güncelle, yaklaşan yolculuğunu takip et ve hesabını yönet.</p></header>
    {loading ? <div className="account-card" role="status">Profil bilgileri yükleniyor...</div> : !profile ? <div className="account-card"><p className="account-error" role="alert">{loadError || "Profil bilgileri alınamadı."}</p><button className="site-button secondary" type="button" onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></div> : <div className="account-layout">
      <aside className="account-sidebar"><div className="account-avatar" aria-hidden="true">{profile.fullName.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join("").toLocaleUpperCase("tr-TR")}</div><h2>{profile.fullName}</h2><p>{profile.email}</p><span className="account-member">Üyelik: {dates.format(new Date(profile.createdAt))}</span><nav aria-label="Profil bölümleri"><a href="#kisisel-bilgiler">Kişisel bilgiler</a><a href="#yaklasan-yolculuk">Yaklaşan yolculuk</a><a href="#bildirim-tercihleri">Bildirim tercihleri</a><a href="#hesap-guvenligi">Hesap güvenliği</a></nav></aside>
      <div className="account-content">
        <section className="account-card" id="kisisel-bilgiler" aria-labelledby="personal-title"><span className="account-kicker">Sana ait bilgiler</span><h2 id="personal-title">Kişisel bilgiler</h2><p>Adını ve iletişim numaranı buradan güncelleyebilirsin.</p>
          <form onSubmit={saveProfile}><div className="account-form-grid"><div className="account-field"><label htmlFor="profile-name">Ad soyad</label><input id="profile-name" autoComplete="name" value={fullName} onChange={e => setFullName(e.target.value)} required maxLength={120} disabled={busy} /></div><div className="account-field"><label htmlFor="profile-phone">Telefon numarası <span>(isteğe bağlı)</span></label><input id="profile-phone" type="tel" inputMode="tel" autoComplete="tel" placeholder="05xx xxx xx xx" maxLength={32} value={phone} onChange={e => setPhone(e.target.value)} disabled={busy} /><small>Türkiye numarası veya +ülke koduyla bir numara girebilirsin.</small></div><div className="account-field account-full"><label htmlFor="profile-email">E-posta</label><input id="profile-email" type="email" value={profile.email} readOnly /><small>E-posta adresi bu ekrandan değiştirilemez.</small></div></div>
            {personalError && <p role="alert" className="account-error">{personalError}</p>}{personalMessage && <p role="status" className="account-success">{personalMessage}</p>}<div className="account-actions"><button type="submit" className="site-button primary" disabled={busy || !changed || !fullName.trim()}>{pending === "profile" ? "Kaydediliyor..." : "Bilgileri kaydet"}</button></div>
          </form>
        </section>

        <section className="account-card account-journey" id="yaklasan-yolculuk" aria-labelledby="journey-title"><div className="account-section-heading"><div><span className="account-kicker">Bir sonraki durak</span><h2 id="journey-title">Yaklaşan yolculuğun</h2></div><Link to="/tickets">Biletlerim →</Link></div>
          {summaryLoading ? <p role="status">Yolculuk bilgileri yükleniyor...</p> : summaryError ? <div><p role="alert" className="account-error">{summaryError}</p><button className="site-button secondary" type="button" onClick={() => setSummaryRevision(v => v + 1)}>Yolculukları tekrar yükle</button></div> : trip ? <>
            <p className="account-journey-route">{trip.departureCity} <span aria-hidden="true">→</span> {trip.arrivalCity}</p><dl className="account-trip-details"><div><dt>Kalkış</dt><dd>{times.format(new Date(trip.departureTime))}</dd></div><div><dt>Varış</dt><dd>{times.format(new Date(trip.arrivalTime))}</dd></div><div><dt>Firma / Koltuk</dt><dd>{trip.companyName} · {trip.seatNumber} numara</dd></div><div><dt>PNR</dt><dd className="account-pnr">{trip.pnr}</dd></div></dl><div className="account-actions"><Link className="site-button primary" to={`/tickets/${encodeURIComponent(trip.pnr)}`}>Bilet detayını gör</Link><span>{summary.upcomingCount} yaklaşan yolculuk · Türkiye saati</span></div>
          </> : <div className="account-empty"><strong>Yeni yolculuklar seni bekliyor.</strong><p>Henüz yaklaşan bir biletli yolculuğun bulunmuyor.</p><Link className="site-button secondary" to="/#sefer-ara">Sefer ara</Link></div>}
        </section>

        <section className="account-card" id="bildirim-tercihleri" aria-labelledby="preferences-title"><span className="account-kicker">Gelişmelerden haberdar ol</span><h2 id="preferences-title">Bildirim tercihleri</h2><p>Bildirim kanallarını ve almak istediğin yolculuk güncellemelerini seç.</p><form onSubmit={savePreferences}><div className="account-preferences">{preferenceFields.map(field => <label className="account-preference" key={field.key}><span><strong>{field.title}</strong><small>{field.description}</small></span><input type="checkbox" role="switch" aria-label={field.title} checked={preferences[field.key]} onChange={e => setPreferences(previous => ({ ...previous, [field.key]: e.target.checked }))} disabled={busy} /></label>)}</div>
            {preferenceError && <p role="alert" className="account-error">{preferenceError}</p>}{preferenceMessage && <p role="status" className="account-success">{preferenceMessage}</p>}<div className="account-actions"><button type="submit" className="site-button primary" disabled={busy || !preferencesChanged}>{pending === "preferences" ? "Kaydediliyor..." : "Tercihleri kaydet"}</button></div>
          </form>
          {!summaryLoading && !summaryError && <div className="account-notifications"><h3>Güncel bildirimlerin</h3>{summary?.notifications.length ? <ul>{summary.notifications.map(notice => <li key={notice.id}><div><strong>{notice.title}</strong><time dateTime={notice.date}>{times.format(new Date(notice.date))}</time></div><p>{notice.description}</p><Link to={notice.href}>Bileti incele →</Link></li>)}</ul> : <p>Kaydettiğin tercihlere uygun güncel bir bildirim bulunmuyor.</p>}</div>}
        </section>

        <section className="account-card" id="hesap-guvenligi" aria-labelledby="security-title"><span className="account-kicker">Kontrol sende</span><h2 id="security-title">Hesap güvenliği</h2>{profile.lastLoginAt && <p>Son giriş: {times.format(new Date(profile.lastLoginAt))} (Türkiye)</p>}<h3>Şifreni değiştir</h3><p>Şifren değiştiğinde diğer oturumlar kapatılır; bu oturum açık kalır.</p><form onSubmit={changePassword}>
            <PasswordField id="profile-current-password" label="Mevcut şifre" value={currentPassword} onChange={setCurrentPassword} disabled={busy} />
            <div className="account-form-grid"><PasswordField id="profile-new-password" label="Yeni şifre" value={newPassword} onChange={setNewPassword} disabled={busy} isNew /><PasswordField id="profile-confirm-password" label="Yeni şifre tekrar" value={confirmPassword} onChange={setConfirmPassword} disabled={busy} isNew /></div>
            <ul className="account-password-checks" aria-label="Yeni şifre koşulları">{["8–128 karakter uzunluğunda", "Mevcut şifrenden farklı", "Tekrar alanıyla eşleşiyor"].map((label, index) => <li key={label} className={passwordChecks[index] ? "met" : ""}><span aria-hidden="true">{passwordChecks[index] ? "✓" : "○"}</span>{label}{passwordChecks[index] && <span className="account-visually-hidden"> — tamamlandı</span>}</li>)}</ul>
            <div className="account-actions"><button className="site-button primary" type="submit" disabled={busy || !currentPassword || !passwordChecks.every(Boolean)}>{pending === "password" ? "Değiştiriliyor..." : "Şifreyi değiştir"}</button></div>
          </form>
          <details className="account-session-control"><summary>Diğer oturumları kapat</summary><p>Hesabının açık olduğu diğer oturumları kapatmak için mevcut şifrenle onayla. Bu oturumu kullanmaya devam edebilirsin.</p><form onSubmit={revokeSessions}><PasswordField id="profile-session-password" label="Oturum kapatma onay şifresi" value={sessionPassword} onChange={setSessionPassword} disabled={busy} /><button className="site-button secondary" type="submit" disabled={busy || !sessionPassword}>{pending === "sessions" ? "Oturumlar kapatılıyor..." : "Diğer oturumları kapat"}</button></form></details>
          {securityError && <p role="alert" className="account-error">{securityError}</p>}{securityMessage && <p role="status" className="account-success">{securityMessage}</p>}
        </section>
      </div>
    </div>}
  </main></div>;
}
