import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import "../Admin.css";

const API_URL = "http://localhost:5062/api/trips";
const BUSES_API_URL = "http://localhost:5062/api/buses";
const COMPANIES_API_URL = "http://localhost:5062/api/admin/companies";
const ROUTES_API_URL = "http://localhost:5062/api/routes";
const PAGE_SIZE = 10;

type Trip = {
  id: number;
  companyId: number;
  companyName: string;
  busId: number;
  busPlateNumber: string;
  busBrand: string | null;
  busModel: string | null;
  busCapacity: number;
  routeId: number;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  price: number;
  availableSeats: number;
  status: string;
};

type TripDetail = Trip & {
  createdAt: string;
  updatedAt: string | null;
  busIsActive: boolean;
  companyIsActive: boolean;
  distanceKm: number | null;
  estimatedDurationMinutes: number | null;
  cancellationReason: string | null;
  cancelledAt: string | null;
};

type Summary = {
  totalTrips: number;
  scheduledTrips: number;
  inProgressTrips: number;
  completedTrips: number;
  cancelledTrips: number;
};

type Bus = {
  id: number;
  companyId: number;
  companyName: string;
  plateNumber: string;
  brand: string | null;
  model: string | null;
  seatCapacity: number;
  isActive: boolean;
};

type Company = {
  id: number;
  name: string;
  isActive: boolean | null;
};

type Route = {
  id: number;
  departureCity: string;
  arrivalCity: string;
  distanceKm: number | null;
  estimatedDurationMinutes: number | null;
};

type ListResponse = {
  items: Trip[];
  totalCount: number;
  totalPages: number;
};

type TripForm = {
  companyId: string;
  busId: string;
  routeId: string;
  departureTime: string;
  arrivalTime: string;
  price: string;
};

type RouteForm = {
  departureCity: string;
  arrivalCity: string;
  distanceKm: string;
  estimatedDurationMinutes: string;
};

const emptyTripForm: TripForm = {
  companyId: "",
  busId: "",
  routeId: "",
  departureTime: "",
  arrivalTime: "",
  price: "",
};

const emptyRouteForm: RouteForm = {
  departureCity: "",
  arrivalCity: "",
  distanceKm: "",
  estimatedDurationMinutes: "",
};

type SessionUser = {
  role?: string;
};

function getSessionUser(): SessionUser | null {
  try {
    return JSON.parse(sessionStorage.getItem("travela_user") || "null") as SessionUser | null;
  } catch {
    return null;
  }
}

const knownStatuses = ["Scheduled", "InProgress", "Completed", "Cancelled"];

function normalizeStatus(status: string) {
  const value = status.trim().toLowerCase();
  if (value === "scheduled" || value === "planlandı" || value === "planlandi") return "Scheduled";
  if (value === "inprogress" || value === "in progress" || value === "devam ediyor") return "InProgress";
  if (value === "completed" || value === "tamamlandı" || value === "tamamlandi") return "Completed";
  if (value === "cancelled" || value === "canceled" || value === "iptal" || value === "iptal edildi") return "Cancelled";
  return status;
}

function statusLabel(status: string) {
  const value = normalizeStatus(status);
  if (value === "Scheduled") return "Planlandı";
  if (value === "InProgress") return "Devam Ediyor";
  if (value === "Completed") return "Tamamlandı";
  if (value === "Cancelled") return "İptal Edildi";
  return status;
}

function toLocalInput(value: string) {
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function formatDate(value: string) {
  return new Date(value).toLocaleString("tr-TR");
}

function TripManagement() {
  const navigate = useNavigate();
  const currentUser = getSessionUser();
  const isAdmin = currentUser?.role === "Admin";
  const createEmptyTripForm = (): TripForm => ({
    ...emptyTripForm,
  });
  const [trips, setTrips] = useState<Trip[]>([]);
  const [summary, setSummary] = useState<Summary | null>(null);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [buses, setBuses] = useState<Bus[]>([]);
  const [routes, setRoutes] = useState<Route[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [pageNumber, setPageNumber] = useState(1);
  const [sortBy, setSortBy] = useState("date");
  const [sortDirection, setSortDirection] = useState("asc");
  const [search, setSearch] = useState("");
  const [companyFilter, setCompanyFilter] = useState("");
  const [departureCityFilter, setDepartureCityFilter] = useState("");
  const [arrivalCityFilter, setArrivalCityFilter] = useState("");
  const [departureFrom, setDepartureFrom] = useState("");
  const [departureTo, setDepartureTo] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [busPlateFilter, setBusPlateFilter] = useState("");
  const [minPrice, setMinPrice] = useState("");
  const [maxPrice, setMaxPrice] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [summaryError, setSummaryError] = useState("");
  const [message, setMessage] = useState("");
  const [showTripForm, setShowTripForm] = useState(false);
  const [editingTrip, setEditingTrip] = useState<Trip | null>(null);
  const [tripForm, setTripForm] = useState<TripForm>(createEmptyTripForm);
  const [detail, setDetail] = useState<TripDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [cancelTrip, setCancelTrip] = useState<Trip | null>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [showRouteForm, setShowRouteForm] = useState(false);
  const [routeForm, setRouteForm] = useState<RouteForm>(emptyRouteForm);
  const requestHeaders = (json = false): HeadersInit => {
    const token = sessionStorage.getItem("travela_token");
    return {
      ...(json ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    };
  };

  const unauthorized = () => {
    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");
    navigate("/login", { replace: true });
  };

  const readError = async (response: Response, fallback: string) => {
    const body = (await response.json().catch(() => null)) as { message?: string } | null;
    return body?.message || `${fallback} (${response.status})`;
  };

  const request = async (url: string, init?: RequestInit) => {
    const response = await fetch(url, init);
    if (response.status === 401) {
      unauthorized();
      throw new Error("Oturum süresi doldu. Lütfen tekrar giriş yapın.");
    }
    return response;
  };

  const loadOptions = async () => {
    try {
      const [companyResponse, busResponse, routeResponse] = await Promise.all([
        isAdmin
          ? request(COMPANIES_API_URL, { headers: requestHeaders() })
          : Promise.resolve(null),
        request(
          `${BUSES_API_URL}?pageNumber=1&pageSize=100`,
          { headers: requestHeaders() }
        ),
        request(ROUTES_API_URL, { headers: requestHeaders() }),
      ]);

      if (companyResponse && !companyResponse.ok) {
        throw new Error(await readError(companyResponse, "Firmalar alınamadı"));
      }
      if (!busResponse.ok) throw new Error(await readError(busResponse, "Otobüsler alınamadı"));
      if (!routeResponse.ok) throw new Error(await readError(routeResponse, "Güzergâhlar alınamadı"));

      const [adminCompanyData, busData, routeData] = await Promise.all([
        companyResponse ? companyResponse.json() as Promise<Company[]> : Promise.resolve([]),
        busResponse.json() as Promise<Bus[] | { items?: Bus[] }>,
        routeResponse.json() as Promise<Route[] | { items?: Route[] }>,
      ]);

      const normalizedBuses = Array.isArray(busData) ? busData : (busData as { items?: Bus[] }).items ?? [];
      const normalizedRoutes = Array.isArray(routeData) ? routeData : (routeData as { items?: Route[] }).items ?? [];

      if (isAdmin) {
        setCompanies(adminCompanyData);
      }
      setBuses(normalizedBuses);
      setRoutes(normalizedRoutes);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Seçim verileri yüklenemedi.");
    }
  };

  const loadSummary = async () => {
    setSummaryError("");
    try {
      const response = await request(`${API_URL}/summary`, { headers: requestHeaders() });
      if (!response.ok) throw new Error(await readError(response, "Sefer istatistikleri alınamadı"));
      setSummary(await response.json());
    } catch (err) {
      setSummaryError(err instanceof Error ? err.message : "Sefer istatistikleri yüklenemedi.");
    }
  };

  const loadTrips = async () => {
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
      if (departureCityFilter.trim()) params.set("departureCity", departureCityFilter.trim());
      if (arrivalCityFilter.trim()) params.set("arrivalCity", arrivalCityFilter.trim());
      if (departureFrom) params.set("departureFrom", `${departureFrom}T00:00:00Z`);
      if (departureTo) params.set("departureTo", `${departureTo}T00:00:00Z`);
      if (statusFilter) params.set("status", statusFilter);
      if (busPlateFilter.trim()) params.set("busPlate", busPlateFilter.trim());
      if (minPrice) params.set("minPrice", minPrice);
      if (maxPrice) params.set("maxPrice", maxPrice);

      const response = await request(`${API_URL}?${params}`, { headers: requestHeaders() });
      if (!response.ok) throw new Error(await readError(response, "Seferler alınamadı"));
      const data: ListResponse = await response.json();
      if (!Array.isArray(data.items) || typeof data.totalCount !== "number" || typeof data.totalPages !== "number") {
        throw new Error("Sefer listesi API yanıtı beklenen biçimde değil.");
      }
      setTrips(data.items);
      setTotalCount(data.totalCount);
      setTotalPages(Math.max(data.totalPages, 1));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Seferler yüklenemedi.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadOptions();
    void loadSummary();
  }, []);

  useEffect(() => {
    void loadTrips();
  }, [
    pageNumber,
    search,
    companyFilter,
    departureCityFilter,
    arrivalCityFilter,
    departureFrom,
    departureTo,
    statusFilter,
    busPlateFilter,
    minPrice,
    maxPrice,
    sortBy,
    sortDirection,
  ]);

  const selectedCompanyBuses = useMemo(
    () => buses.filter((bus) => bus.isActive && (!tripForm.companyId || bus.companyId === Number(tripForm.companyId))),
    [buses, tripForm.companyId]
  );

  const resetTripForm = () => {
    setShowTripForm(false);
    setEditingTrip(null);
    setTripForm(createEmptyTripForm());
  };

  const createTrip = () => {
    setEditingTrip(null);
    setTripForm(createEmptyTripForm());
    setError("");
    setMessage("");
    setShowTripForm(true);
  };

  const editTrip = (trip: Trip) => {
    setEditingTrip(trip);
    setTripForm({
      companyId: String(trip.companyId),
      busId: String(trip.busId),
      routeId: String(trip.routeId),
      departureTime: toLocalInput(trip.departureTime),
      arrivalTime: toLocalInput(trip.arrivalTime),
      price: String(trip.price),
    });
    setError("");
    setMessage("");
    setShowTripForm(true);
  };

  const openDetail = async (tripId: number) => {
    setDetailLoading(true);
    setDetail(null);
    try {
      const response = await request(`${API_URL}/${tripId}`, { headers: requestHeaders() });
      if (!response.ok) throw new Error(await readError(response, "Sefer detayı alınamadı"));
      setDetail(await response.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sefer detayı yüklenemedi.");
    } finally {
      setDetailLoading(false);
    }
  };

  const submitTrip = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    setError("");
    setMessage("");
    try {
      if (!tripForm.routeId || !tripForm.busId || !tripForm.departureTime || !tripForm.arrivalTime) {
        throw new Error("Güzergâh, otobüs, kalkış ve varış bilgileri zorunludur.");
      }
      const response = await request(
        editingTrip ? `${API_URL}/${editingTrip.id}` : API_URL,
        {
          method: editingTrip ? "PUT" : "POST",
          headers: requestHeaders(true),
          body: JSON.stringify({
            companyId: isAdmin && tripForm.companyId ? Number(tripForm.companyId) : null,
            busId: Number(tripForm.busId),
            routeId: Number(tripForm.routeId),
            departureTime: new Date(tripForm.departureTime).toISOString(),
            arrivalTime: new Date(tripForm.arrivalTime).toISOString(),
            price: Number(tripForm.price),
          }),
        }
      );
      if (!response.ok) throw new Error(await readError(response, "Sefer kaydedilemedi"));
      const data = (await response.json().catch(() => null)) as { message?: string } | null;
      resetTripForm();
      setMessage(data?.message || "Sefer kaydedildi.");
      await Promise.all([loadTrips(), loadSummary()]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sefer kaydedilemedi.");
    } finally {
      setSaving(false);
    }
  };

  const updateStatus = async (trip: Trip, nextStatus: string) => {
    setError("");
    setMessage("");
    try {
      const response = await request(`${API_URL}/${trip.id}/status`, {
        method: "PATCH",
        headers: requestHeaders(true),
        body: JSON.stringify({ status: nextStatus }),
      });
      if (!response.ok) throw new Error(await readError(response, "Sefer durumu güncellenemedi"));
      const data = (await response.json().catch(() => null)) as { message?: string } | null;
      setMessage(data?.message || "Sefer durumu güncellendi.");
      await Promise.all([loadTrips(), loadSummary()]);
      if (detail?.id === trip.id) await openDetail(trip.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sefer durumu güncellenemedi.");
    }
  };

  const submitCancellation = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!cancelTrip) return;
    setSaving(true);
    setError("");
    try {
      const response = await request(`${API_URL}/${cancelTrip.id}/cancel`, {
        method: "POST",
        headers: requestHeaders(true),
        body: JSON.stringify({ reason: cancelReason }),
      });
      if (!response.ok) throw new Error(await readError(response, "Sefer iptal edilemedi"));
      setCancelTrip(null);
      setCancelReason("");
      setMessage("Sefer iptal edildi. Rezervasyon/ödeme altyapısı bağlı olmadığından iade veya yolcu bildirimi yapılmadı.");
      await Promise.all([loadTrips(), loadSummary()]);
      if (detail?.id === cancelTrip.id) await openDetail(cancelTrip.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sefer iptal edilemedi.");
    } finally {
      setSaving(false);
    }
  };

  const submitRoute = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSaving(true);
    setError("");
    try {
      const response = await request(ROUTES_API_URL, {
        method: "POST",
        headers: requestHeaders(true),
        body: JSON.stringify({
          departureCity: routeForm.departureCity,
          arrivalCity: routeForm.arrivalCity,
          distanceKm: routeForm.distanceKm ? Number(routeForm.distanceKm) : null,
          estimatedDurationMinutes: routeForm.estimatedDurationMinutes
            ? Number(routeForm.estimatedDurationMinutes)
            : null,
        }),
      });
      if (!response.ok) throw new Error(await readError(response, "Güzergâh oluşturulamadı"));
      const data = (await response.json()) as { route: Route; message: string };
      setRoutes((current) => [...current, data.route].sort((a, b) => a.departureCity.localeCompare(b.departureCity)));
      setTripForm((current) => ({ ...current, routeId: String(data.route.id) }));
      setRouteForm(emptyRouteForm);
      setShowRouteForm(false);
      setMessage(data.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Güzergâh oluşturulamadı.");
    } finally {
      setSaving(false);
    }
  };

  const clearFilters = () => {
    setSearch("");
    setCompanyFilter("");
    setDepartureCityFilter("");
    setArrivalCityFilter("");
    setDepartureFrom("");
    setDepartureTo("");
    setStatusFilter("");
    setBusPlateFilter("");
    setMinPrice("");
    setMaxPrice("");
    setSortBy("date");
    setSortDirection("asc");
    setPageNumber(1);
  };

  const changeSort = (value: string) => {
    if (sortBy === value) setSortDirection((current) => current === "asc" ? "desc" : "asc");
    else {
      setSortBy(value);
      setSortDirection("asc");
    }
    setPageNumber(1);
  };

  const statCards = [
    ["Toplam Sefer", summary?.totalTrips],
    ["Planlanan Seferler", summary?.scheduledTrips],
    ["Devam Eden Seferler", summary?.inProgressTrips],
    ["Tamamlanan Seferler", summary?.completedTrips],
    ["İptal Edilen Seferler", summary?.cancelledTrips],
  ];

  return (
    <div className="admin-dashboard">
      <div className="dashboard-heading trip-page-heading">
        <div>
          <h1>Sefer Yönetimi</h1>
          <p>Seferleri, güzergâhları ve otobüs atamalarını yönetin.</p>
        </div>
        <button className="primary-button" type="button" onClick={createTrip}>+ Yeni Sefer Ekle</button>
      </div>

      <div className="dashboard-stats trip-stats">
        {statCards.map(([title, value]) => (
          <div className="stat-card" key={title}>
            <p>{title}</p>
            <h2>{value ?? "—"}</h2>
          </div>
        ))}
      </div>

      {summaryError && <p className="form-alert error" role="alert">{summaryError}</p>}
      {error && <p className="form-alert error" role="alert">{error}</p>}
      {message && <p className="form-alert success" role="status">{message}</p>}

      {showTripForm && (
        <section className="dashboard-section form-section">
          <div className="trip-form-heading">
            <h2>{editingTrip ? `Sefer #${editingTrip.id} Düzenle` : "Yeni Sefer Oluştur"}</h2>
            {isAdmin && (
              <button className="secondary-button" type="button" onClick={() => setShowRouteForm(true)}>
                + Güzergâh Oluştur
              </button>
            )}
          </div>
          {routes.length === 0 && <p className="trip-helper">Sefer eklemek için önce aktif bir güzergâh oluşturun.</p>}
          <form className="user-form" onSubmit={submitTrip}>
            <div className="form-grid trip-form-grid">
              {isAdmin && (
                <label>
                  Firma *
                  <select
                    required
                    value={tripForm.companyId}
                    onChange={(event) => setTripForm((current) => ({ ...current, companyId: event.target.value, busId: "" }))}
                  >
                    <option value="">Firma seçin</option>
                    {companies.filter((company) => company.isActive !== false).map((company) => (
                      <option key={company.id} value={company.id}>{company.name}</option>
                    ))}
                  </select>
                </label>
              )}
              <label>
                Güzergâh *
                <select
                  required
                  value={tripForm.routeId}
                  onChange={(event) => setTripForm((current) => ({ ...current, routeId: event.target.value }))}
                >
                  <option value="">Güzergâh seçin</option>
                  {routes.map((route) => (
                    <option key={route.id} value={route.id}>
                      {route.departureCity} – {route.arrivalCity}
                      {route.distanceKm != null ? ` · ${route.distanceKm} km` : ""}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Otobüs *
                <select
                  required
                  value={tripForm.busId}
                  onChange={(event) => setTripForm((current) => ({ ...current, busId: event.target.value }))}
                >
                  <option value="">Aktif otobüs seçin</option>
                  {selectedCompanyBuses.map((bus) => (
                    <option key={bus.id} value={bus.id}>
                      {bus.plateNumber} · {bus.seatCapacity} koltuk
                    </option>
                  ))}
                </select>
              </label>
              {tripForm.busId && (
                <div className="trip-selection-info">
                  {(() => {
                    const selected = buses.find((bus) => bus.id === Number(tripForm.busId));
                    return selected ? <><strong>Seçilen otobüs:</strong> {selected.plateNumber} · {selected.seatCapacity} koltuk</> : null;
                  })()}
                </div>
              )}
              <label>
                Kalkış tarihi ve saati *
                <input
                  type="datetime-local"
                  required
                  value={tripForm.departureTime}
                  onChange={(event) => setTripForm((current) => ({ ...current, departureTime: event.target.value }))}
                />
              </label>
              <label>
                Varış tarihi ve saati *
                <input
                  type="datetime-local"
                  required
                  value={tripForm.arrivalTime}
                  onChange={(event) => setTripForm((current) => ({ ...current, arrivalTime: event.target.value }))}
                />
              </label>
              <label>
                Bilet fiyatı (TL) *
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  required
                  value={tripForm.price}
                  onChange={(event) => setTripForm((current) => ({ ...current, price: event.target.value }))}
                />
              </label>
              <div className="trip-selection-info">
                Yeni seferler güvenlik gereği <strong>Planlandı</strong> durumunda oluşturulur.
              </div>
            </div>
            <div className="detail-actions">
              <button className="primary-button" type="submit" disabled={saving || routes.length === 0}>
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
              <button className="secondary-button" type="button" onClick={resetTripForm} disabled={saving}>İptal</button>
            </div>
          </form>
        </section>
      )}

      <section className="dashboard-section table-section">
        <div className="filter-header">
          <h2>Sefer Listesi</h2>
          <div className="filters-grid trip-filters">
            <label>Sefer ID, firma veya plaka
              <input type="search" value={search} onChange={(event) => { setSearch(event.target.value); setPageNumber(1); }} placeholder="Ara..." />
            </label>
            {isAdmin && (
              <label>Firma
                <select value={companyFilter} onChange={(event) => { setCompanyFilter(event.target.value); setPageNumber(1); }}>
                  <option value="">Tüm firmalar</option>
                  {companies.map((company) => <option key={company.id} value={company.id}>{company.name}</option>)}
                </select>
              </label>
            )}
            <label>Kalkış şehri
              <input value={departureCityFilter} onChange={(event) => { setDepartureCityFilter(event.target.value); setPageNumber(1); }} />
            </label>
            <label>Varış şehri
              <input value={arrivalCityFilter} onChange={(event) => { setArrivalCityFilter(event.target.value); setPageNumber(1); }} />
            </label>
            <label>Kalkış başlangıcı
              <input type="date" value={departureFrom} onChange={(event) => { setDepartureFrom(event.target.value); setPageNumber(1); }} />
            </label>
            <label>Kalkış bitişi
              <input type="date" value={departureTo} onChange={(event) => { setDepartureTo(event.target.value); setPageNumber(1); }} />
            </label>
            <label>Durum
              <select value={statusFilter} onChange={(event) => { setStatusFilter(event.target.value); setPageNumber(1); }}>
                <option value="">Tüm durumlar</option>
                {knownStatuses.map((status) => <option key={status} value={status}>{statusLabel(status)}</option>)}
              </select>
            </label>
            <label>Otobüs plakası
              <input value={busPlateFilter} onChange={(event) => { setBusPlateFilter(event.target.value); setPageNumber(1); }} />
            </label>
            <label>En düşük fiyat
              <input type="number" min="0" step="0.01" value={minPrice} onChange={(event) => { setMinPrice(event.target.value); setPageNumber(1); }} />
            </label>
            <label>En yüksek fiyat
              <input type="number" min="0" step="0.01" value={maxPrice} onChange={(event) => { setMaxPrice(event.target.value); setPageNumber(1); }} />
            </label>
          </div>
          <div className="filter-actions">
            <span className="result-count">Toplam {totalCount} sefer</span>
            <button className="secondary-button" type="button" onClick={clearFilters}>Filtreleri Temizle</button>
          </div>
        </div>

        {loading ? <p className="empty-state">Seferler yükleniyor...</p> :
          trips.length === 0 ? <p className="empty-state">Filtrelerle eşleşen sefer bulunamadı.</p> :
            <div className="table-wrap">
              <table className="data-table trip-data-table">
                <thead>
                  <tr>
                    <th>Sefer ID</th><th>Firma</th><th>Güzergâh</th><th>Otobüs</th>
                    <th><button className="sort-button" type="button" onClick={() => changeSort("date")}>Kalkış {sortBy === "date" ? (sortDirection === "asc" ? "↑" : "↓") : ""}</button></th>
                    <th>Varış</th>
                    <th><button className="sort-button" type="button" onClick={() => changeSort("price")}>Fiyat {sortBy === "price" ? (sortDirection === "asc" ? "↑" : "↓") : ""}</button></th>
                    <th>Boş Koltuk</th><th>Durum</th><th>İşlemler</th>
                  </tr>
                </thead>
                <tbody>
                  {trips.map((trip) => (
                    <tr key={trip.id}>
                      <td>#{trip.id}</td>
                      <td>{trip.companyName}</td>
                      <td>{trip.departureCity} – {trip.arrivalCity}</td>
                      <td>{trip.busPlateNumber}</td>
                      <td>{formatDate(trip.departureTime)}</td>
                      <td>{formatDate(trip.arrivalTime)}</td>
                      <td>{trip.price.toLocaleString("tr-TR", { style: "currency", currency: "TRY" })}</td>
                      <td>{trip.availableSeats} / {trip.busCapacity}</td>
                      <td><span className={`status-badge trip-status ${normalizeStatus(trip.status).toLowerCase()}`}>{statusLabel(trip.status)}</span></td>
                      <td>
                        <div className="action-group">
                          <button className="link-button" type="button" onClick={() => void openDetail(trip.id)}>Detay</button>
                          {normalizeStatus(trip.status) === "Scheduled" && new Date(trip.departureTime) > new Date() && (
                            <>
                              <button className="link-button" type="button" onClick={() => editTrip(trip)}>Düzenle</button>
                              <button className="link-button trip-cancel-link" type="button" onClick={() => { setCancelTrip(trip); setCancelReason(""); }}>
                                İptal
                              </button>
                            </>
                          )}
                          {normalizeStatus(trip.status) === "Scheduled" && new Date(trip.departureTime) <= new Date() &&
                            <button className="link-button" type="button" onClick={() => void updateStatus(trip, "InProgress")}>Başlat</button>}
                          {normalizeStatus(trip.status) === "InProgress" && new Date(trip.arrivalTime) <= new Date() &&
                            <button className="link-button" type="button" onClick={() => void updateStatus(trip, "Completed")}>Tamamla</button>}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>}

        <div className="pagination-row">
          <span>Sayfa {pageNumber} / {totalPages}</span>
          <div>
            <button type="button" disabled={loading || pageNumber <= 1} onClick={() => setPageNumber((value) => Math.max(1, value - 1))}>Önceki</button>
            <button type="button" disabled={loading || pageNumber >= totalPages} onClick={() => setPageNumber((value) => Math.min(totalPages, value + 1))}>Sonraki</button>
          </div>
        </div>
      </section>

      {(detail || detailLoading) && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setDetail(null); }}>
          <section className="modal-card trip-detail-modal" role="dialog" aria-modal="true" aria-labelledby="trip-detail-heading">
            {detailLoading || !detail ? <p>Sefer detayları yükleniyor...</p> : <>
              <h3 id="trip-detail-heading">Sefer #{detail.id} Detayları</h3>
              <div className="detail-grid">
                <div><strong>Firma</strong><span>{detail.companyName} (#{detail.companyId}) · {detail.companyIsActive ? "Aktif" : "Pasif"}</span></div>
                <div><strong>Otobüs</strong><span>{detail.busPlateNumber} · {detail.busBrand || ""} {detail.busModel || ""}</span></div>
                <div><strong>Otobüs durumu / kapasitesi</strong><span>{detail.busIsActive ? "Aktif" : "Pasif"} · {detail.busCapacity} koltuk</span></div>
                <div><strong>Güzergâh</strong><span>{detail.departureCity} – {detail.arrivalCity}</span></div>
                <div><strong>Mesafe / tahmini süre</strong><span>{detail.distanceKm ?? "—"} km · {detail.estimatedDurationMinutes ?? "—"} dk</span></div>
                <div><strong>Kalkış</strong><span>{formatDate(detail.departureTime)}</span></div>
                <div><strong>Varış</strong><span>{formatDate(detail.arrivalTime)}</span></div>
                <div><strong>Bilet fiyatı</strong><span>{detail.price.toLocaleString("tr-TR", { style: "currency", currency: "TRY" })}</span></div>
                <div><strong>Boş / toplam koltuk</strong><span>{detail.availableSeats} / {detail.busCapacity}</span></div>
                <div><strong>Sefer durumu</strong><span>{statusLabel(detail.status)}</span></div>
                <div><strong>Oluşturulma tarihi</strong><span>{formatDate(detail.createdAt)}</span></div>
                {detail.cancelledAt && <div><strong>İptal tarihi / nedeni</strong><span>{formatDate(detail.cancelledAt)} · {detail.cancellationReason}</span></div>}
              </div>
              <p className="trip-helper">Rezervasyon/satış tabloları bulunmadığından satılmış koltuk ve doluluk oranı gösterilmiyor.</p>
              <div className="detail-actions">
                {isAdmin && <>
                  <button className="secondary-button" type="button" onClick={() => navigate(`/admin/companies/${detail.companyId}`)}>Firmayı Görüntüle</button>
                  <button className="secondary-button" type="button" onClick={() => navigate("/admin/buses")}>Otobüsleri Görüntüle</button>
                </>}
                {normalizeStatus(detail.status) === "Scheduled" && <button className="primary-button" type="button" onClick={() => { editTrip(detail); setDetail(null); }}>Düzenle</button>}
                <button className="secondary-button" type="button" onClick={() => setDetail(null)}>Kapat</button>
              </div>
            </>}
          </section>
        </div>
      )}

      {cancelTrip && (
        <div className="modal-backdrop">
          <section className="modal-card trip-cancel-modal" role="dialog" aria-modal="true" aria-labelledby="trip-cancel-heading">
            <h3 id="trip-cancel-heading">Sefer #{cancelTrip.id} iptal edilsin mi?</h3>
            <p>İptal edilen sefer silinmez ve yeni satışa kapatılır. Rezervasyon/ödeme entegrasyonu bulunmadığından otomatik iade veya bildirim yapılmaz.</p>
            <form className="user-form" onSubmit={submitCancellation}>
              <label>İptal nedeni *
                <textarea required maxLength={500} rows={4} value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} />
              </label>
              <div className="detail-actions">
                <button className="primary-button danger-button" type="submit" disabled={saving}>{saving ? "İptal ediliyor..." : "Seferi İptal Et"}</button>
                <button className="secondary-button" type="button" onClick={() => setCancelTrip(null)} disabled={saving}>Geri Dön</button>
              </div>
            </form>
          </section>
        </div>
      )}

      {showRouteForm && (
        <div className="modal-backdrop">
          <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="route-form-heading">
            <h3 id="route-form-heading">Yeni Güzergâh Oluştur</h3>
            <form className="user-form" onSubmit={submitRoute}>
              <div className="form-grid">
                <label>Kalkış şehri *
                  <input required maxLength={100} value={routeForm.departureCity} onChange={(event) => setRouteForm((current) => ({ ...current, departureCity: event.target.value }))} />
                </label>
                <label>Varış şehri *
                  <input required maxLength={100} value={routeForm.arrivalCity} onChange={(event) => setRouteForm((current) => ({ ...current, arrivalCity: event.target.value }))} />
                </label>
                <label>Mesafe (km)
                  <input type="number" min="0" value={routeForm.distanceKm} onChange={(event) => setRouteForm((current) => ({ ...current, distanceKm: event.target.value }))} />
                </label>
                <label>Tahmini süre (dakika)
                  <input type="number" min="1" required value={routeForm.estimatedDurationMinutes} onChange={(event) => setRouteForm((current) => ({ ...current, estimatedDurationMinutes: event.target.value }))} />
                </label>
              </div>
              <div className="detail-actions">
                <button className="primary-button" type="submit" disabled={saving}>{saving ? "Kaydediliyor..." : "Güzergâhı Kaydet"}</button>
                <button className="secondary-button" type="button" onClick={() => setShowRouteForm(false)} disabled={saving}>İptal</button>
              </div>
            </form>
          </section>
        </div>
      )}
    </div>
  );
}

export default TripManagement;
