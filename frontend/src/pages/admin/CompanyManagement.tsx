
import { useEffect, useState } from "react";
import type { FormEvent } from "react";

const API_URL = "http://localhost:5062/api/admin/companies";

type Company = {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
  createdAt: string;
  isActive: boolean;
};

function CompanyManagement() {
  // Firma listesi
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);

  // Bildirimler
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  // Form durumları
  const [showForm, setShowForm] = useState(false);
  const [saving, setSaving] = useState(false);

  // Düzenlenen firma
  const [editingCompany, setEditingCompany] =
    useState<Company | null>(null);

  // Form alanları
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [address, setAddress] = useState("");
  const [isActive, setIsActive] = useState(true);

  // Formu temizle
  const resetForm = () => {
    setName("");
    setEmail("");
    setPhone("");
    setAddress("");
    setEditingCompany(null);
    setIsActive(true);
  };

  // Firmaları getir
  const loadCompanies = async () => {
    setLoading(true);
    setError("");

    try {
      const token = sessionStorage.getItem("travela_token");

      const response = await fetch(API_URL, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        throw new Error(
          `Firmalar alınamadı: ${response.status}`
        );
      }

      const data: Company[] = await response.json();

      setCompanies(data);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Firmalar yüklenirken hata oluştu."
      );
    } finally {
      setLoading(false);
    }
  };

  // Sayfa açıldığında firmaları getir
  useEffect(() => {
    void loadCompanies();
  }, []);

  // Yeni firma formunu aç / kapat
  const handleNewCompany = () => {
    if (saving) return;

    if (showForm && !editingCompany) {
      setShowForm(false);
    } else {
      resetForm();
      setShowForm(true);
    }

    setError("");
    setMessage("");
  };

  // Firma düzenleme formunu aç
  const handleEditCompany = (company: Company) => {
    if (saving) return;

    setEditingCompany(company);

    setName(company.name);
    setEmail(company.email || "");
    setPhone(company.phone || "");
    setAddress(company.address || "");

    setShowForm(true);
    setError("");
    setMessage("");
  };

  // Formu kapat
  const handleCloseForm = () => {
    if (saving) return;

    resetForm();
    setShowForm(false);
    setError("");
  };

  // Yeni firma oluştur
  const handleCreateCompany = async (
    e: FormEvent<HTMLFormElement>
  ) => {
    e.preventDefault();

    if (saving) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const token = sessionStorage.getItem("travela_token");

      const response = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
       body: JSON.stringify({
  name: name.trim(),
  email: email.trim() || null,
  phone: phone.trim() || null,
  address: address.trim() || null,
  isActive: isActive,
}),
      });

      if (!response.ok) {
        if (response.status === 409) {
          throw new Error(
            "Bu isimde bir firma zaten kayıtlı."
          );
        }

        throw new Error(
          `Firma oluşturulamadı: ${response.status}`
        );
      }

      resetForm();
      setShowForm(false);

      await loadCompanies();

      setMessage("Firma başarıyla oluşturuldu.");
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Firma oluşturulurken hata oluştu."
      );
    } finally {
      setSaving(false);
    }
  };

  // Mevcut firmayı güncelle
  const handleUpdateCompany = async (
    e: FormEvent<HTMLFormElement>
  ) => {
    e.preventDefault();

    if (!editingCompany || saving) return;

    setSaving(true);
    setError("");
    setMessage("");

    try {
      const token = sessionStorage.getItem("travela_token");

      const response = await fetch(
        `${API_URL}/${editingCompany.id}`,
        {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${token}`,
          },
          body: JSON.stringify({
            name: name.trim(),
            email: email.trim() || null,
            phone: phone.trim() || null,
            address: address.trim() || null,
            isActive: isActive,
          }),
        }
      );

      if (!response.ok) {
        if (response.status === 409) {
          throw new Error(
            "Bu isimde başka bir firma zaten kayıtlı."
          );
        }

        const data = await response
          .json()
          .catch(() => null);

        throw new Error(
          data?.message ||
            `Firma güncellenemedi: ${response.status}`
        );
      }

      resetForm();
      setShowForm(false);

      await loadCompanies();

      setMessage(
        "Firma bilgileri başarıyla güncellendi."
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Firma güncellenirken hata oluştu."
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="admin-dashboard">

      {/* Sayfa başlığı */}
      <div className="dashboard-heading">
        <h1>Firma Yönetimi</h1>
        <p>
          Travela sistemine kayıtlı otobüs firmalarını
          yönetin.
        </p>
      </div>

      {/* Yeni firma butonu */}
      <div style={{ marginBottom: 20 }}>
        <button
          type="button"
          onClick={handleNewCompany}
          disabled={saving}
        >
          {showForm && !editingCompany
            ? "Formu Kapat"
            : "+ Yeni Firma Ekle"}
        </button>
      </div>

      {/* Hata mesajı */}
      {error && (
        <p
          role="alert"
          style={{ color: "#dc2626" }}
        >
          {error}
        </p>
      )}

      {/* Başarı mesajı */}
      {message && (
        <p
          role="status"
          style={{ color: "#16a34a" }}
        >
          {message}
        </p>
      )}

      {/* Firma ekleme / düzenleme formu */}
      {showForm && (
        <section
          className="dashboard-section"
          style={{ marginBottom: 24 }}
        >
          <h2>
            {editingCompany
              ? "Firma Bilgilerini Düzenle"
              : "Yeni Firma Oluştur"}
          </h2>

          <form
            onSubmit={
              editingCompany
                ? handleUpdateCompany
                : handleCreateCompany
            }
          >

            {/* Firma adı */}
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="company-name">
                Firma Adı *
              </label>
              <br />
              <input
                id="company-name"
                type="text"
                value={name}
                onChange={(e) =>
                  setName(e.target.value)
                }
                required
                maxLength={150}
                disabled={saving}
              />
            </div>

            {/* E-posta */}
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="company-email">
                E-posta
              </label>
              <br />
              <input
                id="company-email"
                type="email"
                value={email}
                onChange={(e) =>
                  setEmail(e.target.value)
                }
                disabled={saving}
              />
            </div>

            {/* Telefon */}
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="company-phone">
                Telefon
              </label>
              <br />
              <input
                id="company-phone"
                type="tel"
                value={phone}
                onChange={(e) =>
                  setPhone(e.target.value)
                }
                disabled={saving}
              />
            </div>

            {/* Adres */}
            <div style={{ marginBottom: 16 }}>
              <label htmlFor="company-address">
                Adres
              </label>
              <br />
              <textarea
                id="company-address"
                value={address}
                onChange={(e) =>
                  setAddress(e.target.value)
                }
                rows={3}
                disabled={saving}
              />
            </div>

{/* Firma durumu - yalnızca düzenleme sırasında */}
{editingCompany && (
  <div style={{ marginBottom: 16 }}>
    <label htmlFor="company-status">
      Firma Durumu
    </label>

    <select
      id="company-status"
      value={isActive ? "active" : "passive"}
      onChange={(e) =>
        setIsActive(e.target.value === "active")
      }
      disabled={saving}
      style={{
        display: "block",
        width: "100%",
        padding: "12px",
        marginTop: "6px",
        borderRadius: "8px",
        border: "1px solid #d1d5db",
        boxSizing: "border-box",
      }}
    >
      <option value="active">Aktif</option>
      <option value="passive">Pasif</option>
    </select>
  </div>
)}
            {/* Form butonları */}
            <div
              style={{
                display: "flex",
                gap: 10,
                flexWrap: "wrap",
              }}
            >
              <button
                type="submit"
                disabled={saving}
              >
                {saving
                  ? "Kaydediliyor..."
                  : editingCompany
                    ? "Değişiklikleri Kaydet"
                    : "Firmayı Kaydet"}
              </button>

              <button
                type="button"
                onClick={handleCloseForm}
                disabled={saving}
              >
                İptal
              </button>
            </div>
          </form>
        </section>
      )}

      {/* Firma listesi */}
      <section className="dashboard-section">

        <h2>Firma Listesi</h2>

        <p>
          Toplam Firma: {companies.length}
        </p>

        {loading && (
          <p>Firmalar yükleniyor...</p>
        )}

        {!loading &&
          !error &&
          companies.length === 0 && (
            <p>
              Henüz kayıtlı firma bulunmuyor.
            </p>
          )}

        {!loading && companies.length > 0 && (
          <div style={{ overflowX: "auto" }}>
            <table
              style={{
                width: "100%",
                borderCollapse: "collapse",
                textAlign: "left",
              }}
            >
              <thead>
                <tr>
                  <th>Firma Adı</th>
                  <th>E-posta</th>
                  <th>Telefon</th>
                  <th>Durum</th>
                  <th>Kayıt Tarihi</th>
                  <th>İşlemler</th>
                </tr>
              </thead>

              <tbody>
                {companies.map((company) => (
                  <tr key={company.id}>

                    <td>{company.name}</td>

                    <td>
                      {company.email || "—"}
                    </td>

                    <td>
                      {company.phone || "—"}
                    </td>

                    <td>
                      <span
                        style={{
                          color: company.isActive
                            ? "#16a34a"
                            : "#dc2626",
                          fontWeight: 600,
                        }}
                      >
                        {company.isActive
                          ? "Aktif"
                          : "Pasif"}
                      </span>
                    </td>

                    <td>
                      {new Date(
                        company.createdAt
                      ).toLocaleDateString("tr-TR")}
                    </td>

                    {/* Firma işlemleri */}
                    <td>
                      <button
                        type="button"
                        onClick={() =>
                          handleEditCompany(company)
                        }
                        disabled={saving}
                      >
                        Düzenle
                      </button>
                    </td>

                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      
    </div>
  );
}

export default CompanyManagement;
