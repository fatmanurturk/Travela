import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { getTodayInTurkey } from "../dateUtils";

import CityCombobox from "./CityCombobox";
import { compareCityNames } from "../citySearch";
import type { CityOption } from "../citySearch";

type CitySelection = { text: string; selected?: CityOption };

type TripSearchFormProps = {
  initialDeparture?: string;
  initialArrival?: string;
  initialDate?: string;
  initialPassengers?: number;
  initialTripType?: "one-way" | "round-trip";
  initialReturnDate?: string;
};

function TripSearchForm({
  initialDeparture = "",
  initialArrival = "",
  initialDate = getTodayInTurkey(),
  initialPassengers = 1,
  initialTripType = "one-way",
  initialReturnDate = "",
}: TripSearchFormProps) {
  const navigate = useNavigate();
  const [cities, setCities] = useState<CityOption[]>([]);
  const [departure, setDeparture] = useState<CitySelection>({ text: initialDeparture });
  const [arrival, setArrival] = useState<CitySelection>({ text: initialArrival });
  const editedCities = useRef({ departureCity: false, arrivalCity: false });
  const [date, setDate] = useState(initialDate);
  const [passengers, setPassengers] = useState(String(initialPassengers));
  const [tripType, setTripType] = useState<"one-way" | "round-trip">(initialTripType);
  const [returnDate, setReturnDate] = useState(initialReturnDate);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [citiesError, setCitiesError] = useState("");
  const [loadingCities, setLoadingCities] = useState(true);
  const [retryCities, setRetryCities] = useState(0);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    const loadCities = async () => {
      setLoadingCities(true);
      setCitiesError("");
      try {
        const response = await fetch(apiUrl("/api/routes/city-options"), { signal: controller.signal });
        if (!response.ok) throw new Error("İl listesi yüklenemedi. Yeniden deneyin.");
        const data = await response.json() as CityOption[];
        if (!Array.isArray(data) || data.length !== 81 ||
          data.some(city => !Number.isInteger(city?.id) || city.id < 1 || city.id > 81 || typeof city.name !== "string" || !city.name.trim()) ||
          new Set(data.map(city => city.id)).size !== 81 || new Set(data.map(city => city.name)).size !== 81) {
          throw new Error("İl listesi eksik veya geçersiz. Yeniden deneyin.");
        }
        if (controller.signal.aborted) return;
        setCities([...data].sort(compareCityNames));
        // Restore canonical selections from existing URLs, never from edited input.
        setDeparture(current => !editedCities.current.departureCity && current.text === initialDeparture
          ? { ...current, selected: data.find(city => city.name === initialDeparture) } : current);
        setArrival(current => !editedCities.current.arrivalCity && current.text === initialArrival
          ? { ...current, selected: data.find(city => city.name === initialArrival) } : current);
      } catch (error) {
        if (controller.signal.aborted) return;
        setCitiesError(error instanceof Error ? error.message : "İl listesi yüklenemedi.");
      } finally {
        if (!controller.signal.aborted) setLoadingCities(false);
      }
    };

    void loadCities();
    return () => controller.abort();
  }, [initialDeparture, initialArrival, retryCities]);

  const updateCity = (field: "departureCity" | "arrivalCity", value: CitySelection) => {
    editedCities.current[field] = true;
    if (field === "departureCity") setDeparture(value);
    else setArrival(value);
    setErrors(current => ({ ...current, [field]: "", ...(field === "departureCity" ? { arrivalCity: "" } : {}) }));
  };

  const validate = () => {
    const nextErrors: Record<string, string> = {};
    const passengerCount = Number(passengers);
    const earliestDate = getTodayInTurkey();

    if (!departure.text.trim()) nextErrors.departureCity = "Kalkış şehrini seçin.";
    else if (!departure.selected) nextErrors.departureCity = "Öneri listesinden bir kalkış ili seçin.";
    if (!arrival.text.trim()) nextErrors.arrivalCity = "Varış şehrini seçin.";
    else if (!arrival.selected) nextErrors.arrivalCity = "Öneri listesinden bir varış ili seçin.";
    else if (departure.selected?.id === arrival.selected.id) {
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
    if (!validate() || !departure.selected || !arrival.selected) return;
    setSubmitting(true);

    const params = new URLSearchParams({
      from: departure.selected.name,
      to: arrival.selected.name,
      fromId: String(departure.selected.id),
      toId: String(arrival.selected.id),
      date,
      passengers,
      tripType,
    });
    if (tripType === "round-trip") params.set("returnDate", returnDate);
    navigate(`/search?${params.toString()}`);
    setSubmitting(false);
  };

  const swapCities = () => {
    editedCities.current = { departureCity: true, arrivalCity: true };
    setDeparture(arrival);
    setArrival(departure);
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
        <CityCombobox label="Nereden" placeholder="Kalkış şehri" cities={cities}
          text={departure.text} selectedId={departure.selected?.id} loading={loadingCities}
          onTextChange={text => updateCity("departureCity", { text })}
          onSelect={city => updateCity("departureCity", { text: city.name, selected: city })}
          error={errors.departureCity} />

        <button type="button" className="swap-cities-button" onClick={swapCities} aria-label="Kalkış ve varış şehirlerini değiştir">⇄</button>

        <CityCombobox label="Nereye" placeholder="Varış şehri" cities={cities}
          text={arrival.text} selectedId={arrival.selected?.id} loading={loadingCities}
          onTextChange={text => updateCity("arrivalCity", { text })}
          onSelect={city => updateCity("arrivalCity", { text: city.name, selected: city })}
          error={errors.arrivalCity || (departure.selected && departure.selected.id === arrival.selected?.id
            ? "Kalkış ve varış şehirleri aynı olamaz." : undefined)} />

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
      {citiesError && <div className="form-message-error" role="alert">{citiesError} <button type="button" className="site-button secondary" onClick={() => setRetryCities(value => value + 1)}>İlleri yeniden yükle</button></div>}
    </form>
  );
}

export default TripSearchForm;
