import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { apiUrl } from "../../api";
import "./CompanyDetails.css";

type CompanyUser = {
  id: number;
  fullName: string;
  email: string;
  role: string;
  isActive?: boolean;
  mustChangePassword?: boolean;
  createdAt: string;
};

type CompanyBus = {
  id: number;
  plateNumber: string;
  brand: string | null;
  model: string | null;
  seatCapacity: number;
  isActive: boolean;
  createdAt: string;
};

type CompanyDetailsData = {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
  createdAt: string;
  isActive: boolean;
  totalUsers: number;
  totalBuses: number;
  users: CompanyUser[];
  buses: CompanyBus[];
};

const API_URL = apiUrl("/api/admin/companies");

const formatDate = (value?: string | null) => {
  if (!value) {
    return "Belirtilmemiş";
  }

  return new Date(value).toLocaleDateString("tr-TR");
};

const formatLocation = (value?: string | null) => value || "Belirtilmemiş";

function CompanyDetails() {
  const { id } = useParams();
  const [company, setCompany] = useState<CompanyDetailsData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [userForm, setUserForm] = useState({
    fullName: "",
    email: "",
    temporaryPassword: "",
    confirmPassword: "",
  });
  const [userFormLoading, setUserFormLoading] = useState(false);
  const [userFormError, setUserFormError] = useState("");
  const [userFormSuccess, setUserFormSuccess] = useState("");

  const reloadCompany = async () => {
    if (!id) {
      setError("Firma bulunamadı.");
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const token = sessionStorage.getItem("travela_token");
      if (!token) {
        setError("Oturumunuz geçersiz veya süresi dolmuş.");
        setLoading(false);
        return;
      }

      const response = await fetch(`${API_URL}/${id}`, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (response.status === 404) {
        setError("Firma bulunamadı.");
        setCompany(null);
        setLoading(false);
        return;
      }

      if (response.status === 401) {
        setError("Oturumunuz geçersiz veya süresi dolmuş.");
        setCompany(null);
        setLoading(false);
        return;
      }

      if (response.status === 403) {
        setError("Bu sayfaya erişim yetkiniz bulunmuyor.");
        setCompany(null);
        setLoading(false);
        return;
      }

      if (response.status === 405) {
        setError("Sunucu tarafında firma detay endpoint’i aktif değil. Backend uygulamasını yeniden başlatın.");
        setCompany(null);
        setLoading(false);
        return;
      }

      if (!response.ok) {
        const errorBody = await response.json().catch(() => null);
        const message = errorBody?.message || "Firma bilgileri yüklenirken bir hata oluştu.";
        setError(message);
        setCompany(null);
        setLoading(false);
        return;
      }

      const data: CompanyDetailsData = await response.json();
      setCompany(data);
    } catch {
      setError("Sunucuya bağlanırken bir hata oluştu. Backend çalışıyor mu kontrol edin.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void reloadCompany();
  }, [id]);

  const handleCreateCompanyUser = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setUserFormError("");
    setUserFormSuccess("");

    if (!id) {
      setUserFormError("Firma kimliği bulunamadı.");
      return;
    }

    if (!userForm.fullName.trim() || !userForm.email.trim()) {
      setUserFormError("Ad soyad ve e-posta zorunludur.");
      return;
    }

    if (userForm.temporaryPassword.length < 8) {
      setUserFormError("Geçici şifre en az 8 karakter olmalıdır.");
      return;
    }

    if (userForm.temporaryPassword !== userForm.confirmPassword) {
      setUserFormError("Geçici şifre ile tekrarı eşleşmiyor.");
      return;
    }

    setUserFormLoading(true);
    try {
      const token = sessionStorage.getItem("travela_token");
      if (!token) {
        setUserFormError("Oturumunuz geçersiz veya süresi dolmuş.");
        return;
      }

      const response = await fetch(`${API_URL}/${id}/users`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          fullName: userForm.fullName,
          email: userForm.email,
          temporaryPassword: userForm.temporaryPassword,
          confirmPassword: userForm.confirmPassword,
        }),
      });

      const body = await response.json().catch(() => null) as { message?: string } | null;
      if (!response.ok) {
        throw new Error(body?.message || "Firma kullanıcısı oluşturulamadı.");
      }

      setUserFormSuccess(body?.message || "Firma kullanıcısı başarıyla oluşturuldu.");
      setUserForm({ fullName: "", email: "", temporaryPassword: "", confirmPassword: "" });
      await reloadCompany();
    } catch (cause) {
      setUserFormError(cause instanceof Error ? cause.message : "Firma kullanıcısı oluşturulamadı.");
    } finally {
      setUserFormLoading(false);
    }
  };

  const companyName = company?.name || "Firma bilgileri yükleniyor...";
  const companyStatus = company ? (company.isActive ? "Aktif" : "Pasif") : "Durum yükleniyor";
  const companyStatusClass = company ? (company.isActive ? "active" : "inactive") : "loading";

  return (
    <div className="admin-dashboard company-details">
      <div className="dashboard-heading company-details-heading">
        <div>
          <p className="company-details-eyebrow">FİRMA YÖNETİMİ</p>
          <h1>Firma Detayları</h1>
          <p>Firma, kullanıcı ve otobüs bilgilerini bu sayfadan görüntüleyin.</p>
        </div>
        <Link className="company-details-back" to="/admin/companies">
          Firma Listesine Dön
        </Link>
      </div>

      {error && !loading && (
        <section className="dashboard-section">
          <p className="company-details-error" role="alert">
            {error}
          </p>
        </section>
      )}

      <section className="dashboard-section company-details-overview">
        <div className="company-details-name">
          <div className="company-details-avatar" aria-hidden="true">
            {companyName.charAt(0).toUpperCase() || "F"}
          </div>
          <div>
            <h2>{companyName}</h2>
            <span className={`company-details-status company-details-status--${companyStatusClass}`}>
              {companyStatus}
            </span>
          </div>
        </div>
        <div className="company-details-stats">
          <article className="company-details-stat">
            <span>Toplam Kullanıcı</span>
            <strong>{loading ? "—" : company?.totalUsers ?? 0}</strong>
          </article>
          <article className="company-details-stat">
            <span>Toplam Otobüs</span>
            <strong>{loading ? "—" : company?.totalBuses ?? 0}</strong>
          </article>
        </div>
      </section>

      <section className="dashboard-section">
        <h2>Firma Bilgileri</h2>
        <dl className="company-details-fields">
          <div className="company-details-field">
            <dt>Firma ID</dt>
            <dd>{company?.id ?? id ?? "Belirtilmemiş"}</dd>
          </div>
          <div className="company-details-field">
            <dt>Firma Adı</dt>
            <dd>{company?.name ?? "Belirtilmemiş"}</dd>
          </div>
          <div className="company-details-field">
            <dt>E-posta</dt>
            <dd>{company?.email || "Belirtilmemiş"}</dd>
          </div>
          <div className="company-details-field">
            <dt>Telefon</dt>
            <dd>{company?.phone || "Belirtilmemiş"}</dd>
          </div>
          <div className="company-details-field">
            <dt>Adres</dt>
            <dd>{formatLocation(company?.address)}</dd>
          </div>
          <div className="company-details-field">
            <dt>Kayıt Tarihi</dt>
            <dd>{formatDate(company?.createdAt)}</dd>
          </div>
          <div className="company-details-field">
            <dt>Durum</dt>
            <dd>{company ? (company.isActive ? "Aktif" : "Pasif") : "Belirtilmemiş"}</dd>
          </div>
        </dl>
      </section>

      <section className="dashboard-section">
        <h2>Firma Kullanıcıları</h2>

        <form onSubmit={handleCreateCompanyUser} className="company-form" style={{ marginBottom: "1.5rem" }}>
          <div className="company-form-grid">
            <div className="company-form-field">
              <label htmlFor="companyUserFullName">Ad Soyad</label>
              <input id="companyUserFullName" value={userForm.fullName} onChange={(event) => setUserForm((current) => ({ ...current, fullName: event.target.value }))} required />
            </div>
            <div className="company-form-field">
              <label htmlFor="companyUserEmail">E-posta</label>
              <input id="companyUserEmail" type="email" value={userForm.email} onChange={(event) => setUserForm((current) => ({ ...current, email: event.target.value }))} required />
            </div>
            <div className="company-form-field">
              <label htmlFor="companyUserPassword">İlk Giriş Geçici Şifre</label>
              <input id="companyUserPassword" type="password" value={userForm.temporaryPassword} onChange={(event) => setUserForm((current) => ({ ...current, temporaryPassword: event.target.value }))} minLength={8} required />
            </div>
            <div className="company-form-field">
              <label htmlFor="companyUserPasswordConfirm">Şifre Tekrar</label>
              <input id="companyUserPasswordConfirm" type="password" value={userForm.confirmPassword} onChange={(event) => setUserForm((current) => ({ ...current, confirmPassword: event.target.value }))} minLength={8} required />
            </div>
          </div>

          {userFormError && <p className="company-details-error" role="alert">{userFormError}</p>}
          {userFormSuccess && <p className="company-details-success" role="status">{userFormSuccess}</p>}

          <button type="submit" className="company-primary-button" disabled={userFormLoading}>
            {userFormLoading ? "Kullanıcı oluşturuluyor..." : "Firma Kullanıcısı Ekle"}
          </button>
        </form>

        <div className="company-details-table-wrap">
          <table>
            <thead>
              <tr>
                <th>Kullanıcı ID</th>
                <th>Ad Soyad</th>
                <th>E-posta</th>
                <th>Hesap Durumu</th>
                <th>Rol</th>
                <th>Oluşturulma Tarihi</th>
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr>
                  <td colSpan={6} className="company-details-placeholder">
                    Firma bilgileri yükleniyor...
                  </td>
                </tr>
              )}

              {!loading && company && company.users.length === 0 && (
                <tr>
                  <td colSpan={6} className="company-details-placeholder">
                    Bu firmaya bağlı kullanıcı bulunmuyor.
                  </td>
                </tr>
              )}

              {!loading && company && company.users.length > 0 && company.users.map((user) => (
                <tr key={user.id}>
                  <td>{user.id}</td>
                  <td>{user.fullName}</td>
                  <td>{user.email}</td>
                  <td>
                    <span className={`company-details-bus-status ${user.isActive === false ? "inactive" : "active"}`}>
                      {user.isActive === false ? "Pasif" : "Aktif"}
                    </span>
                  </td>
                  <td>{user.role}</td>
                  <td>{formatDate(user.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="dashboard-section">
        <h2>Firma Otobüsleri</h2>
        <div className="company-details-table-wrap">
          <table>
            <thead>
              <tr>
                <th>Otobüs ID</th>
                <th>Plaka</th>
                <th>Marka</th>
                <th>Model</th>
                <th>Koltuk Kapasitesi</th>
                <th>Durum</th>
                <th>Kayıt Tarihi</th>
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr>
                  <td colSpan={7} className="company-details-placeholder">
                    Firma bilgileri yükleniyor...
                  </td>
                </tr>
              )}

              {!loading && company && company.buses.length === 0 && (
                <tr>
                  <td colSpan={7} className="company-details-placeholder">
                    Bu firmaya kayıtlı otobüs bulunmuyor.
                  </td>
                </tr>
              )}

              {!loading && company && company.buses.length > 0 && company.buses.map((bus) => (
                <tr key={bus.id}>
                  <td>{bus.id}</td>
                  <td>{bus.plateNumber}</td>
                  <td>{bus.brand || "Belirtilmemiş"}</td>
                  <td>{bus.model || "Belirtilmemiş"}</td>
                  <td>{bus.seatCapacity}</td>
                  <td>
                    <span className={`company-details-bus-status ${bus.isActive ? "active" : "inactive"}`}>
                      {bus.isActive ? "Aktif" : "Pasif"}
                    </span>
                  </td>
                  <td>{formatDate(bus.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

export default CompanyDetails;
