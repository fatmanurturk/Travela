import { useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";
import "../Admin.css";
import "./TripScheduling.css";
import { buildSchedule, turkeyInput, weekDays } from "../../tripScheduling";
import type { ScheduleMode } from "../../tripScheduling";
import JourneyStops from "../../components/JourneyStops";
import TripSeatEditor from "../../components/TripSeatEditor";
import TripChangeEditor from "../../components/TripChangeEditor";
import type { Journey } from "../../seatPlans";

const API_URL = apiUrl("/api/trips");
const BUSES_API_URL = apiUrl("/api/buses");
const COMPANIES_API_URL = apiUrl("/api/admin/companies");
const ROUTES_API_URL = apiUrl("/api/routes");
const PAGE_SIZE = 10;

type Trip = Journey & {
  id: number;
  companyId: number;
  companyName: string;
  busId: number;
  busPlateNumber: string;
  busBrand: string | null;
  busModel: string | null;
  busCapacity: number;
  routeId: number;
  departureTerminalId?: number | null;
  arrivalTerminalId?: number | null;
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
  departureTerminalId: string;
  arrivalTerminalId: string;
  boardingInstructions: string;
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
  departureTerminalId: "", arrivalTerminalId: "", boardingInstructions: "",
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

function toLocalInput(value: string) { return turkeyInput(value); }

function formatDate(value: string) {
  return new Date(value).toLocaleString("tr-TR", { timeZone: "Europe/Istanbul" });
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
  const [terminals, setTerminals] = useState<{ id: number; city: string; name: string; address: string }[]>([]);
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
  const [scheduleMode, setScheduleMode] = useState<ScheduleMode>("single");
  const [repeatUntil, setRepeatUntil] = useState("");
  const [repeatDays, setRepeatDays] = useState<number[]>([]);
  const [selectedDates, setSelectedDates] = useState<string[]>([]);
  const [dateToAdd, setDateToAdd] = useState("");
  const [copiedFrom, setCopiedFrom] = useState<number | null>(null);
  const schedulePreview = useMemo(() => {
    if (!showTripForm || editingTrip) return { items: [], error: "" };
    try { return { items: buildSchedule(tripForm.departureTime, tripForm.arrivalTime, scheduleMode, repeatUntil, repeatDays, selectedDates), error: "" }; }
    catch (cause) { return { items: [], error: cause instanceof Error ? cause.message : "Sefer tarihleri geçersiz." }; }
  }, [showTripForm, editingTrip, tripForm.departureTime, tripForm.arrivalTime, scheduleMode, repeatUntil, repeatDays, selectedDates]);
  const resetSchedule = () => { setScheduleMode("single"); setRepeatUntil(""); setRepeatDays([]); setSelectedDates([]); setDateToAdd(""); setCopiedFrom(null); };
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
      const [companyResponse, busResponse, routeResponse, terminalResponse] = await Promise.all([
        isAdmin
          ? request(COMPANIES_API_URL, { headers: requestHeaders() })
          : Promise.resolve(null),
        request(
          `${BUSES_API_URL}?pageNumber=1&pageSize=100`,
          { headers: requestHeaders() }
        ),
        request(ROUTES_API_URL, { headers: requestHeaders() }),
        request(apiUrl("/api/terminals"), { headers: requestHeaders() }),
      ]);

      if (companyResponse && !companyResponse.ok) {
        throw new Error(await readError(companyResponse, "Firmalar alınamadı"));
      }
      if (!busResponse.ok) throw new Error(await readError(busResponse, "Otobüsler alınamadı"));
      if (!routeResponse.ok) throw new Error(await readError(routeResponse, "Güzergâhlar alınamadı"));
      if (!terminalResponse.ok) throw new Error(await readError(terminalResponse, "Terminaller alınamadı"));
      setTerminals(await terminalResponse.json());

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

  const loadTrips = async (background = false) => {
    if (!background) { setLoading(true); setError(""); }
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
      if (!background) setError(err instanceof Error ? err.message : "Seferler yüklenemedi.");
    } finally {
      if (!background) setLoading(false);
    }
  };

  useEffect(() => {
    void loadOptions();
    void loadSummary();
  }, []);

  useEffect(() => {
    void loadTrips();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") { void loadTrips(true); void loadSummary(); }
    }, 15000);
    const refreshOnFocus = () => { void loadTrips(true); void loadSummary(); };
    window.addEventListener("focus", refreshOnFocus);
    return () => { window.clearInterval(timer); window.removeEventListener("focus", refreshOnFocus); };
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
    resetSchedule();
    setShowTripForm(false);
    setEditingTrip(null);
    setTripForm(createEmptyTripForm());
  };

  const createTrip = () => {
    resetSchedule();
    setEditingTrip(null);
    setTripForm(createEmptyTripForm());
    setError("");
    setMessage("");
    setShowTripForm(true);
  };

  const editTrip = (trip: Trip) => {
    resetSchedule();
    setEditingTrip(trip);
    setTripForm({
      companyId: String(trip.companyId),
      busId: String(trip.busId),
      routeId: String(trip.routeId),
      departureTerminalId: String(trip.departureTerminalId ?? ""), arrivalTerminalId: String(trip.arrivalTerminalId ?? ""), boardingInstructions: trip.boardingInstructions ?? "",
      departureTime: toLocalInput(trip.departureTime),
      arrivalTime: toLocalInput(trip.arrivalTime),
      price: String(trip.price),
    });
    setError("");
    setMessage("");
    setShowTripForm(true);
  };

  const copyTrip = (trip: Trip) => {
    resetSchedule(); setEditingTrip(null); setCopiedFrom(trip.id);
    setTripForm({ companyId: String(trip.companyId), busId: String(trip.busId), routeId: String(trip.routeId), departureTime: "", arrivalTime: "", price: String(trip.price), departureTerminalId: String(trip.departureTerminalId ?? ""), arrivalTerminalId: String(trip.arrivalTerminalId ?? ""), boardingInstructions: trip.boardingInstructions ?? "" });
    setError(""); setMessage(""); setShowTripForm(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const linkedDetail = useRef(new URLSearchParams(location.search).get("detail"));
  const openDetail = async (tripId: number, background = false) => {
    if (!background) setDetailLoading(true);
    if (!background) setDetail(null);
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

  useEffect(() => {
    const id = Number(linkedDetail.current);
    linkedDetail.current = null;
    if (Number.isSafeInteger(id) && id > 0) void openDetail(id);
  });

  const submitTrip = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    setError("");
    setMessage("");
    try {
      if (!tripForm.routeId || !tripForm.busId || !tripForm.departureTime || !tripForm.arrivalTime) {
        throw new Error("Güzergâh, otobüs, kalkış ve varış bilgileri zorunludur.");
      }
      const times = buildSchedule(tripForm.departureTime, tripForm.arrivalTime, editingTrip ? "single" : scheduleMode, repeatUntil, repeatDays, selectedDates);
      const payload = times.map(time => ({
        companyId: isAdmin && tripForm.companyId ? Number(tripForm.companyId) : null,
        busId: Number(tripForm.busId), routeId: Number(tripForm.routeId), price: Number(tripForm.price), ...time
        , departureTerminalId: tripForm.departureTerminalId ? Number(tripForm.departureTerminalId) : null,
        arrivalTerminalId: tripForm.arrivalTerminalId ? Number(tripForm.arrivalTerminalId) : null, boardingInstructions: tripForm.boardingInstructions.trim() || null
      }));
      const batch = !editingTrip && scheduleMode !== "single";
      const response = await request(editingTrip ? `${API_URL}/${editingTrip.id}` : batch ? `${API_URL}/batch` : API_URL, {
        method: editingTrip ? "PUT" : "POST", headers: requestHeaders(true),
        body: JSON.stringify(batch ? { trips: payload } : payload[0])
      });
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
      setMessage("Sefer iptal edildi. Satılmış biletler için tam iade talepleri kaydedildi; tamamlanma durumu sağlayıcı sonucuyla güncellenir.");
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
          <p>Seferleri, güzergâhları ve otobüs atamalarını yönetin. Durumlar kalkış ve varış saatlerine göre otomatik güncellenir.</p>
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
            {copiedFrom && <p className="trip-helper">Sefer #{copiedFrom} bilgileri kopyalandı. Yeni kalkış ve varış tarihlerini seçin. Biletler, yolcular ve önceki seferin durumu kopyalanmaz.</p>}
            {!editingTrip && <label>Oluşturma biçimi<select value={scheduleMode} disabled={saving} onChange={e => setScheduleMode(e.target.value as ScheduleMode)}><option value="single">Tek sefer</option><option value="weekly">Haftalık tekrar</option><option value="dates">Tarih seçerek toplu oluştur</option></select></label>}
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
                  onChange={(event) => setTripForm((current) => ({ ...current, routeId: event.target.value, departureTerminalId: "", arrivalTerminalId: "" }))}
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
              <label>Kalkış terminali<select required value={tripForm.departureTerminalId} onChange={event => setTripForm(current => ({ ...current, departureTerminalId: event.target.value }))}>
                <option value="">Kalkış terminali seçin</option>{terminals.filter(terminal => terminal.city.toLocaleLowerCase("tr-TR") === routes.find(route => route.id === Number(tripForm.routeId))?.departureCity.toLocaleLowerCase("tr-TR")).map(terminal => <option key={terminal.id} value={terminal.id}>{terminal.name} · {terminal.address}</option>)}
              </select></label>
              <label>Varış terminali<select required value={tripForm.arrivalTerminalId} onChange={event => setTripForm(current => ({ ...current, arrivalTerminalId: event.target.value }))}>
                <option value="">Varış terminali seçin</option>{terminals.filter(terminal => terminal.city.toLocaleLowerCase("tr-TR") === routes.find(route => route.id === Number(tripForm.routeId))?.arrivalCity.toLocaleLowerCase("tr-TR")).map(terminal => <option key={terminal.id} value={terminal.id}>{terminal.name} · {terminal.address}</option>)}
              </select></label>
              <label>Biniş açıklaması<textarea maxLength={1000} value={tripForm.boardingInstructions} onChange={event => setTripForm(current => ({ ...current, boardingInstructions: event.target.value }))} /></label>
              {tripForm.routeId && !terminals.length && <p>Terminal bulunmuyor. Önce yönetici terminal eklemelidir.</p>}
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
                Tüm saatler Türkiye saatidir. Yeni seferler <strong>Planlandı</strong> durumunda oluşturulur.
              </div>
            </div>
            {!editingTrip && scheduleMode !== "single" && <section className="trip-repeat" aria-labelledby="trip-repeat-title">
              <h3 id="trip-repeat-title">Sefer takvimi</h3>
              <p>Kalkış ve varış alanları ilk seferin saatini ve yolculuk süresini belirler. Aynı otobüs, güzergâh, terminal ve fiyat seçilen tarihlere uygulanır. Bir işlemde en fazla 100 sefer kaydedilir.</p>
              {scheduleMode === "weekly" ? <>
                <label>Tekrar bitiş tarihi<input type="date" required min={tripForm.departureTime.slice(0, 10)} value={repeatUntil} disabled={saving} onChange={e => setRepeatUntil(e.target.value)} /></label>
                <fieldset className="trip-weekdays"><legend>Tekrar günleri</legend>{weekDays.map(([value, label]) => <label key={value}><input type="checkbox" checked={repeatDays.includes(value)} disabled={saving} onChange={e => setRepeatDays(days => e.target.checked ? [...days, value] : days.filter(d => d !== value))} />{label}</label>)}</fieldset>
              </> : <>
                <div className="trip-repeat-controls"><label>Sefer tarihi<input type="date" value={dateToAdd} disabled={saving} onChange={e => setDateToAdd(e.target.value)} /></label><button type="button" className="secondary-button" disabled={saving || !dateToAdd || selectedDates.length >= 100} onClick={() => { setSelectedDates(dates => [...new Set([...dates, dateToAdd])].sort()); setDateToAdd(""); }}>Tarihi ekle</button></div>
                <ul className="trip-date-chips">{selectedDates.map(date => <li key={date}>{date.split("-").reverse().join(".")}<button className="link-button" type="button" disabled={saving} aria-label={`${date} tarihini kaldır`} onClick={() => setSelectedDates(dates => dates.filter(d => d !== date))}>Kaldır</button></li>)}</ul>
              </>}
              {schedulePreview.error && tripForm.departureTime && tripForm.arrivalTime && <p role="status">{schedulePreview.error}</p>}
              {schedulePreview.items.length > 0 && <><p><strong>{schedulePreview.items.length} sefer oluşturulacak.</strong> Kaydetmeden önce tarihleri kontrol edin. Çakışma varsa hiçbir sefer eklenmez.</p><div className="trip-schedule-preview"><table><caption>Oluşturulacak seferler (Türkiye saati)</caption><thead><tr><th>Kalkış</th><th>Varış</th></tr></thead><tbody>{schedulePreview.items.map(item => <tr key={item.departureTime}><td>{formatDate(item.departureTime)}</td><td>{formatDate(item.arrivalTime)}</td></tr>)}</tbody></table></div></>}
            </section>}
            <div className="detail-actions">
              <button className="primary-button" type="submit" disabled={saving || routes.length === 0 || (!editingTrip && scheduleMode !== "single" && schedulePreview.items.length === 0)}>
                {saving ? "Kaydediliyor..." : !editingTrip && scheduleMode !== "single" ? `${schedulePreview.items.length} Seferi Oluştur` : "Kaydet"}
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
                          <button className="link-button" type="button" onClick={() => copyTrip(trip)}>Kopyala</button>
                          {normalizeStatus(trip.status) === "Scheduled" && new Date(trip.departureTime) > new Date() && (
                            <>
                              <button className="link-button" type="button" onClick={() => editTrip(trip)}>Düzenle</button>
                              <button className="link-button trip-cancel-link" type="button" onClick={() => { setCancelTrip(trip); setCancelReason(""); }}>
                                İptal
                              </button>
                            </>
                          )}
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
              <p className="trip-helper">Boş koltuk sayısı ödeme tutmaları ve satılmış biletlerle güncellenir. Eski ücretsiz kayıtlar korunur; ödenmiş bilet sayılmaz.</p>
              <JourneyStops journey={detail} />
              {normalizeStatus(detail.status) === "Scheduled" && <TripChangeEditor key={detail.id} trip={detail} buses={buses.filter(bus => bus.companyId === detail.companyId && bus.isActive)} saved={() => { void openDetail(detail.id, true); void loadTrips(); void loadSummary(); }} />}
              {normalizeStatus(detail.status) === "Scheduled" && <TripSeatEditor key={`${detail.id}:${detail.updatedAt}`} id={detail.id} saved={() => { void openDetail(detail.id, true); void loadTrips(); void loadSummary(); }} />}
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
            <p>İptal edilen sefer silinmez ve yeni satışa kapatılır. İlgili aktif biletler iptal edilir ve iki saat kuralından bağımsız tam iade süreci başlatılır. İadenin tamamlanması sağlayıcı sonucuyla takip edilir; yolcu tercihlerine göre bildirimler kuyruğa alınır.</p>
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
