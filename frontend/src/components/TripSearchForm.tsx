import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { getTodayInTurkey } from "../dateUtils";

type RouteOption = {
  departureCity: string;
  arrivalCity: string;
};

type TripSearchFormProps = {
  initialDeparture?: string;
  initialArrival?: string;
  initialDate?: string;
  initialPassengers?: number;
  initialTripType?: "one-way" | "round-trip";
  initialReturnDate?: string;
};

const normalizeCity = (city: string) => city.trim().toLocaleLowerCase("tr-TR");

function TripSearchForm({
  initialDeparture = "",
  initialArrival = "",
  initialDate = getTodayInTurkey(),
  initialPassengers = 1,
  initialTripType = "one-way",
  initialReturnDate = "",
}: TripSearchFormProps) {
  const navigate = useNavigate();
  const [routes, setRoutes] = useState<RouteOption[]>([]);
  const [departureCity, setDepartureCity] = useState(initialDeparture);
  const [arrivalCity, setArrivalCity] = useState(initialArrival);
  const [date, setDate] = useState(initialDate);
  const [passengers, setPassengers] = useState(String(initialPassengers));
  const [tripType, setTripType] = useState<"one-way" | "round-trip">(initialTripType);
  const [returnDate, setReturnDate] = useState(initialReturnDate);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [routesError, setRoutesError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    const loadRoutes = async () => {
      try {
        const response = await fetch(apiUrl("/api/routes/public"), { signal: controller.signal });
        if (!response.ok) {
          throw new Error("Şehir seçenekleri şu anda yüklenemiyor.");
        }
        const data = (await response.json()) as RouteOption[];
        if (!Array.isArray(data)) {
          throw new Error("Güzergâh verileri beklenen biçimde alınamadı.");
        }
        setRoutes(data);
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setRoutesError(error instanceof Error ? error.message : "Şehir seçenekleri yüklenemedi.");
      }
    };

    void loadRoutes();
    return () => controller.abort();
  }, []);

  const departureCities = useMemo(
    () => [...new Set(routes.map((route) => route.departureCity))],
    [routes],
  );
  const arrivalCities = useMemo(
    () => [...new Set(routes
      .filter((route) => !departureCity || normalizeCity(route.departureCity) === normalizeCity(departureCity))
      .map((route) => route.arrivalCity))],
    [departureCity, routes],
  );

  const validate = () => {
    const nextErrors: Record<string, string> = {};
    const knownDeparture = departureCities.some((city) => normalizeCity(city) === normalizeCity(departureCity));
    const knownArrival = arrivalCities.some((city) => normalizeCity(city) === normalizeCity(arrivalCity));
    const passengerCount = Number(passengers);
    const earliestDate = getTodayInTurkey();

    if (!departureCity.trim()) nextErrors.departureCity = "Kalkış şehrini seçin.";
    else if (!knownDeparture) nextErrors.departureCity = "Listeden geçerli bir kalkış şehri seçin.";
    if (!arrivalCity.trim()) nextErrors.arrivalCity = "Varış şehrini seçin.";
    else if (!knownArrival) nextErrors.arrivalCity = "Seçilen kalkış şehrine uygun bir varış seçin.";
    else if (normalizeCity(departureCity) === normalizeCity(arrivalCity)) {
      nextErrors.arrivalCity = "Kalkış ve varış şehirleri aynı olamaz.";
    }
    if (!date) nextErrors.date = "Gidiş tarihi zorunludur.";
    else if (date < earliestDate) nextErrors.date = "Geçmiş bir tarih seçemezsiniz.";
    if (!Number.isInteger(passengerCount) || passengerCount < 1 || passengerCount > 10) {
      nextErrors.passengers = "Yolcu sayısı 1 ile 10 arasında olmalıdır.";
    }
    if (tripType === "round-trip") {
      if (!returnDate) nextErrors.returnDate = "Dönüş tarihi zorunludur.";
      else if (returnDate < date) nextErrors.returnDate = "Dönüş tarihi gidiş tarihinden önce olamaz.";
    }
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!validate()) return;
    setSubmitting(true);

    const params = new URLSearchParams({
      from: departureCities.find((city) => normalizeCity(city) === normalizeCity(departureCity)) ?? departureCity.trim(),
      to: arrivalCities.find((city) => normalizeCity(city) === normalizeCity(arrivalCity)) ?? arrivalCity.trim(),
      date,
      passengers,
      tripType,
    });
    if (tripType === "round-trip") params.set("returnDate", returnDate);
    navigate(`/search?${params.toString()}`);
  };

  const swapCities = () => {
    setDepartureCity(arrivalCity);
    setArrivalCity(departureCity);
    setErrors({});
  };

  return (
    <form className="trip-search-form" onSubmit={handleSubmit} noValidate>
      <div className="trip-search-topline">
        <div className="trip-type-options" role="group" aria-label="Yolculuk türü">
          <button type="button" className={tripType === "one-way" ? "selected" : ""} onClick={() => setTripType("one-way")}>Tek yön</button>
          <button type="button" className={tripType === "round-trip" ? "selected" : ""} onClick={() => setTripType("round-trip")}>Gidiş-dönüş</button>
        </div>
        <label className="passenger-field">
          Yolcu
          <input
            aria-label="Yolcu sayısı"
            type="number"
            min="1"
            max="10"
            step="1"
            value={passengers}
            onChange={(event) => setPassengers(event.target.value)}
            aria-invalid={Boolean(errors.passengers)}
            aria-describedby={errors.passengers ? "passengers-error" : undefined}
          />
        </label>
      </div>

      <div className="trip-search-fields">
        <div className="trip-search-field">
          <label htmlFor="departure-city">Nereden</label>
          <input
            id="departure-city"
            list="departure-cities"
            value={departureCity}
            onChange={(event) => {
              setDepartureCity(event.target.value);
              setArrivalCity("");
            }}
            placeholder="Kalkış şehri"
            autoComplete="off"
            aria-invalid={Boolean(errors.departureCity)}
            aria-describedby={errors.departureCity ? "departure-error" : undefined}
          />
          <datalist id="departure-cities">
            {departureCities.map((city) => <option value={city} key={city} />)}
          </datalist>
          {errors.departureCity && <span className="field-error" id="departure-error">{errors.departureCity}</span>}
        </div>

        <button type="button" className="swap-cities-button" onClick={swapCities} aria-label="Kalkış ve varış şehirlerini değiştir">⇄</button>

        <div className="trip-search-field">
          <label htmlFor="arrival-city">Nereye</label>
          <input
            id="arrival-city"
            list="arrival-cities"
            value={arrivalCity}
            onChange={(event) => setArrivalCity(event.target.value)}
            placeholder="Varış şehri"
            autoComplete="off"
            aria-invalid={Boolean(errors.arrivalCity)}
            aria-describedby={errors.arrivalCity ? "arrival-error" : undefined}
          />
          <datalist id="arrival-cities">
            {arrivalCities.map((city) => <option value={city} key={city} />)}
          </datalist>
          {errors.arrivalCity && <span className="field-error" id="arrival-error">{errors.arrivalCity}</span>}
        </div>

        <div className="trip-search-field">
          <label htmlFor="departure-date">Gidiş tarihi</label>
          <input
            id="departure-date"
            type="date"
            min={getTodayInTurkey()}
            value={date}
            onChange={(event) => setDate(event.target.value)}
            aria-invalid={Boolean(errors.date)}
            aria-describedby={errors.date ? "date-error" : undefined}
          />
          {errors.date && <span className="field-error" id="date-error">{errors.date}</span>}
        </div>

        {tripType === "round-trip" && (
          <div className="trip-search-field">
            <label htmlFor="return-date">Dönüş tarihi</label>
            <input
              id="return-date"
              type="date"
              min={date || getTodayInTurkey()}
              value={returnDate}
              onChange={(event) => setReturnDate(event.target.value)}
              aria-invalid={Boolean(errors.returnDate)}
              aria-describedby={errors.returnDate ? "return-date-error" : undefined}
            />
            {errors.returnDate && <span className="field-error" id="return-date-error">{errors.returnDate}</span>}
          </div>
        )}

        <button type="submit" className="trip-search-submit" disabled={submitting}>{submitting ? "Yönlendiriliyor..." : <>Sefer Ara <span aria-hidden="true">→</span></>}</button>
      </div>
      {errors.passengers && <span className="field-error passenger-error" id="passengers-error">{errors.passengers}</span>}
      {routesError && <p className="form-message-error" role="alert">{routesError}</p>}
    </form>
  );
}

export default TripSearchForm;
