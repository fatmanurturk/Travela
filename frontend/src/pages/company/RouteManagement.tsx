import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { apiUrl } from "../../api";
import { formatRouteDate, parseRouteDetail, parseRouteList, routeHttpError, totalRouteTrips } from "../../routeManagementData";
import type { RouteDetail as Detail, RouteRecord } from "../../routeManagementData";
import "../Admin.css";
import "./RouteManagement.css";
import RouteGeometryEditor from "../../components/RouteGeometryEditor";

type RouteForm = { departureCity: string; arrivalCity: string; distanceKm: string; hours: string; minutes: string; isActive: boolean };
type SortKey = "id" | "departureCity" | "arrivalCity" | "distanceKm" | "estimatedDurationMinutes" | "tripCount" | "isActive";
const emptyForm: RouteForm = { departureCity: "", arrivalCity: "", distanceKm: "", hours: "", minutes: "", isActive: true };
const pageSize = 10;
const lower = (value: string) => value.toLocaleLowerCase("tr-TR");
const duration = (value: number | null) => value == null ? "Belirtilmedi" : `${Math.floor(value / 60)} saat ${value % 60} dk`;
const statusLabel = (value: string) => ({ Scheduled: "Planlandı", InProgress: "Devam Ediyor", Completed: "Tamamlandı", Cancelled: "İptal Edildi" })[value] ?? value;

class RouteApiError extends Error {
  code?: string;
  constructor(message: string, code?: string) { super(message); this.code = code; }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = sessionStorage.getItem("travela_token");
  const response = await fetch(apiUrl(`/api/routes${path}`), { ...init, headers: {
    ...(init?.body ? { "Content-Type": "application/json" } : {}), Authorization: `Bearer ${token ?? ""}`,
  } });
  if (response.status === 401) {
    sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user");
    throw new Error(routeHttpError(401, null));
  }
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new RouteApiError(body?.errors ? Object.values(body.errors).flat().join(" ") : routeHttpError(response.status, body), body?.code);
  return body as T;
}

export default function RouteManagement() {
  let isAdmin = false;
  try { isAdmin = JSON.parse(sessionStorage.getItem("travela_user") || "null")?.role === "Admin"; } catch { /* ProtectedRoute handles invalid sessions. */ }
  const [routes, setRoutes] = useState<RouteRecord[]>([]);
  const [cities, setCities] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [search, setSearch] = useState("");
  const [departure, setDeparture] = useState("");
  const [arrival, setArrival] = useState("");
  const [status, setStatus] = useState("");
  const [sort, setSort] = useState<SortKey>("id");
  const [ascending, setAscending] = useState(false);
  const [page, setPage] = useState(1);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<RouteRecord | null>(null);
  const [form, setForm] = useState<RouteForm>(emptyForm);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const detailRequest = useRef(0);
  const detailSection = useRef<HTMLElement>(null);
  const formSection = useRef<HTMLElement>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true); setError("");
    try {
      const [data, cityData] = await Promise.all([
        request<unknown>("?includeInactive=true", { signal }).then(parseRouteList),
        isAdmin ? request<string[]>("/cities", { signal }) : Promise.resolve([] as string[]),
      ]);
      if (!signal?.aborted) { setRoutes(data); setCities(cityData); setLoaded(true); }
    } catch (cause) { if (!signal?.aborted) { setLoaded(false); setRoutes([]); setError(cause instanceof Error ? cause.message : "Güzergâhlar yüklenemedi."); } }
    finally { if (!signal?.aborted) setLoading(false); }
  }, [isAdmin]);
  useEffect(() => {
    const controller = new AbortController(); void load(controller.signal);
    return () => controller.abort();
  }, [isAdmin, load]);

  const filtered = useMemo(() => routes.filter(route =>
    (!departure || route.departureCity === departure) && (!arrival || route.arrivalCity === arrival) &&
    (!status || String(route.isActive) === status) && (!search.trim() ||
      lower(`${route.id} ${route.departureCity} ${route.arrivalCity}`).includes(lower(search.trim()))))
    .sort((a, b) => {
      const av = a[sort], bv = b[sort];
      const comparison = typeof av === "string" && typeof bv === "string" ? av.localeCompare(bv, "tr") : Number(av ?? -1) - Number(bv ?? -1);
      return (ascending ? 1 : -1) * (comparison || a.id - b.id);
    }), [routes, departure, arrival, status, search, sort, ascending]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const displayed = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const cityOptions = (key: "departureCity" | "arrivalCity") => [...new Set(routes.map(route => route[key]))].sort((a, b) => a.localeCompare(b, "tr"));
  const clearFilters = () => { setSearch(""); setDeparture(""); setArrival(""); setStatus(""); setPage(1); };
  const changeSort = (key: SortKey) => { setAscending(sort === key ? !ascending : true); setSort(key); setPage(1); };
  const openForm = (route: RouteRecord | null) => {
    setEditing(route); setShowForm(true); setError(""); setMessage("");
    setForm(route ? { departureCity: route.departureCity, arrivalCity: route.arrivalCity, distanceKm: String(route.distanceKm ?? ""),
      hours: route.estimatedDurationMinutes == null ? "" : String(Math.floor(route.estimatedDurationMinutes / 60)),
      minutes: route.estimatedDurationMinutes == null ? "" : String(route.estimatedDurationMinutes % 60), isActive: route.isActive } : { ...emptyForm });
    requestAnimationFrame(() => { formSection.current?.scrollIntoView({ behavior: "smooth", block: "start" }); formSection.current?.focus(); });
  };
  const openDetail = async (id: number, pageNumber = 1) => {
    if (!Number.isSafeInteger(id) || id < 1) { setError("Geçerli bir güzergâh seçin."); return; }
    const requestId = ++detailRequest.current; setDetail(null); setDetailLoading(true); setError("");
    try {
      const data = parseRouteDetail(await request<unknown>(`/${id}?pageNumber=${pageNumber}`), id);
      if (requestId === detailRequest.current) { setDetail(data); requestAnimationFrame(() => { detailSection.current?.scrollIntoView({ behavior: "smooth", block: "start" }); detailSection.current?.focus(); }); }
    } catch (cause) { if (requestId === detailRequest.current) setError(cause instanceof Error ? cause.message : "Detaylar yüklenemedi."); }
    finally { if (requestId === detailRequest.current) setDetailLoading(false); }
  };
  const mutate = async (path: string, method: string, body: object) => {
    try { return await request<{ message: string }>(path, { method, body: JSON.stringify(body) }); }
    catch (cause) {
      if (cause instanceof RouteApiError && cause.code === "route_deactivation_confirmation_required" && window.confirm(cause.message))
        return request<{ message: string }>(path, { method, body: JSON.stringify({ ...body, confirmDeactivation: true }) });
      if (cause instanceof RouteApiError && cause.code === "route_deactivation_confirmation_required") return null;
      throw cause;
    }
  };
  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setError(""); setMessage("");
    const departureCity = cities.find(city => lower(city) === lower(form.departureCity.trim()));
    const arrivalCity = cities.find(city => lower(city) === lower(form.arrivalCity.trim()));
    if ((!departureCity || !arrivalCity) && !editing?.hasTrips) { setError("Kalkış ve varış için listeden geçerli bir il seçin."); return; }
    if (lower(form.departureCity.trim()) === lower(form.arrivalCity.trim())) { setError("Kalkış ve varış şehirleri aynı olamaz."); return; }
    const minutes = form.hours === "" && form.minutes === "" ? null : Number(form.hours || 0) * 60 + Number(form.minutes || 0);
    if (minutes !== null && (!Number.isInteger(minutes) || minutes <= 0 || minutes > 100000)) { setError("Tahmini süre 1 ile 100000 dakika arasında olmalıdır."); return; }
    setSaving(true);
    try {
      const result = await mutate(editing ? `/${editing.id}` : "", editing ? "PUT" : "POST", {
        departureCity: editing?.hasTrips ? form.departureCity : departureCity,
        arrivalCity: editing?.hasTrips ? form.arrivalCity : arrivalCity,
        distanceKm: form.distanceKm === "" ? null : Number(form.distanceKm), estimatedDurationMinutes: minutes, isActive: form.isActive,
      });
      if (result) { setShowForm(false); setEditing(null); setMessage(result.message); clearFilters(); await load(); if (detail) await openDetail(detail.route.id, detail.pageNumber); }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Güzergâh kaydedilemedi."); }
    finally { setSaving(false); }
  };
  const toggle = async (route: RouteRecord) => {
    if (route.isActive && route.plannedTripCount === 0 && !window.confirm(`${route.departureCity} → ${route.arrivalCity} güzergâhı pasife alınsın mı? Yeni sefer oluşturulamaz. Mevcut kayıtlar korunur.`)) return;
    setSaving(true); setError(""); setMessage("");
    try {
      const result = await mutate(`/${route.id}/status`, "PATCH", { isActive: !route.isActive });
      if (result) { setMessage(result.message); await load(); if (detail?.route.id === route.id) await openDetail(route.id, detail.pageNumber); }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Durum değiştirilemedi."); }
    finally { setSaving(false); }
  };
  const cards = [["Toplam Güzergâh", routes.length], ["Aktif Güzergâhlar", routes.filter(r => r.isActive).length],
    ["Pasif Güzergâhlar", routes.filter(r => !r.isActive).length], ["Güzergâhlara Bağlı Toplam Sefer", totalRouteTrips(routes)]] as const;
  const columns: [SortKey, string][] = [["id", "Güzergâh ID"], ["departureCity", "Kalkış Şehri"], ["arrivalCity", "Varış Şehri"],
    ["distanceKm", "Mesafe"], ["estimatedDurationMinutes", "Tahmini Süre"], ["tripCount", "Bağlı Sefer"], ["isActive", "Durum"]];

  return <div className="admin-dashboard route-management">
    <div className="dashboard-heading route-heading"><div><h1>Güzergâh Yönetimi</h1><p>Otobüs seferlerinde kullanılacak güzergâhları oluşturun, düzenleyin ve yönetin.</p></div>
      {isAdmin && <button type="button" className="primary-button" disabled={saving || cities.length === 0} onClick={() => openForm(null)}>+ Yeni Güzergâh Ekle</button>}</div>
    <div className="dashboard-stats route-stats">{cards.map(([title, value]) => <div className="stat-card" key={title}><p>{title}</p><h2>{loaded ? value : "—"}</h2></div>)}</div>
    {!isAdmin && <p className="route-notice">Güzergâhlar sistem genelinde ortaktır. Oluşturma, düzenleme ve durum değiştirme işlemlerini Admin yapabilir. Sefer sayıları ve detayları yalnızca firmanıza aittir.</p>}
    {error && <p role="alert" className="form-alert error">{error} {!sessionStorage.getItem("travela_token") && <Link to="/login?returnTo=%2Fcompany%2Froutes">Giriş yap</Link>}</p>}
    {message && <p role="status" className="form-alert success">{message}</p>}
    {isAdmin && showForm && <section ref={formSection} tabIndex={-1} className="dashboard-section form-section" aria-labelledby="route-form-title">
      <h2 id="route-form-title">{editing ? `Güzergâh #${editing.id} Düzenle` : "Yeni Güzergâh Oluştur"}</h2>
      {editing?.hasTrips && <p className="route-notice">Bu ortak güzergâha bağlı seferler bulunduğu için kalkış ve varış şehirleri değiştirilemez. Mesafe, tahmini süre ve durum güncellenebilir.</p>}
      <form className="user-form" onSubmit={save}><fieldset disabled={saving}><div className="form-grid">
        <label>Kalkış Şehri *<input required list="route-departure-cities" autoComplete="off" disabled={editing?.hasTrips} value={form.departureCity} onChange={e => setForm({ ...form, departureCity: e.target.value })} /></label>
        <datalist id="route-departure-cities">{cities.filter(city => lower(city) !== lower(form.arrivalCity)).map(city => <option key={city} value={city} />)}</datalist>
        <label>Varış Şehri *<input required list="route-arrival-cities" autoComplete="off" disabled={editing?.hasTrips} value={form.arrivalCity} onChange={e => setForm({ ...form, arrivalCity: e.target.value })} /></label>
        <datalist id="route-arrival-cities">{cities.filter(city => lower(city) !== lower(form.departureCity)).map(city => <option key={city} value={city} />)}</datalist>
        <label>Mesafe (km)<input type="number" min="1" max="100000" step="1" value={form.distanceKm} onChange={e => setForm({ ...form, distanceKm: e.target.value })} /></label>
        <label>Tahmini Süre (saat)<input type="number" min="0" max="1666" step="1" value={form.hours} onChange={e => setForm({ ...form, hours: e.target.value })} /></label>
        <label>Tahmini Süre (dakika)<input type="number" min="0" max="59" step="1" value={form.minutes} onChange={e => setForm({ ...form, minutes: e.target.value })} /></label>
        <label>Durum<select value={String(form.isActive)} onChange={e => setForm({ ...form, isActive: e.target.value === "true" })}><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
      </div><div className="route-actions"><button className="primary-button" type="submit">{saving ? "Kaydediliyor..." : "Kaydet"}</button><button className="secondary-button" type="button" onClick={() => setShowForm(false)}>İptal</button></div></fieldset></form>
    </section>}
    <section className="dashboard-section" aria-labelledby="route-list-title" aria-busy={loading}>
      <div className="section-heading"><h2 id="route-list-title">Güzergâh Listesi</h2><button className="secondary-button" disabled={loading || saving} onClick={() => void load()}>Yenile</button></div>
      <div className="route-filter-grid">
        <label>Genel arama<input type="search" placeholder="Güzergâh ID veya şehir adı" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} /></label>
        <label>Kalkış şehri<select value={departure} onChange={e => { setDeparture(e.target.value); setPage(1); }}><option value="">Tüm şehirler</option>{cityOptions("departureCity").map(city => <option key={city}>{city}</option>)}</select></label>
        <label>Varış şehri<select value={arrival} onChange={e => { setArrival(e.target.value); setPage(1); }}><option value="">Tüm şehirler</option>{cityOptions("arrivalCity").map(city => <option key={city}>{city}</option>)}</select></label>
        <label>Durum filtresi<select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }}><option value="">Tümü</option><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
      </div><div className="filter-actions"><span className="result-count">Toplam {filtered.length} sonuç</span><button type="button" className="secondary-button" onClick={clearFilters}>Filtreleri Temizle</button></div>
      {loading ? <p className="empty-state" role="status">Güzergâhlar yükleniyor...</p> : !loaded ? <p className="empty-state">Veriler alınamadı. Yenile düğmesiyle tekrar deneyebilirsiniz.</p> : displayed.length === 0 ? <p className="empty-state">{routes.length ? "Filtrelerle eşleşen güzergâh bulunamadı." : "Henüz güzergâh bulunmuyor. Güzergâh eklemek için Admin ile iletişime geçin."}</p> : <div className="table-wrap" tabIndex={0} role="region" aria-label="Güzergâh tablosu; yatay kaydırılabilir"><table className="data-table route-table">
        <thead><tr>{columns.map(([key, label]) => <th key={key} aria-sort={sort === key ? ascending ? "ascending" : "descending" : "none"}><button type="button" className="sort-button" onClick={() => changeSort(key)}>{label} {sort === key ? ascending ? "↑" : "↓" : "↕"}</button></th>)}<th>İşlemler</th></tr></thead>
        <tbody>{displayed.map(route => <tr key={route.id}><td>#{route.id}</td><td>{route.departureCity}</td><td>{route.arrivalCity}</td><td>{route.distanceKm == null ? "Belirtilmedi" : `${route.distanceKm.toLocaleString("tr-TR")} km`}</td><td>{duration(route.estimatedDurationMinutes)}</td><td>{route.tripCount}</td><td><span className={`route-badge ${route.isActive ? "active" : "inactive"}`}>{route.isActive ? "Aktif" : "Pasif"}</span></td><td><div className="action-group"><button className="link-button" disabled={saving} onClick={() => void openDetail(route.id)}>Detay</button>{isAdmin && <><button className="link-button" disabled={saving} onClick={() => openForm(route)}>Düzenle</button><button className="link-button" disabled={saving} onClick={() => void toggle(route)}>{route.isActive ? "Pasife Al" : "Aktif Yap"}</button></>}</div></td></tr>)}</tbody>
      </table></div>}
      <div className="pagination-row"><span>Sayfa {currentPage} / {totalPages}</span><div><button disabled={loading || currentPage <= 1} onClick={() => setPage(currentPage - 1)}>Önceki</button><button disabled={loading || currentPage >= totalPages} onClick={() => setPage(currentPage + 1)}>Sonraki</button></div></div>
    </section>
    {(detail || detailLoading) && <section ref={detailSection} tabIndex={-1} className="dashboard-section route-detail" aria-labelledby="route-detail-title" aria-busy={detailLoading}>
      <div className="section-heading"><h2 id="route-detail-title">Güzergâh {detail ? `#${detail.route.id} ` : ""}Detayları</h2><button className="secondary-button" onClick={() => { detailRequest.current++; setDetail(null); setDetailLoading(false); }}>Kapat</button></div>
      {detailLoading ? <p role="status">Güzergâh detayları yükleniyor...</p> : detail && <>
        <dl className="route-detail-grid">{[["Güzergâh ID", `#${detail.route.id}`], ["Kalkış şehri", detail.route.departureCity], ["Varış şehri", detail.route.arrivalCity], ["Mesafe", detail.route.distanceKm == null ? "Belirtilmedi" : `${detail.route.distanceKm} km`], ["Tahmini yolculuk süresi", duration(detail.route.estimatedDurationMinutes)], ["Durum", detail.route.isActive ? "Aktif" : "Pasif"], ["Bağlı sefer sayısı", detail.route.tripCount]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
        {isAdmin && <RouteGeometryEditor key={detail.route.id} id={detail.route.id} />}
        <h3>{isAdmin ? "Güzergâha bağlı seferler" : "Firmanızın bu güzergâhtaki seferleri"}</h3>
        {detail.trips.length === 0 ? <p className="empty-state">{isAdmin ? "Bu güzergâha bağlı sefer bulunmuyor." : "Firmanızın bu güzergâhta seferi bulunmuyor."}</p> : <div className="table-wrap" tabIndex={0} role="region" aria-label="Güzergâha bağlı seferler"><table className="data-table"><thead><tr><th>Sefer ID</th><th>Kalkış Tarihi</th><th>Varış Tarihi</th><th>Otobüs</th><th>Fiyat</th><th>Sefer Durumu</th></tr></thead><tbody>{detail.trips.map(trip => <tr key={trip.id}><td>#{trip.id}</td><td>{formatRouteDate(trip.departureTime)}</td><td>{formatRouteDate(trip.arrivalTime)}</td><td>{trip.busPlateNumber}</td><td>{trip.price.toLocaleString("tr-TR", { style: "currency", currency: "TRY" })}</td><td>{statusLabel(trip.status)}</td></tr>)}</tbody></table></div>}
        <div className="pagination-row"><span>Sayfa {detail.pageNumber} / {detail.totalPages}</span><div><button disabled={detail.pageNumber <= 1} onClick={() => void openDetail(detail.route.id, detail.pageNumber - 1)}>Önceki seferler</button><button disabled={detail.pageNumber >= detail.totalPages} onClick={() => void openDetail(detail.route.id, detail.pageNumber + 1)}>Sonraki seferler</button></div></div>
      </>}
    </section>}
  </div>;
}
