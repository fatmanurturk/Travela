
import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import "../Admin.css";

const API_URL = "http://localhost:5062/api/admin";

type User = {
  id: number;
  fullName: string;
  email: string;
  role: string;
  companyId: number | null;
  companyName: string | null;
  createdAt: string;
};

type Company = {
  id: number;
  name: string;
  isActive: boolean;
};

function UserManagement() {
  const [users, setUsers] = useState<User[]>([]);
  const [companies, setCompanies] = useState<Company[]>([]);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showForm, setShowForm] = useState(false);

  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState("Passenger");
  const [companyId, setCompanyId] = useState("");

  const getToken = () =>
    sessionStorage.getItem("travela_token");

  const loadUsers = async () => {
    setLoading(true);

    try {
      const response = await fetch(`${API_URL}/users`, {
        headers: {
          Authorization: `Bearer ${getToken()}`,
        },
      });

      if (!response.ok) {
        throw new Error(
          `Kullanıcılar alınamadı: ${response.status}`
        );
      }

      const data: User[] = await response.json();
      setUsers(data);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Bir hata oluştu."
      );
    } finally {
      setLoading(false);
    }
  };

  const loadCompanies = async () => {
    try {
      const response = await fetch(`${API_URL}/companies`, {
        headers: {
          Authorization: `Bearer ${getToken()}`,
        },
      });

      if (!response.ok) {
        throw new Error(
          `Firmalar alınamadı: ${response.status}`
        );
      }

      const data: Company[] = await response.json();
      setCompanies(data.filter((company) => company.isActive));
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Firmalar yüklenemedi."
      );
    }
  };

  useEffect(() => {
    void loadUsers();
    void loadCompanies();
  }, []);

  const handleCreateUser = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSaving(true);
    setError("");
    setMessage("");

    try {
      if (role === "Company" && !companyId) {
        throw new Error("Firma rolü için bir firma seçmelisiniz.");
      }

      const response = await fetch(`${API_URL}/users`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${getToken()}`,
        },
        body: JSON.stringify({
          fullName: fullName.trim(),
          email: email.trim(),
          password,
          role,
          companyId: role === "Company" ? Number(companyId) : null,
        }),
      });

      if (!response.ok) {
        if (response.status === 409) {
          throw new Error("Bu e-posta adresi zaten kayıtlı.");
        }

        const data = await response.json().catch(() => null);

        throw new Error(
          data?.message || `Kullanıcı oluşturulamadı: ${response.status}`
        );
      }

      setFullName("");
      setEmail("");
      setPassword("");
      setRole("Passenger");
      setCompanyId("");
      setShowForm(false);

      await loadUsers();
      setMessage("Kullanıcı başarıyla oluşturuldu.");
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Kullanıcı kaydedilemedi."
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading">
        <h1>Kullanıcı Yönetimi</h1>
        <p>
          Travela kullanıcılarını, rollerini ve firma ilişkilerini yönetin.
        </p>
      </div>

      <div style={{ marginBottom: 20 }}>
        <button
          type="button"
          onClick={() => {
            setShowForm(!showForm);
            setError("");
            setMessage("");
          }}
        >
          {showForm ? "Formu Kapat" : "+ Yeni Kullanıcı Ekle"}
        </button>
      </div>

      {error && (
        <p role="alert" style={{ color: "#dc2626" }}>
          {error}
        </p>
      )}

      {message && (
        <p role="status" style={{ color: "#16a34a" }}>
          {message}
        </p>
      )}

      {showForm && (
        <section
          className="dashboard-section"
          style={{ marginBottom: 24 }}
        >
          <h2>Yeni Kullanıcı Oluştur</h2>

          <form onSubmit={handleCreateUser}>
            <div style={{ marginBottom: 12 }}>
              <label htmlFor="user-name">Ad Soyad *</label>
              <br />
              <input
                id="user-name"
                type="text"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                required
                maxLength={100}
              />
            </div>

            <div style={{ marginBottom: 12 }}>
              <label htmlFor="user-email">E-posta *</label>
              <br />
              <input
                id="user-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>

            <div style={{ marginBottom: 12 }}>
              <label htmlFor="user-password">Şifre *</label>
              <br />
              <input
                id="user-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={8}
                autoComplete="new-password"
              />
            </div>

            <div style={{ marginBottom: 12 }}>
              <label htmlFor="user-role">Rol *</label>
              <br />
              <select
                id="user-role"
                value={role}
                onChange={(e) => {
                  setRole(e.target.value);
                  setCompanyId("");
                }}
              >
                <option value="Passenger">Yolcu</option>
                <option value="Company">Firma</option>
                <option value="Admin">Yönetici</option>
              </select>
            </div>

            {role === "Company" && (
              <div style={{ marginBottom: 12 }}>
                <label htmlFor="user-company">Firma *</label>
                <br />
                <select
                  id="user-company"
                  value={companyId}
                  onChange={(e) => setCompanyId(e.target.value)}
                  required
                >
                  <option value="">Firma seçiniz</option>

                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>
                      {company.name}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <button type="submit" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kullanıcıyı Kaydet"}
            </button>
          </form>
        </section>
      )}

      <section className="dashboard-section">
        <h2>Kullanıcı Listesi</h2>

        {loading && <p>Kullanıcılar yükleniyor...</p>}

        {!loading && users.length === 0 && !error && (
          <p>Henüz kayıtlı kullanıcı bulunmuyor.</p>
        )}

        {!loading && users.length > 0 && (
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
                  <th>Ad Soyad</th>
                  <th>E-posta</th>
                  <th>Rol</th>
                  <th>Firma</th>
                  <th>Kayıt Tarihi</th>
                </tr>
              </thead>

              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>{user.fullName}</td>
                    <td>{user.email}</td>
                    <td>
                      {user.role === "Admin"
                        ? "Yönetici"
                        : user.role === "Company"
                        ? "Firma"
                        : "Yolcu"}
                    </td>
                    <td>{user.companyName || "—"}</td>
                    <td>
                      {new Date(
                        user.createdAt
                      ).toLocaleDateString("tr-TR")}
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

export default UserManagement;
