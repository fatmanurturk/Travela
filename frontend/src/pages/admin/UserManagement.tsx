import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import "../Admin.css";

const API_URL = "http://localhost:5062/api/admin";

type UserRow = {
  id: number;
  fullName: string;
  email: string;
  role: string;
  companyId: number | null;
  companyName: string | null;
  isActive: boolean;
  createdAt: string;
  lastLoginAt?: string | null;
  updatedAt?: string | null;
};

type UserStats = {
  totalUsers: number;
  activeUsers: number;
  passiveUsers: number;
  companyUsers: number;
};

type Company = {
  id: number;
  name: string;
  isActive: boolean;
};

type ListResponse = {
  items: UserRow[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
};

function UserManagement() {
  const navigate = useNavigate();
  const [users, setUsers] = useState<UserRow[]>([]);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [stats, setStats] = useState<UserStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [selectedUser, setSelectedUser] = useState<UserRow | null>(null);
  const [editingUser, setEditingUser] = useState<UserRow | null>(null);
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("Tümü");
  const [companyFilter, setCompanyFilter] = useState("Tümü");
  const [statusFilter, setStatusFilter] = useState("Tümü");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(10);
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortDirection, setSortDirection] = useState("desc");
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");

  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState("Passenger");
  const [companyId, setCompanyId] = useState("");

  const getToken = () => sessionStorage.getItem("travela_token");
  const handleUnauthorized = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  const addToken = (headers: HeadersInit = {}) => ({
    ...headers,
    Authorization: getToken() ? `Bearer ${getToken()}` : "",
  }) as HeadersInit;

  const loadCompanies = async () => {
    try {
      const response = await fetch(`${API_URL}/companies`, {
        headers: addToken(),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!response.ok) {
        throw new Error(`Firmalar alınamadı: ${response.status}`);
      }

      const data: Company[] = await response.json();
      setCompanies(data.filter((company) => company.isActive));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Firmalar yüklenemedi.");
    }
  };

  const loadStats = async () => {
    try {
      const response = await fetch(`${API_URL}/dashboard`, {
        headers: addToken(),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!response.ok) {
        throw new Error(`İstatistikler alınamadı: ${response.status}`);
      }

      const data: UserStats = await response.json();
      setStats(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "İstatistikler yüklenemedi.");
    }
  };

  const loadUsers = async () => {
    setLoading(true);

    try {
      const params = new URLSearchParams({
        search,
        role: roleFilter === "Tümü" ? "" : roleFilter,
        status: statusFilter === "Tümü" ? "" : statusFilter,
        sortBy,
        sortDirection,
        pageNumber: String(pageNumber),
        pageSize: String(pageSize),
      });

      if (companyFilter !== "Tümü") {
        params.set("companyId", companyFilter);
      }

      if (startDate) {
        params.set("startDate", startDate);
      }

      if (endDate) {
        params.set("endDate", endDate);
      }

      const response = await fetch(`${API_URL}/users?${params.toString()}`, {
        headers: addToken(),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }

      if (!response.ok) {
        throw new Error(`Kullanıcılar alınamadı: ${response.status}`);
      }

      const data: ListResponse = await response.json();
      setUsers(data.items);
      setTotalCount(data.totalCount);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Bir hata oluştu.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadCompanies();
    void loadStats();
  }, []);

  useEffect(() => {
    void loadUsers();
  }, [search, roleFilter, companyFilter, statusFilter, startDate, endDate, pageNumber, sortBy, sortDirection]);

  const statsCards = useMemo(
    () => [
      { title: "Toplam Kullanıcı", value: stats?.totalUsers ?? 0 },
      { title: "Aktif Kullanıcı", value: stats?.activeUsers ?? 0 },
      { title: "Pasif Kullanıcı", value: stats?.passiveUsers ?? 0 },
      { title: "Firma Yetkilisi Sayısı", value: stats?.companyUsers ?? 0 },
    ],
    [stats]
  );

  const resetFilters = () => {
    setSearch("");
    setRoleFilter("Tümü");
    setCompanyFilter("Tümü");
    setStatusFilter("Tümü");
    setStartDate("");
    setEndDate("");
    setSortBy("createdAt");
    setSortDirection("desc");
    setPageNumber(1);
  };

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
          ...addToken(),
        },
        body: JSON.stringify({
          fullName: fullName.trim(),
          email: email.trim(),
          password: password.trim() || undefined,
          role,
          companyId: role === "Company" ? Number(companyId) : null,
          isActive: false,
        }),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(data?.message || `Kullanıcı oluşturulamadı: ${response.status}`);
      }

      setFullName("");
      setEmail("");
      setPassword("");
      setRole("Passenger");
      setCompanyId("");
      setShowForm(false);
      setMessage(
        data?.activationToken
          ? `Kullanıcı oluşturuldu. Aktivasyon tokenı: ${data.activationToken}`
          : "Kullanıcı başarıyla oluşturuldu."
      );
      await loadUsers();
      await loadStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kullanıcı kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  };

  const toggleStatus = async (user: UserRow) => {
    const nextStatus = !user.isActive;

    try {
      const response = await fetch(`${API_URL}/users/${user.id}/status`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          ...addToken(),
        },
        body: JSON.stringify({ isActive: nextStatus }),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(data?.message || `Hesap durumu güncellenemedi: ${response.status}`);
      }

      setMessage(data?.message || "Hesap durumu güncellendi.");
      await loadUsers();
      await loadStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem başarısız.");
    }
  };

  const startPasswordReset = async (user: UserRow) => {
    try {
      const response = await fetch(`${API_URL}/users/${user.id}/password-reset`, {
        method: "POST",
        headers: addToken(),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(data?.message || `Şifre sıfırlama başlatılamadı: ${response.status}`);
      }

      setMessage(`Şifre sıfırlama tokenı: ${data.resetToken}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Şifre sıfırlama başlatılamadı.");
    }
  };

  const saveUserEdit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!editingUser) return;

    try {
      const response = await fetch(`${API_URL}/users/${editingUser.id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          ...addToken(),
        },
        body: JSON.stringify({
          fullName: editingUser.fullName,
          email: editingUser.email,
          role: editingUser.role,
          companyId: editingUser.role === "Company" ? editingUser.companyId : null,
          isActive: editingUser.isActive,
        }),
      });

      const data = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(data?.message || `Kullanıcı güncellenemedi: ${response.status}`);
      }

      setEditingUser(null);
      setMessage(data?.message || "Kullanıcı güncellendi.");
      await loadUsers();
      await loadStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Kullanıcı güncellenemedi.");
    }
  };

  const handleSort = (field: string) => {
    if (sortBy === field) {
      setSortDirection((current) => (current === "asc" ? "desc" : "asc"));
      return;
    }

    setSortBy(field);
    setSortDirection(field === "name" ? "asc" : "desc");
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading">
        <h1>Kullanıcı Yönetimi</h1>
        <p>Travela kullanıcılarını, rollerini ve güvenlik durumlarını yönetin.</p>
      </div>

      <div className="dashboard-stats">
        {statsCards.map((stat) => (
          <div className="stat-card" key={stat.title}>
            <p>{stat.title}</p>
            <h2>{loading ? "Yükleniyor..." : stat.value}</h2>
          </div>
        ))}
      </div>

      <div className="toolbar-row">
        <button type="button" className="primary-button" onClick={() => setShowForm((current) => !current)}>
          {showForm ? "Formu Kapat" : "Yeni Kullanıcı Ekle"}
        </button>
      </div>

      {error && <p className="form-alert error">{error}</p>}
      {message && <p className="form-alert success">{message}</p>}

      {showForm && (
        <section className="dashboard-section form-section">
          <h2>Yeni Kullanıcı Oluştur</h2>
          <form onSubmit={handleCreateUser} className="user-form">
            <div className="form-grid">
              <label>
                <span>Ad Soyad *</span>
                <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
              </label>
              <label>
                <span>E-posta *</span>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
              </label>
              <label>
                <span>Şifre (opsiyonel)</span>
                <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} minLength={8} />
              </label>
              <label>
                <span>Kullanıcı Rolü *</span>
                <select value={role} onChange={(e) => { setRole(e.target.value); setCompanyId(""); }}>
                  <option value="Passenger">Passenger</option>
                  <option value="Company">Company</option>
                  <option value="Admin">Admin</option>
                </select>
              </label>
              {role === "Company" && (
                <label>
                  <span>Firma *</span>
                  <select value={companyId} onChange={(e) => setCompanyId(e.target.value)} required>
                    <option value="">Firma seçin</option>
                    {companies.map((company) => (
                      <option key={company.id} value={company.id}>{company.name}</option>
                    ))}
                  </select>
                </label>
              )}
            </div>
            <button type="submit" className="primary-button" disabled={saving}>
              {saving ? "Kaydediliyor..." : "Kullanıcı Oluştur"}
            </button>
          </form>
        </section>
      )}

      <section className="dashboard-section table-section">
        <div className="filter-header">
          <div className="filters-grid">
            <label>
              <span>Arama</span>
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ad veya e-posta" />
            </label>
            <label>
              <span>Rol</span>
              <select value={roleFilter} onChange={(e) => { setRoleFilter(e.target.value); setPageNumber(1); }}>
                <option value="Tümü">Tümü</option>
                <option value="Admin">Admin</option>
                <option value="Company">Company</option>
                <option value="Passenger">Passenger</option>
              </select>
            </label>
            <label>
              <span>Firma</span>
              <select value={companyFilter} onChange={(e) => { setCompanyFilter(e.target.value); setPageNumber(1); }}>
                <option value="Tümü">Tümü</option>
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>{company.name}</option>
                ))}
              </select>
            </label>
            <label>
              <span>Hesap Durumu</span>
              <select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPageNumber(1); }}>
                <option value="Tümü">Tümü</option>
                <option value="Aktif">Aktif</option>
                <option value="Pasif">Pasif</option>
              </select>
            </label>
            <label>
              <span>Başlangıç</span>
              <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
            </label>
            <label>
              <span>Bitiş</span>
              <input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
            </label>
          </div>
          <div className="filter-actions">
            <button type="button" className="secondary-button" onClick={resetFilters}>Filtreleri Temizle</button>
            <span className="result-count">Toplam Sonuç: {totalCount}</span>
          </div>
        </div>

        {loading ? (
          <p>Kullanıcılar yükleniyor...</p>
        ) : users.length === 0 ? (
          <div className="empty-state">Kayıtlı kullanıcı bulunmuyor.</div>
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th onClick={() => handleSort("name")}>Ad Soyad</th>
                    <th onClick={() => handleSort("email")}>E-posta</th>
                    <th>Kullanıcı Rolü</th>
                    <th>Bağlı Firma</th>
                    <th>Hesap Durumu</th>
                    <th onClick={() => handleSort("createdAt")}>Kayıt Tarihi</th>
                    <th>İşlemler</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => (
                    <tr key={user.id}>
                      <td>{user.fullName}</td>
                      <td>{user.email}</td>
                      <td>{user.role}</td>
                      <td>{user.companyName || "—"}</td>
                      <td>
                        <span className={`status-badge ${user.isActive ? "active" : "inactive"}`}>
                          {user.isActive ? "Aktif" : "Pasif"}
                        </span>
                      </td>
                      <td>{new Date(user.createdAt).toLocaleDateString("tr-TR")}</td>
                      <td>
                        <div className="action-group">
                          <button type="button" className="link-button" onClick={() => setSelectedUser(user)}>Detay</button>
                          <button type="button" className="link-button" onClick={() => setEditingUser(user)}>Düzenle</button>
                          <button type="button" className="link-button" onClick={() => toggleStatus(user)}>
                            {user.isActive ? "Pasifleştir" : "Aktifleştir"}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="pagination-row">
              <button type="button" disabled={pageNumber === 1} onClick={() => setPageNumber((value) => Math.max(1, value - 1))}>Önceki</button>
              <span>{pageNumber} / {totalPages}</span>
              <button type="button" disabled={pageNumber >= totalPages} onClick={() => setPageNumber((value) => value + 1)}>Sonraki</button>
            </div>
          </>
        )}
      </section>

      {selectedUser && (
        <div className="modal-backdrop" onClick={() => setSelectedUser(null)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>Kullanıcı Detayı</h3>
            <div className="detail-grid">
              <div><strong>Kullanıcı ID</strong><span>{selectedUser.id}</span></div>
              <div><strong>Ad Soyad</strong><span>{selectedUser.fullName}</span></div>
              <div><strong>E-posta</strong><span>{selectedUser.email}</span></div>
              <div><strong>Rol</strong><span>{selectedUser.role}</span></div>
              <div><strong>Bağlı Firma</strong><span>{selectedUser.companyName || "—"}</span></div>
              <div><strong>Hesap Durumu</strong><span>{selectedUser.isActive ? "Aktif" : "Pasif"}</span></div>
              <div><strong>Kayıt Tarihi</strong><span>{new Date(selectedUser.createdAt).toLocaleString("tr-TR")}</span></div>
              <div><strong>Son Giriş</strong><span>{selectedUser.lastLoginAt ? new Date(selectedUser.lastLoginAt).toLocaleString("tr-TR") : "Veri yok"}</span></div>
            </div>
            <div className="detail-actions">
              <button type="button" className="primary-button" onClick={() => { setSelectedUser(null); setEditingUser(selectedUser); }}>Düzenle</button>
              <button type="button" className="secondary-button" onClick={() => toggleStatus(selectedUser)}>Aktifleştir / Pasifleştir</button>
              <button type="button" className="secondary-button" onClick={() => startPasswordReset(selectedUser)}>Şifre Sıfırlama</button>
            </div>
          </div>
        </div>
      )}

      {editingUser && (
        <div className="modal-backdrop" onClick={() => setEditingUser(null)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>Kullanıcı Düzenle</h3>
            <form onSubmit={saveUserEdit} className="user-form">
              <div className="form-grid">
                <label>
                  <span>Ad Soyad *</span>
                  <input value={editingUser.fullName} onChange={(e) => setEditingUser({ ...editingUser, fullName: e.target.value })} required />
                </label>
                <label>
                  <span>E-posta *</span>
                  <input type="email" value={editingUser.email} onChange={(e) => setEditingUser({ ...editingUser, email: e.target.value })} required />
                </label>
                <label>
                  <span>Kullanıcı Rolü *</span>
                  <select value={editingUser.role} onChange={(e) => setEditingUser({ ...editingUser, role: e.target.value, companyId: e.target.value === "Company" ? editingUser.companyId : null })}>
                    <option value="Passenger">Passenger</option>
                    <option value="Company">Company</option>
                    <option value="Admin">Admin</option>
                  </select>
                </label>
                {editingUser.role === "Company" && (
                  <label>
                    <span>Firma</span>
                    <select value={editingUser.companyId ?? ""} onChange={(e) => setEditingUser({ ...editingUser, companyId: e.target.value ? Number(e.target.value) : null })}>
                      <option value="">Firma seçin</option>
                      {companies.map((company) => (
                        <option key={company.id} value={company.id}>{company.name}</option>
                      ))}
                    </select>
                  </label>
                )}
                <label>
                  <span>Hesap Durumu</span>
                  <select value={editingUser.isActive ? "Aktif" : "Pasif"} onChange={(e) => setEditingUser({ ...editingUser, isActive: e.target.value === "Aktif" })}>
                    <option value="Aktif">Aktif</option>
                    <option value="Pasif">Pasif</option>
                  </select>
                </label>
              </div>
              <div className="detail-actions">
                <button type="submit" className="primary-button">Kaydet</button>
                <button type="button" className="secondary-button" onClick={() => setEditingUser(null)}>İptal</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}

export default UserManagement;