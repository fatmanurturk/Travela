import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import "../Admin.css";

const API_URL = "http://localhost:5062/api/buses";
const ADMIN_API_URL = "http://localhost:5062/api/admin";
const PAGE_SIZE = 10;

type Bus = {
  id: number;
  companyId: number;
  companyName: string;
  plateNumber: string;
  brand: string | null;
  model: string | null;
  seatCapacity: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
};

type BusSummary = {
  totalBuses: number;
  activeBuses: number;
  inactiveBuses: number;
  companiesWithBuses: number;
};

type BusTrip = {
  id: number;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  status: string;
  availableSeats: number;
};

type BusDetail = Bus & {
  companyIsActive: boolean;
  totalTrips: number;
  upcomingTripsCount: number;
  completedTripsCount: number;
  cancelledTripsCount: number;
  upcomingTrips: BusTrip[];
};

type Company = {
  id: number;
  name: string;
  isActive: boolean;
};

type BusListResponse = {
  items: Bus[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
};

type BusFormState = {
  companyId: string;
  plateNumber: string;
  brand: string;
  model: string;
  seatCapacity: string;
  isActive: boolean;
};

const emptyForm: BusFormState = {
  companyId: "",
  plateNumber: "",
  brand: "",
  model: "",
  seatCapacity: "46",
  isActive: true,
};

type SessionUser = {
  role?: string;
  companyName?: string;
};

function getSessionUser(): SessionUser | null {
  try {
    return JSON.parse(sessionStorage.getItem("travela_user") || "null") as SessionUser | null;
  } catch {
    return null;
  }
}

function BusManagement() {
  const navigate = useNavigate();
  const currentUser = getSessionUser();
  const isAdmin = currentUser?.role === "Admin";
  const [buses, setBuses] = useState<Bus[]>([]);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [summary, setSummary] = useState<BusSummary | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [pageNumber, setPageNumber] = useState(1);
  const [search, setSearch] = useState("");
  const [companyFilter, setCompanyFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [brandFilter, setBrandFilter] = useState("");
  const [minCapacity, setMinCapacity] = useState("");
  const [maxCapacity, setMaxCapacity] = useState("");
  const [sortBy, setSortBy] = useState("plate");
  const [sortDirection, setSortDirection] = useState("asc");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [form, setForm] = useState<BusFormState>(emptyForm);
  const [showForm, setShowForm] = useState(false);
  const [editingBus, setEditingBus] = useState<Bus | null>(null);
  const [details, setDetails] = useState<BusDetail | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);

  const getHeaders = (json = false): HeadersInit => {
    const token = sessionStorage.getItem("travela_token");
    return {
      ...(json ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    };
  };

  const handleUnauthorized = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  const readError = async (response: Response, fallback: string) => {
    const data = (await response.json().catch(() => null)) as
      | { message?: string }
      | null;
    return data?.message || `${fallback} (${response.status})`;
  };

  const loadBuses = async () => {
    setLoading(true);
    setError("");

    try {
      const params = new URLSearchParams({
        pageNumber: String(pageNumber),
        pageSize: String(PAGE_SIZE),
        sortBy,
        sortDirection,
      });

      if (search.trim()) params.set("search", search.trim());
      if (isAdmin && companyFilter) params.set("companyId", companyFilter);
      if (statusFilter) params.set("isActive", String(statusFilter === "active"));
      if (brandFilter.trim()) params.set("brand", brandFilter.trim());
      if (minCapacity) params.set("minCapacity", minCapacity);
      if (maxCapacity) params.set("maxCapacity", maxCapacity);

      const response = await fetch(`${API_URL}?${params.toString()}`, {
        headers: getHeaders(),
      });

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Otobüsler alınamadı"));
      }

      const data: BusListResponse = await response.json();
      setBuses(data.items);
      setTotalCount(data.totalCount);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Otobüsler yüklenemedi.");
    } finally {
      setLoading(false);
    }
  };

  const loadSummary = async () => {
    try {
      const response = await fetch(`${API_URL}/summary`, {
        headers: getHeaders(),
      });
      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Otobüs istatistikleri alınamadı"));
      }
      setSummary(await response.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : "İstatistikler yüklenemedi.");
    }
  };

  const loadCompanies = async () => {
    try {
      const url = isAdmin
        ? `${ADMIN_API_URL}/companies`
        : "http://localhost:5062/api/company/profile";

      const response = await fetch(url, {
        headers: getHeaders(),
      });
      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Firmalar alınamadı"));
      }
      const data = await response.json();
      setCompanies(isAdmin ? data as Company[] : [data as Company]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Firmalar yüklenemedi.");
    }
  };

  useEffect(() => {
    void loadCompanies();
  }, []);

  useEffect(() => {
    void loadSummary();
  }, []);

  useEffect(() => {
    void loadBuses();
  }, [
    pageNumber,
    search,
    companyFilter,
    statusFilter,
    brandFilter,
    minCapacity,
    maxCapacity,
    sortBy,
    sortDirection,
  ]);

  const startCreate = () => {
    setShowForm(true);
    setEditingBus(null);
    setForm({
      ...emptyForm,
    });
    setError("");
    setMessage("");
  };

  const startEdit = (bus: Bus) => {
    setShowForm(true);
    setEditingBus(bus);
    setForm({
      companyId: String(bus.companyId),
      plateNumber: bus.plateNumber,
      brand: bus.brand ?? "",
      model: bus.model ?? "",
      seatCapacity: String(bus.seatCapacity),
      isActive: bus.isActive,
    });
    setError("");
    setMessage("");
  };

  const openDetails = async (busId: number) => {
    setDetailsLoading(true);
    setDetails(null);
    setError("");
    try {
      const response = await fetch(`${API_URL}/${busId}`, {
        headers: getHeaders(),
      });
      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Otobüs detayları alınamadı"));
      }
      setDetails(await response.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Detaylar yüklenemedi.");
    } finally {
      setDetailsLoading(false);
    }
  };

  const saveBus = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    setError("");
    setMessage("");

    try {
      const response = await fetch(
        editingBus ? `${API_URL}/${editingBus.id}` : API_URL,
        {
          method: editingBus ? "PUT" : "POST",
          headers: getHeaders(true),
          body: JSON.stringify({
            companyId: isAdmin ? Number(form.companyId) : null,
            plateNumber: form.plateNumber,
            brand: form.brand,
            model: form.model,
            seatCapacity: Number(form.seatCapacity),
            isActive: form.isActive,
          }),
        }
      );

      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Otobüs kaydedilemedi"));
      }

      const data = (await response.json().catch(() => null)) as
        | { message?: string }
        | null;
      setEditingBus(null);
      setShowForm(false);
      setMessage(data?.message || "Otobüs bilgileri kaydedildi.");
      await Promise.all([loadBuses(), loadSummary()]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Otobüs kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  };

  const toggleStatus = async (bus: Bus) => {
    const nextStatus = !bus.isActive;
    if (
      !window.confirm(
        nextStatus
          ? "Bu otobüsü aktifleştirmek istediğinize emin misiniz?"
          : "Bu otobüsü pasifleştirmek istediğinize emin misiniz? Yaklaşan seferler varsa işlem engellenir."
      )
    ) {
      return;
    }

    setError("");
    setMessage("");
    try {
      const response = await fetch(`${API_URL}/${bus.id}/status`, {
        method: "PATCH",
        headers: getHeaders(true),
        body: JSON.stringify({ isActive: nextStatus }),
      });
      if (response.status === 401) {
        handleUnauthorized();
        return;
      }
      if (!response.ok) {
        throw new Error(await readError(response, "Durum güncellenemedi"));
      }
      const data = (await response.json().catch(() => null)) as
        | { message?: string }
        | null;
      setMessage(data?.message || "Otobüs durumu güncellendi.");
      await Promise.all([loadBuses(), loadSummary()]);
      if (details?.id === bus.id) {
        await openDetails(bus.id);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Durum güncellenemedi.");
    }
  };

  const clearFilters = () => {
    setSearch("");
    setCompanyFilter("");
    setStatusFilter("");
    setBrandFilter("");
    setMinCapacity("");
    setMaxCapacity("");
    setSortBy("plate");
    setSortDirection("asc");
    setPageNumber(1);
  };

  const changeSort = (nextSort: string) => {
    if (sortBy === nextSort) {
      setSortDirection((current) => (current === "asc" ? "desc" : "asc"));
    } else {
      setSortBy(nextSort);
      setSortDirection("asc");
    }
    setPageNumber(1);
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const activeForm = showForm;

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading bus-page-heading">
        <div>
          <h1>Otobüs Yönetimi</h1>
          <p>Firmalara ait otobüsleri görüntüleyin ve yönetin.</p>
        </div>
        <button type="button" className="primary-button" onClick={startCreate}>
          + Yeni Otobüs Ekle
        </button>
      </div>

      <div className="dashboard-stats bus-stats">
        {[
          ["Toplam Otobüs", summary?.totalBuses],
          ["Aktif Otobüs", summary?.activeBuses],
          ["Pasif Otobüs", summary?.inactiveBuses],
          ["Otobüsü Bulunan Firma", summary?.companiesWithBuses],
        ].map(([title, value]) => (
          <div className="stat-card" key={title}>
            <p>{title}</p>
            <h2>{value ?? "—"}</h2>
          </div>
        ))}
      </div>

      {error && <p className="form-alert error" role="alert">{error}</p>}
      {message && <p className="form-alert success" role="status">{message}</p>}

      {activeForm && (
        <section className="dashboard-section form-section">
          <h2>{editingBus ? "Otobüsü Düzenle" : "Yeni Otobüs Ekle"}</h2>
          <form className="user-form" onSubmit={saveBus}>
            <div className="form-grid">
              <label>
                Plaka *
                <input
                  required
                  maxLength={40}
                  value={form.plateNumber}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, plateNumber: event.target.value }))
                  }
                  placeholder="34 ABC 123"
                />
              </label>
              {isAdmin ? (
                <label>
                  Firma *
                  <select
                    required
                    value={form.companyId}
                    onChange={(event) =>
                      setForm((current) => ({ ...current, companyId: event.target.value }))
                    }
                  >
                    <option value="">Firma seçin</option>
                    {companies.filter((company) => company.isActive || company.id === editingBus?.companyId).map((company) => (
                      <option key={company.id} value={company.id}>{company.name}</option>
                    ))}
                  </select>
                </label>
              ) : (
                <label>
                  Firma
                  <input value={companies[0]?.name || currentUser?.companyName || "Firma bilgisi yükleniyor..."} readOnly />
                </label>
              )}
              <label>
                Marka
                <input
                  maxLength={100}
                  value={form.brand}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, brand: event.target.value }))
                  }
                />
              </label>
              <label>
                Model
                <input
                  maxLength={100}
                  value={form.model}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, model: event.target.value }))
                  }
                />
              </label>
              <label>
                Koltuk Kapasitesi *
                <input
                  type="number"
                  min="1"
                  max="1000"
                  step="1"
                  required
                  value={form.seatCapacity}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, seatCapacity: event.target.value }))
                  }
                />
              </label>
              <label>
                Durum
                <select
                  value={String(form.isActive)}
                  onChange={(event) =>
                    setForm((current) => ({ ...current, isActive: event.target.value === "true" }))
                  }
                >
                  <option value="true">Aktif</option>
                  <option value="false">Pasif</option>
                </select>
              </label>
            </div>
            <div className="detail-actions">
              <button className="primary-button" type="submit" disabled={saving}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button
                className="secondary-button"
                type="button"
                onClick={() => {
                  setShowForm(false);
                  setEditingBus(null);
                }}
                disabled={saving}
              >
                İptal
              </button>
            </div>
          </form>
        </section>
      )}

      <section className="dashboard-section table-section">
        <div className="filter-header">
          <h2>Otobüs Listesi</h2>
          <div className="filters-grid bus-filters">
            <label>
              Plaka, marka veya model
              <input
                type="search"
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  setPageNumber(1);
                }}
                placeholder="Otobüs ara..."
              />
            </label>
            {isAdmin && (
              <label>
                Firma
                <select
                  value={companyFilter}
                  onChange={(event) => {
                    setCompanyFilter(event.target.value);
                    setPageNumber(1);
                  }}
                >
                  <option value="">Tüm firmalar</option>
                  {companies.map((company) => (
                    <option key={company.id} value={company.id}>{company.name}</option>
                  ))}
                </select>
              </label>
            )}
            <label>
              Durum
              <select
                value={statusFilter}
                onChange={(event) => {
                  setStatusFilter(event.target.value);
                  setPageNumber(1);
                }}
              >
                <option value="">Tümü</option>
                <option value="active">Aktif</option>
                <option value="inactive">Pasif</option>
              </select>
            </label>
            <label>
              Marka filtresi
              <input
                value={brandFilter}
                onChange={(event) => {
                  setBrandFilter(event.target.value);
                  setPageNumber(1);
                }}
              />
            </label>
            <label>
              En az koltuk
              <input
                type="number"
                min="0"
                value={minCapacity}
                onChange={(event) => {
                  setMinCapacity(event.target.value);
                  setPageNumber(1);
                }}
              />
            </label>
            <label>
              En fazla koltuk
              <input
                type="number"
                min="0"
                value={maxCapacity}
                onChange={(event) => {
                  setMaxCapacity(event.target.value);
                  setPageNumber(1);
                }}
              />
            </label>
          </div>
          <div className="filter-actions">
            <span className="result-count">Toplam {totalCount} otobüs</span>
            <button className="secondary-button" type="button" onClick={clearFilters}>
              Filtreleri Temizle
            </button>
          </div>
        </div>

        {loading ? (
          <p className="empty-state">Otobüsler yükleniyor...</p>
        ) : buses.length === 0 ? (
          <p className="empty-state">Filtrelerle eşleşen otobüs bulunamadı.</p>
        ) : (
          <div className="table-wrap">
            <table className="data-table bus-data-table">
              <thead>
                <tr>
                  <th>
                    <button type="button" className="sort-button" onClick={() => changeSort("plate")}>
                      Plaka {sortBy === "plate" ? (sortDirection === "asc" ? "↑" : "↓") : ""}
                    </button>
                  </th>
                  <th>Firma</th>
                  <th>Marka</th>
                  <th>Model</th>
                  <th>
                    <button type="button" className="sort-button" onClick={() => changeSort("capacity")}>
                      Koltuk Kapasitesi {sortBy === "capacity" ? (sortDirection === "asc" ? "↑" : "↓") : ""}
                    </button>
                  </th>
                  <th>Durum</th>
                  <th>İşlemler</th>
                </tr>
              </thead>
              <tbody>
                {buses.map((bus) => (
                  <tr key={bus.id}>
                    <td>{bus.plateNumber}</td>
                    <td>{bus.companyName}</td>
                    <td>{bus.brand || "—"}</td>
                    <td>{bus.model || "—"}</td>
                    <td>{bus.seatCapacity}</td>
                    <td>
                      <span className={`status-badge ${bus.isActive ? "active" : "inactive"}`}>
                        {bus.isActive ? "Aktif" : "Pasif"}
                      </span>
                    </td>
                    <td>
                      <div className="action-group">
                        <button className="link-button" type="button" onClick={() => void openDetails(bus.id)}>
                          Detay
                        </button>
                        <button className="link-button" type="button" onClick={() => startEdit(bus)}>
                          Düzenle
                        </button>
                        <button className="link-button" type="button" onClick={() => void toggleStatus(bus)}>
                          {bus.isActive ? "Pasifleştir" : "Aktifleştir"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="pagination-row">
          <span>Sayfa {pageNumber} / {totalPages}</span>
          <div>
            <button
              type="button"
              disabled={loading || pageNumber <= 1}
              onClick={() => setPageNumber((page) => Math.max(1, page - 1))}
            >
              Önceki
            </button>
            <button
              type="button"
              disabled={loading || pageNumber >= totalPages}
              onClick={() => setPageNumber((page) => Math.min(totalPages, page + 1))}
            >
              Sonraki
            </button>
          </div>
        </div>
      </section>

      {(details || detailsLoading) && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => {
          if (event.target === event.currentTarget) setDetails(null);
        }}>
          <section className="modal-card bus-detail-modal" role="dialog" aria-modal="true" aria-labelledby="bus-detail-title">
            {detailsLoading || !details ? (
              <p>Otobüs detayları yükleniyor...</p>
            ) : (
              <>
                <h3 id="bus-detail-title">Otobüs Detayları</h3>
                <div className="detail-grid">
                  <div><strong>Otobüs ID</strong><span>{details.id}</span></div>
                  <div><strong>Plaka</strong><span>{details.plateNumber}</span></div>
                  <div><strong>Marka / Model</strong><span>{[details.brand, details.model].filter(Boolean).join(" ") || "—"}</span></div>
                  <div><strong>Koltuk Kapasitesi</strong><span>{details.seatCapacity}</span></div>
                  <div><strong>Durum</strong><span>{details.isActive ? "Aktif" : "Pasif"}</span></div>
                  <div><strong>Oluşturulma</strong><span>{new Date(details.createdAt).toLocaleString("tr-TR")}</span></div>
                  <div>
                    <strong>Firma</strong>
                    <span>
                      {details.companyName} (#{details.companyId}) · {details.companyIsActive ? "Aktif" : "Pasif"}
                    </span>
                  </div>
                  <div><strong>Toplam Sefer</strong><span>{details.totalTrips}</span></div>
                  <div><strong>Yaklaşan Sefer</strong><span>{details.upcomingTripsCount}</span></div>
                  <div><strong>Tamamlanan Sefer</strong><span>{details.completedTripsCount}</span></div>
                  <div><strong>İptal Edilen Sefer</strong><span>{details.cancelledTripsCount}</span></div>
                </div>

                <h4>Yaklaşan Seferler</h4>
                {details.upcomingTrips.length === 0 ? (
                  <p>Yaklaşan sefer bulunmuyor.</p>
                ) : (
                  <div className="table-wrap">
                    <table className="data-table bus-trip-table">
                      <thead>
                        <tr><th>Güzergâh</th><th>Kalkış</th><th>Durum</th><th>Boş Koltuk</th></tr>
                      </thead>
                      <tbody>
                        {details.upcomingTrips.map((trip) => (
                          <tr key={trip.id}>
                            <td>{trip.departureCity} – {trip.arrivalCity}</td>
                            <td>{new Date(trip.departureTime).toLocaleString("tr-TR")}</td>
                            <td>{trip.status}</td>
                            <td>{trip.availableSeats}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
                <div className="detail-actions">
                  <button
                    className="secondary-button"
                    type="button"
                    onClick={() => navigate(`/admin/companies/${details.companyId}`)}
                  >
                    Firmayı Görüntüle
                  </button>
                  <button
                    className="primary-button"
                    type="button"
                    onClick={() => {
                      startEdit(details);
                      setDetails(null);
                    }}
                  >
                    Otobüsü Düzenle
                  </button>
                  <button
                    className="secondary-button"
                    type="button"
                    onClick={() => void toggleStatus(details)}
                  >
                    {details.isActive ? "Pasifleştir" : "Aktifleştir"}
                  </button>
                  <button className="secondary-button" type="button" onClick={() => setDetails(null)}>
                    Kapat
                  </button>
                </div>
              </>
            )}
          </section>
        </div>
      )}
    </div>
  );
}

export default BusManagement;
