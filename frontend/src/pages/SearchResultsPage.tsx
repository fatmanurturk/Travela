import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { apiUrl } from "../api";
import SiteHeader from "../components/SiteHeader";
import TripSearchForm from "../components/TripSearchForm";

type PublicTrip = {
  id: number;
  companyId: number;
  companyName: string;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  durationMinutes: number;
  price: number;
  availableSeats: number;
};

type CompanyOption = { id: number; name: string };
type TripResponse = {
  items: PublicTrip[];
  companies: CompanyOption[];
  totalCount: number;
  totalPages: number;
};
type LegData = {
  trips: PublicTrip[];
  companies: CompanyOption[];
  totalCount: number;
  totalPages: number;
  error: string;
};

const EMPTY_LEG: LegData = { trips: [], companies: [], totalCount: 0, totalPages: 0, error: "" };
const currencyFormatter = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const dateFormatter = new Intl.DateTimeFormat("tr-TR", {
  weekday: "long",
  day: "numeric",
  month: "long",
  timeZone: "Europe/Istanbul",
});
const timeFormatter = new Intl.DateTimeFormat("tr-TR", {
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "Europe/Istanbul",
});
const CURRENT_YEAR = new Date().getFullYear();

function localDate(value: string) {
  const date = new Date(value);
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(date);
}

function durationLabel(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return hours > 0 ? `${hours} sa ${remainingMinutes > 0 ? `${remainingMinutes} dk` : ""}`.trim() : `${remainingMinutes} dk`;
}

function SearchResultsPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const departureCity = searchParams.get("from") ?? "";
  const arrivalCity = searchParams.get("to") ?? "";
  const date = searchParams.get("date") ?? "";
  const returnDate = searchParams.get("returnDate") ?? "";
  const tripType = searchParams.get("tripType") === "round-trip" ? "round-trip" : "one-way";
  const allTrips = searchParams.get("all") === "true";
  const parsedPassengers = Number(searchParams.get("passengers") ?? 1);
  const passengers = Number.isInteger(parsedPassengers) ? parsedPassengers : 0;
  const companyId = searchParams.get("companyId") ?? "";
  const departureHourFrom = searchParams.get("departureHourFrom") ?? "";
  const departureHourTo = searchParams.get("departureHourTo") ?? "";
  const minPrice = searchParams.get("minPrice") ?? "";
  const maxPrice = searchParams.get("maxPrice") ?? "";
  const sortBy = searchParams.get("sortBy") ?? "departure";
  const parsedPage = Number(searchParams.get("page") ?? 1);
  const page = Number.isSafeInteger(parsedPage) && parsedPage > 0 ? parsedPage : 1;
  const outboundTripId = searchParams.get("outboundTrip") ?? "";
  const inboundTripId = searchParams.get("returnTrip") ?? "";

  const [legs, setLegs] = useState<{ outbound: LegData; inbound: LegData }>({
    outbound: EMPTY_LEG,
    inbound: EMPTY_LEG,
  });
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const [selectedTrips, setSelectedTrips] = useState<Record<number, PublicTrip>>({});
  const [selectionError, setSelectionError] = useState("");

  const validCriteria = (allTrips || Boolean(departureCity && arrivalCity && date)) &&
    passengers >= 1 && passengers <= 10;
  const outboundParams = useMemo(() => ({
    from: allTrips ? "" : departureCity,
    to: allTrips ? "" : arrivalCity,
    date: allTrips ? "" : date,
  }), [allTrips, arrivalCity, date, departureCity]);
  const inboundParams = useMemo(() => ({
    from: arrivalCity,
    to: departureCity,
    date: returnDate,
  }), [arrivalCity, departureCity, returnDate]);

  useEffect(() => {
    if (!validCriteria || (tripType === "round-trip" && !returnDate)) {
      return;
    }

    const controller = new AbortController();
    const loadLeg = async (leg: typeof outboundParams, direction: "outbound" | "inbound"): Promise<LegData> => {
      const params = new URLSearchParams({
        passengers: String(passengers),
        pageNumber: String(page),
        pageSize: "6",
        sortBy,
      });
      if (leg.from) params.set("departureCity", leg.from);
      if (leg.to) params.set("arrivalCity", leg.to);
      if (leg.date) params.set("date", leg.date);
      if (allTrips) params.set("all", "true");
      if (companyId) params.set("companyId", companyId);
      if (departureHourFrom) params.set("departureHourFrom", departureHourFrom);
      if (departureHourTo) params.set("departureHourTo", departureHourTo);
      if (minPrice) params.set("minPrice", minPrice);
      if (maxPrice) params.set("maxPrice", maxPrice);

      try {
        const response = await fetch(apiUrl(`/api/trips/search?${params}`), { signal: controller.signal });
        const body = await response.json() as TripResponse | { message?: string };
        if (!response.ok) {
          throw new Error("message" in body && body.message ? body.message : `${direction === "outbound" ? "Gidiş" : "Dönüş"} seferleri alınamadı.`);
        }
        if (!("items" in body) || !Array.isArray(body.items) || !Array.isArray(body.companies)) {
          throw new Error("Sefer verileri beklenen biçimde alınamadı.");
        }
        return {
          trips: body.items,
          companies: body.companies,
          totalCount: body.totalCount,
          totalPages: body.totalPages,
          error: "",
        };
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") return EMPTY_LEG;
        return {
          ...EMPTY_LEG,
          error: error instanceof Error ? error.message : "Seferler yüklenemedi.",
        };
      }
    };

    const load = async () => {
      setLoading(true);
      const outbound = loadLeg(outboundParams, "outbound");
      const inbound = tripType === "round-trip" ? loadLeg(inboundParams, "inbound") : Promise.resolve(EMPTY_LEG);
      const [outboundData, inboundData] = await Promise.all([outbound, inbound]);
      if (!controller.signal.aborted) {
        setLegs({ outbound: outboundData, inbound: inboundData });
        setLoading(false);
      }
    };
    void load();
    return () => controller.abort();
  }, [
    allTrips,
    companyId,
    date,
    departureCity,
    departureHourFrom,
    departureHourTo,
    inboundParams,
    maxPrice,
    minPrice,
    outboundParams,
    page,
    passengers,
    retry,
    sortBy,
    tripType,
    validCriteria,
    returnDate,
  ]);

  useEffect(() => {
    const ids = [outboundTripId, inboundTripId].filter(Boolean);
    if (ids.length === 0) return;
    const controller = new AbortController();
    const loadSelected = async () => {
      const selected = await Promise.all(ids.map(async (id) => {
        try {
          const response = await fetch(apiUrl(`/api/trips/public/${id}`), { signal: controller.signal });
          if (!response.ok) return { error: "Önceden seçilen sefer artık görüntülenemiyor." };
          return { trip: await response.json() as PublicTrip };
        } catch (error) {
          if (error instanceof DOMException && error.name === "AbortError") return {};
          return { error: error instanceof Error ? error.message : "Seçilen sefer bilgisi yüklenemedi." };
        }
      }));
      if (!controller.signal.aborted) {
        setSelectionError(selected.find((result) => result.error)?.error ?? "");
        setSelectedTrips((current) => ({
          ...current,
          ...Object.fromEntries(selected
            .flatMap((result) => result.trip ? [[result.trip.id, result.trip] as const] : [])),
        }));
      }
    };
    void loadSelected();
    return () => controller.abort();
  }, [inboundTripId, outboundTripId]);

  const companyOptions = useMemo(() => {
    const unique = new Map<number, CompanyOption>();
    [...legs.outbound.companies, ...legs.inbound.companies].forEach((company) => unique.set(company.id, company));
    return [...unique.values()].sort((a, b) => a.name.localeCompare(b.name, "tr"));
  }, [legs.inbound.companies, legs.outbound.companies]);

  const selectedOutbound = selectedTrips[Number(outboundTripId)];
  const selectedInbound = selectedTrips[Number(inboundTripId)];
  const totalPrice = (selectedOutbound?.price ?? 0) + (selectedInbound?.price ?? 0);

  const updateQuery = (key: string, value: string) => {
    const params = new URLSearchParams(searchParams);
    if (value) params.set(key, value);
    else params.delete(key);
    if (key === "departureHourFrom" && value && params.get("departureHourTo") &&
      Number(value) > Number(params.get("departureHourTo"))) {
      params.delete("departureHourTo");
    }
    if (key === "departureHourTo" && value && params.get("departureHourFrom") &&
      Number(value) < Number(params.get("departureHourFrom"))) {
      params.delete("departureHourFrom");
    }
    if (key === "minPrice" && value && params.get("maxPrice") &&
      Number(value) > Number(params.get("maxPrice"))) {
      params.delete("maxPrice");
    }
    if (key === "maxPrice" && value && params.get("minPrice") &&
      Number(value) < Number(params.get("minPrice"))) {
      params.delete("minPrice");
    }
    if (key !== "page") params.delete("page");
    setSearchParams(params);
  };

  const chooseTrip = (trip: PublicTrip, direction: "outbound" | "inbound") => {
    setSelectionError("");
    setSelectedTrips((current) => ({ ...current, [trip.id]: trip }));
    updateQuery(direction === "outbound" ? "outboundTrip" : "returnTrip", String(trip.id));
  };

  const clearFilters = () => {
    const params = new URLSearchParams(searchParams);
    ["companyId", "departureHourFrom", "departureHourTo", "minPrice", "maxPrice", "page"].forEach((key) => params.delete(key));
    params.delete("sortBy");
    setSearchParams(params);
  };

  const renderLeg = (leg: LegData, direction: "outbound" | "inbound", legDate: string) => {
    const selectedId = direction === "outbound" ? outboundTripId : inboundTripId;
    const title = direction === "outbound" ? "Gidiş seferleri" : "Dönüş seferleri";
    const from = direction === "outbound" ? departureCity : arrivalCity;
    const to = direction === "outbound" ? arrivalCity : departureCity;

    return (
      <section className="results-leg" key={direction}>
        <div className="results-leg-heading">
          <div><span className="eyebrow">{legDate ? dateFormatter.format(new Date(`${legDate}T12:00:00+03:00`)) : "Yaklaşan seferler"}</span><h2>{title}</h2></div>
          <span className="result-count">{loading ? "Aranıyor..." : `${leg.totalCount} sefer`}</span>
        </div>
        {leg.error ? (
          <div className="results-message error" role="alert">
            <p>{leg.error}</p>
            <button type="button" className="site-button secondary" onClick={() => setRetry((value) => value + 1)}>Yeniden dene</button>
          </div>
        ) : loading ? (
          <div className="site-empty-state" aria-live="polite">Seferler aranıyor...</div>
        ) : leg.totalCount === 0 ? (
          <div className="site-empty-state">
            <h3>{direction === "outbound" ? "Bu yönde uygun sefer bulunamadı." : "Dönüş yönünde uygun sefer bulunamadı."}</h3>
            <p>Tarih veya arama kriterlerini değiştirerek tekrar deneyin.</p>
            {!allTrips && <p className="empty-route">{from} → {to}</p>}
            {direction === "outbound" && <a className="site-button primary" href="/#sefer-ara">Aramayı değiştir</a>}
          </div>
        ) : (
          <>
            <div className="result-list">
              {leg.trips.map((trip) => {
                const crossesDay = localDate(trip.departureTime) !== localDate(trip.arrivalTime);
                const isSelected = selectedId === String(trip.id);
                return (
                  <article className={`result-card${isSelected ? " selected" : ""}`} key={trip.id}>
                    <div className="result-card-company"><span className="company-mark" aria-hidden="true">{trip.companyName.slice(0, 1)}</span><strong>{trip.companyName}</strong></div>
                    <div className="result-timeline">
                      <div><strong>{timeFormatter.format(new Date(trip.departureTime))}</strong><span>{trip.departureCity}</span><small>{dateFormatter.format(new Date(trip.departureTime))}</small></div>
                      <div className="timeline-duration"><span>{durationLabel(trip.durationMinutes)}</span><i /><small>{crossesDay ? "Ertesi gün varış" : "Direkt sefer"}</small></div>
                      <div><strong>{timeFormatter.format(new Date(trip.arrivalTime))}</strong><span>{trip.arrivalCity}</span><small>{dateFormatter.format(new Date(trip.arrivalTime))}{crossesDay ? " · +1 gün" : ""}</small></div>
                    </div>
                    <div className="result-card-price">
                      <span>{trip.availableSeats} boş koltuk</span>
                      <strong>{currencyFormatter.format(trip.price)}<small> / kişi</small></strong>
                      <span className="total-price">Toplam: {currencyFormatter.format(trip.price * passengers)}</span>
                    </div>
                    <div className="result-card-actions">
                      <Link className="result-detail-link" to={`/trips/${trip.id}`}>Sefer detayı</Link>
                      <button
                        type="button"
                        className={isSelected && tripType === "round-trip" ? "site-button selected-button" : "site-button primary"}
                        onClick={() => {
                          if (tripType === "round-trip") chooseTrip(trip, direction);
                          else navigate(`/trips/${trip.id}`);
                        }}
                      >
                        {isSelected && tripType === "round-trip" ? "Seçildi" : direction === "outbound" && tripType === "round-trip" ? "Gidişi seç" : direction === "inbound" ? "Dönüşü seç" : "Seferi incele / koltuk seç"}
                      </button>
                    </div>
                  </article>
                );
              })}
            </div>
            {leg.totalPages > 1 && (
              <div className="pagination">
                <button type="button" disabled={page <= 1} onClick={() => updateQuery("page", String(page - 1))}>Önceki</button>
                <span>Sayfa {page} / {leg.totalPages}</span>
                <button type="button" disabled={page >= leg.totalPages} onClick={() => updateQuery("page", String(page + 1))}>Sonraki</button>
              </div>
            )}
          </>
        )}
      </section>
    );
  };

  return (
    <div className="travela-site">
      <SiteHeader />
      <main className="results-page">
        <div className="results-intro">
          <Link to="/" className="back-home">← Ana sayfaya dön</Link>
          <span className="eyebrow">Sefer seçenekleri</span>
          <h1>{allTrips ? "Yaklaşan tüm seferler" : `${departureCity} → ${arrivalCity}`}</h1>
          <p>{allTrips ? "Aktif ve satışa açık seferleri inceleyin." : `${date ? dateFormatter.format(new Date(`${date}T12:00:00+03:00`)) : ""} · ${passengers} yolcu${tripType === "round-trip" && returnDate ? ` · dönüş ${dateFormatter.format(new Date(`${returnDate}T12:00:00+03:00`))}` : ""}`}</p>
        </div>

        <details className="change-search-panel">
          <summary>Aramayı değiştir</summary>
          <TripSearchForm
            key={`${departureCity}-${arrivalCity}-${date}-${returnDate}-${passengers}-${tripType}`}
            initialDeparture={departureCity}
            initialArrival={arrivalCity}
            initialDate={date}
            initialPassengers={passengers}
            initialTripType={tripType}
            initialReturnDate={returnDate}
          />
        </details>

        {!validCriteria || (tripType === "round-trip" && !returnDate) ? (
          <div className="site-empty-state">
            {passengers < 1 || passengers > 10
              ? "Yolcu sayısı bağlantıda geçersiz. 1 ile 10 arasında bir yolcu sayısıyla yeniden arayın."
              : "Sefer aramak için kalkış, varış ve tarih bilgilerini seçin."}
          </div>
        ) : (
          <>
            <div className="results-layout">
              <aside className="search-filters">
                <div className="filter-heading"><h2>Filtrele</h2><button type="button" onClick={clearFilters}>Temizle</button></div>
                <label>Firma
                  <select value={companyId} onChange={(event) => updateQuery("companyId", event.target.value)}>
                    <option value="">Tüm firmalar</option>
                    {companyOptions.map((company) => <option value={company.id} key={company.id}>{company.name}</option>)}
                  </select>
                </label>
                <fieldset><legend>Kalkış saati</legend>
                  <div className="filter-range">
                    <label>En erken<select value={departureHourFrom} onChange={(event) => updateQuery("departureHourFrom", event.target.value)}><option value="">Fark etmez</option>{Array.from({ length: 24 }, (_, hour) => <option value={hour} key={hour}>{String(hour).padStart(2, "0")}:00</option>)}</select></label>
                    <label>En geç<select value={departureHourTo} onChange={(event) => updateQuery("departureHourTo", event.target.value)}><option value="">Fark etmez</option>{Array.from({ length: 24 }, (_, hour) => <option value={hour} key={hour}>{String(hour).padStart(2, "0")}:00</option>)}</select></label>
                  </div>
                </fieldset>
                <fieldset><legend>Kişi başı fiyat (₺)</legend>
                  <div className="filter-range">
                    <label>En az<input type="number" min="0" value={minPrice} onChange={(event) => updateQuery("minPrice", event.target.value)} placeholder="0" /></label>
                    <label>En çok<input type="number" min="0" value={maxPrice} onChange={(event) => updateQuery("maxPrice", event.target.value)} placeholder="Fark etmez" /></label>
                  </div>
                </fieldset>
                <label>Sıralama
                  <select value={sortBy} onChange={(event) => updateQuery("sortBy", event.target.value)}>
                    <option value="departure">En erken kalkış</option>
                    <option value="price">En düşük fiyat</option>
                    <option value="duration">En kısa yolculuk</option>
                  </select>
                </label>
              </aside>

              <div className="results-content">
                {renderLeg(legs.outbound, "outbound", date)}
                {tripType === "round-trip" && renderLeg(legs.inbound, "inbound", returnDate)}
              </div>
            </div>
            {tripType === "round-trip" && (selectedOutbound || selectedInbound) && (
              <div className="round-trip-total">
                <div><strong>Seçilen seferler toplamı</strong><span>{selectedOutbound ? "Gidiş seçildi" : "Gidiş bekleniyor"} · {selectedInbound ? "Dönüş seçildi" : "Dönüş bekleniyor"}</span></div>
                <strong>{currencyFormatter.format(totalPrice * passengers)}</strong>
              </div>
            )}
            {selectionError && <p className="results-message error" role="alert">{selectionError}</p>}
          </>
        )}
      </main>
      <footer className="site-footer"><Link to="/" className="site-logo">Travela<span>.</span></Link><small>© {CURRENT_YEAR} Travela</small></footer>
    </div>
  );
}

export default SearchResultsPage;
