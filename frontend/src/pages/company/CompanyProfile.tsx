import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";

const COMPANY_ME_API_URL = "http://localhost:5062/api/company/me";
const COMPANY_SUMMARY_API_URL = "http://localhost:5062/api/company/me/summary";
const PROFILE_API_URL = "http://localhost:5062/api/profile";

type CompanyInfo = {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
  createdAt: string;
  isActive: boolean;
};

type UserInfo = {
  id: number;
  fullName: string;
  email: string;
  role: string;
  companyId: number | null;
  companyName: string | null;
};

type CompanySummary = {
  totalBuses: number;
  activeBuses: number;
  totalTrips: number;
  activeTrips: number;
};

const getHeaders = (json = false): HeadersInit => {
  const token = sessionStorage.getItem("travela_token");

  return {
    ...(json ? { "Content-Type": "application/json" } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
};

const isValidEmail = (value: string) => {
  if (!value.trim()) return false;
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
};

const formatDate = (value: string | null | undefined) => {
  if (!value) return "—";
  return new Date(value).toLocaleDateString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
};

function CompanyProfile() {
  const navigate = useNavigate();
  const [company, setCompany] = useState<CompanyInfo | null>(null);
  const [user, setUser] = useState<UserInfo | null>(null);
  const [summary, setSummary] = useState<CompanySummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [companyEditMode, setCompanyEditMode] = useState(false);
  const [companyForm, setCompanyForm] = useState({ email: "", phone: "", address: "" });
  const [companySaveLoading, setCompanySaveLoading] = useState(false);
  const [companyFormMessage, setCompanyFormMessage] = useState("");
  const [companyFormError, setCompanyFormError] = useState("");

  const [fullName, setFullName] = useState("");
  const [fullNameEditMode, setFullNameEditMode] = useState(false);
  const [fullNameLoading, setFullNameLoading] = useState(false);
  const [fullNameMessage, setFullNameMessage] = useState("");
  const [fullNameError, setFullNameError] = useState("");

  const [passwordForm, setPasswordForm] = useState({
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
  });
  const [passwordLoading, setPasswordLoading] = useState(false);
  const [passwordMessage, setPasswordMessage] = useState("");
  const [passwordError, setPasswordError] = useState("");

  const handleUnauthorized = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  const readError = async (response: Response, fallback: string) => {
    const body = (await response.json().catch(() => null)) as { message?: string } | null;
    return body?.message || `${fallback} (${response.status})`;
  };

  const loadData = async () => {
    setLoading(true);
    setError("");

    try {
      const [companyResponse, profileResponse, summaryResponse] = await Promise.all([
        fetch(COMPANY_ME_API_URL, { headers: getHeaders() }),
        fetch(PROFILE_API_URL, { headers: getHeaders() }),
        fetch(COMPANY_SUMMARY_API_URL, { headers: getHeaders() }),
      ]);

      if (companyResponse.status === 401 || profileResponse.status === 401 || summaryResponse.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!companyResponse.ok) {
        throw new Error(await readError(companyResponse, "Firma bilgileri alınamadı"));
      }

      if (!profileResponse.ok) {
        throw new Error(await readError(profileResponse, "Kullanıcı bilgileri alınamadı"));
      }

      if (!summaryResponse.ok) {
        throw new Error(await readError(summaryResponse, "Firma istatistikleri alınamadı"));
      }

      const [companyData, userData, summaryData] = await Promise.all([
        companyResponse.json() as Promise<CompanyInfo>,
        profileResponse.json() as Promise<UserInfo>,
        summaryResponse.json() as Promise<CompanySummary>,
      ]);

      setCompany(companyData);
      setUser(userData);
      setSummary(summaryData);
      setFullName(userData.fullName);
      setCompanyForm({
        email: companyData.email ?? "",
        phone: companyData.phone ?? "",
        address: companyData.address ?? "",
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Firma bilgileri yüklenemedi.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, [navigate]);

  const handleCompanySave = async (event: React.FormEvent) => {
    event.preventDefault();
    setCompanyFormError("");
    setCompanyFormMessage("");

    if (!companyForm.email && companyForm.email !== "") {
      setCompanyFormError("E-posta alanı boş olamaz.");
      return;
    }

    if (companyForm.email && !isValidEmail(companyForm.email)) {
      setCompanyFormError("Geçerli bir e-posta adresi girin.");
      return;
    }

    setCompanySaveLoading(true);
    try {
      const response = await fetch(COMPANY_ME_API_URL, {
        method: "PUT",
        headers: getHeaders(true),
        body: JSON.stringify({
          email: companyForm.email.trim() || null,
          phone: companyForm.phone.trim() || null,
          address: companyForm.address.trim() || null,
        }),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!response.ok) {
        throw new Error(await readError(response, "Firma bilgileri kaydedilemedi"));
      }

      const payload = (await response.json()) as { company?: CompanyInfo; message?: string };
      setCompany((current) => (payload.company ? payload.company : current));
      setCompanyFormMessage(payload.message || "Firma bilgileri güncellendi.");
      setCompanyEditMode(false);
      await loadData();
    } catch (err) {
      setCompanyFormError(err instanceof Error ? err.message : "Firma bilgileri kaydedilemedi.");
    } finally {
      setCompanySaveLoading(false);
    }
  };

  const handleFullNameSave = async (event: React.FormEvent) => {
    event.preventDefault();
    setFullNameError("");
    setFullNameMessage("");

    const trimmed = fullName.trim();
    if (!trimmed) {
      setFullNameError("Ad soyad alanı boş olamaz.");
      return;
    }

    setFullNameLoading(true);
    try {
      const response = await fetch(PROFILE_API_URL, {
        method: "PUT",
        headers: getHeaders(true),
        body: JSON.stringify({ fullName: trimmed }),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!response.ok) {
        throw new Error(await readError(response, "Profil bilgileri kaydedilemedi"));
      }

      const payload = (await response.json()) as { fullName?: string; message?: string };
      setUser((current) => (current ? { ...current, fullName: payload.fullName ?? trimmed } : current));
      setFullName(payload.fullName ?? trimmed);
      setFullNameMessage(payload.message || "Ad soyad güncellendi.");
      setFullNameEditMode(false);
    } catch (err) {
      setFullNameError(err instanceof Error ? err.message : "Profil bilgileri kaydedilemedi.");
    } finally {
      setFullNameLoading(false);
    }
  };

  const handlePasswordSave = async (event: React.FormEvent) => {
    event.preventDefault();
    setPasswordError("");
    setPasswordMessage("");

    if (!passwordForm.currentPassword || !passwordForm.newPassword || !passwordForm.confirmPassword) {
      setPasswordError("Tüm şifre alanları zorunludur.");
      return;
    }

    if (passwordForm.newPassword.length < 8) {
      setPasswordError("Yeni şifre en az 8 karakter olmalıdır.");
      return;
    }

    if (passwordForm.newPassword !== passwordForm.confirmPassword) {
      setPasswordError("Yeni şifre ile tekrar alanı eşleşmiyor.");
      return;
    }

    setPasswordLoading(true);
    try {
      const response = await fetch(`${PROFILE_API_URL}/change-password`, {
        method: "POST",
        headers: getHeaders(true),
        body: JSON.stringify({
          currentPassword: passwordForm.currentPassword,
          newPassword: passwordForm.newPassword,
          confirmPassword: passwordForm.confirmPassword,
        }),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (response.status === 429) {
        throw new Error("Çok fazla deneme yapıldı. Lütfen 5 dakika sonra tekrar deneyin.");
      }

      if (!response.ok) {
        throw new Error(await readError(response, "Şifre değiştirilemedi"));
      }

      const payload = (await response.json()) as { message?: string };
      setPasswordMessage(payload.message || "Şifre başarıyla değiştirildi.");
      setPasswordForm({ currentPassword: "", newPassword: "", confirmPassword: "" });
    } catch (err) {
      setPasswordError(err instanceof Error ? err.message : "Şifre değiştirilemedi.");
    } finally {
      setPasswordLoading(false);
    }
  };

  if (loading) {
    return <div className="company-profile-page"><div className="company-empty-state">Firma bilgileri yükleniyor...</div></div>;
  }

  return (
    <div className="company-profile-page">
      <div className="company-dashboard-header">
        <h1>Firma Bilgileri</h1>
        <p>Firma bilgilerinizi, kullanıcı detaylarınızı ve güvenlik ayarlarınızı yönetin.</p>
      </div>

      {error && <p role="alert" className="company-profile-error">{error}</p>}

      <div className="company-stat-grid">
        <div className="company-stat-card">
          <p>Toplam Otobüs</p>
          <h2>{summary?.totalBuses ?? 0}</h2>
        </div>
        <div className="company-stat-card">
          <p>Aktif Otobüs</p>
          <h2>{summary?.activeBuses ?? 0}</h2>
        </div>
        <div className="company-stat-card">
          <p>Toplam Sefer</p>
          <h2>{summary?.totalTrips ?? 0}</h2>
        </div>
        <div className="company-stat-card">
          <p>Aktif Sefer</p>
          <h2>{summary?.activeTrips ?? 0}</h2>
        </div>
      </div>

      <div className="company-profile-grid">
        <section className="company-panel company-profile-panel">
          <div className="company-card-header">
            <h3>Firma Genel Bilgileri</h3>
            {!companyEditMode && (
              <button type="button" className="company-primary-button" onClick={() => setCompanyEditMode(true)}>
                Düzenle
              </button>
            )}
          </div>

          {companyEditMode ? (
            <form onSubmit={handleCompanySave} className="company-form">
              <div className="company-form-grid">
                <label>
                  Firma ID
                  <input value={company?.id ?? ""} readOnly />
                </label>
                <label>
                  Firma Adı
                  <input value={company?.name ?? ""} readOnly />
                </label>
                <label>
                  E-posta Adresi
                  <input
                    value={companyForm.email}
                    onChange={(event) => setCompanyForm((current) => ({ ...current, email: event.target.value }))}
                  />
                </label>
                <label>
                  Telefon Numarası
                  <input
                    value={companyForm.phone}
                    onChange={(event) => setCompanyForm((current) => ({ ...current, phone: event.target.value }))}
                  />
                </label>
                <label className="company-full-width">
                  Adres
                  <textarea
                    rows={4}
                    value={companyForm.address}
                    onChange={(event) => setCompanyForm((current) => ({ ...current, address: event.target.value }))}
                  />
                </label>
                <label>
                  Kayıt Tarihi
                  <input value={formatDate(company?.createdAt)} readOnly />
                </label>
                <label>
                  Firma Durumu
                  <input value={company?.isActive ? "Aktif" : "Pasif"} readOnly />
                </label>
              </div>

              {companyFormError && <p className="company-alert error">{companyFormError}</p>}
              {companyFormMessage && <p className="company-alert success">{companyFormMessage}</p>}

              <div className="company-form-actions">
                <button type="submit" className="company-primary-button" disabled={companySaveLoading}>
                  {companySaveLoading ? "Kaydediliyor..." : "Kaydet"}
                </button>
                <button
                  type="button"
                  className="company-secondary-button"
                  onClick={() => {
                    setCompanyEditMode(false);
                    setCompanyFormError("");
                    setCompanyFormMessage("");
                    if (company) {
                      setCompanyForm({
                        email: company.email ?? "",
                        phone: company.phone ?? "",
                        address: company.address ?? "",
                      });
                    }
                  }}
                >
                  İptal
                </button>
              </div>
            </form>
          ) : (
            <div className="company-data-list">
              <div className="company-info-row"><span>Firma ID</span><strong>#{company?.id ?? "—"}</strong></div>
              <div className="company-info-row"><span>Firma Adı</span><strong>{company?.name ?? "—"}</strong></div>
              <div className="company-info-row"><span>E-posta Adresi</span><strong>{company?.email ?? "—"}</strong></div>
              <div className="company-info-row"><span>Telefon Numarası</span><strong>{company?.phone ?? "—"}</strong></div>
              <div className="company-info-row"><span>Adres</span><strong>{company?.address ?? "—"}</strong></div>
              <div className="company-info-row"><span>Kayıt Tarihi</span><strong>{formatDate(company?.createdAt)}</strong></div>
              <div className="company-info-row"><span>Firma Durumu</span><strong className={company?.isActive ? "company-status active" : "company-status inactive"}>{company?.isActive ? "Aktif" : "Pasif"}</strong></div>
            </div>
          )}
        </section>

        <section className="company-panel company-profile-panel">
          <div className="company-card-header">
            <h3>Yetkili Kullanıcı Bilgileri</h3>
          </div>

          <div className="company-data-list">
            <div className="company-info-row"><span>Ad Soyad</span><strong>{user?.fullName ?? "—"}</strong></div>
            <div className="company-info-row"><span>E-posta</span><strong>{user?.email ?? "—"}</strong></div>
            <div className="company-info-row"><span>Kullanıcı Rolü</span><strong>{user?.role ?? "—"}</strong></div>
            <div className="company-info-row"><span>Bağlı Firma</span><strong>{user?.companyName ?? company?.name ?? "—"}</strong></div>
          </div>

          {fullNameEditMode ? (
            <form onSubmit={handleFullNameSave} className="company-form company-inline-form">
              <label>
                Ad Soyad
                <input value={fullName} onChange={(event) => setFullName(event.target.value)} />
              </label>
              {fullNameError && <p className="company-alert error">{fullNameError}</p>}
              {fullNameMessage && <p className="company-alert success">{fullNameMessage}</p>}
              <div className="company-form-actions">
                <button type="submit" className="company-primary-button" disabled={fullNameLoading}>
                  {fullNameLoading ? "Kaydediliyor..." : "Kaydet"}
                </button>
                <button
                  type="button"
                  className="company-secondary-button"
                  onClick={() => {
                    setFullName(user?.fullName ?? "");
                    setFullNameEditMode(false);
                    setFullNameError("");
                    setFullNameMessage("");
                  }}
                >
                  İptal
                </button>
              </div>
            </form>
          ) : (
            <div className="company-edit-trigger">
              <button type="button" className="company-secondary-button" onClick={() => setFullNameEditMode(true)}>
                Ad Soyad Güncelle
              </button>
            </div>
          )}
        </section>
      </div>

      <section className="company-panel company-password-panel">
        <div className="company-card-header">
          <h3>Şifre Değiştir</h3>
        </div>

        <form onSubmit={handlePasswordSave} className="company-form company-password-form">
          <div className="company-form-grid">
            <label>
              Mevcut Şifre
              <input
                type="password"
                value={passwordForm.currentPassword}
                onChange={(event) => setPasswordForm((current) => ({ ...current, currentPassword: event.target.value }))}
              />
            </label>
            <label>
              Yeni Şifre
              <input
                type="password"
                value={passwordForm.newPassword}
                onChange={(event) => setPasswordForm((current) => ({ ...current, newPassword: event.target.value }))}
              />
            </label>
            <label>
              Yeni Şifre Tekrar
              <input
                type="password"
                value={passwordForm.confirmPassword}
                onChange={(event) => setPasswordForm((current) => ({ ...current, confirmPassword: event.target.value }))}
              />
            </label>
          </div>

          {passwordError && <p className="company-alert error">{passwordError}</p>}
          {passwordMessage && <p className="company-alert success">{passwordMessage}</p>}

          <div className="company-form-actions">
            <button type="submit" className="company-primary-button" disabled={passwordLoading}>
              {passwordLoading ? "Şifre değiştiriliyor..." : "Şifre Değiştir"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}

export default CompanyProfile;
