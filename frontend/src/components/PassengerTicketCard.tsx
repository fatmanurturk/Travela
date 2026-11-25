import { useState } from "react";
import type { PassengerDetails } from "../passengerDetails";
import type { Gender } from "../seatCompatibility";
import { isValidIdentityNumber } from "../identityNumber";
import { money } from "../reservations";

export type PassengerTicketJourney = { companyName: string; departureCity: string; arrivalCity: string; departureTime: string; price: number };
const departure = new Intl.DateTimeFormat("tr-TR", { dateStyle: "long", timeStyle: "short", timeZone: "Europe/Istanbul" });

export default function PassengerTicketCard({ seat, journey, passenger, gender, seatError, busy, hasProfile, change, useProfile, clear, remove }: {
  seat: number; journey: PassengerTicketJourney; passenger: PassengerDetails; gender?: Gender | ""; seatError?: string;
  busy: boolean; hasProfile: boolean;
  change: (field: keyof PassengerDetails, value: string) => void;
  useProfile: () => void; clear: () => void; remove: () => void;
}) {
  const [touched, setTouched] = useState({ fullName: false, identityNumber: false });
  const fullNameError = touched.fullName && passenger.fullName.trim().length < 2 ? "Ad soyad en az iki karakter olmalıdır." : "";
  const identityError = touched.identityNumber && !isValidIdentityNumber(passenger.identityNumber) ? "Geçerli bir T.C. kimlik numarası giriniz." : "";
  const nameErrorId = `passenger-${seat}-name-error`, identityErrorId = `passenger-${seat}-identity-error`;
  const resetTouched = () => setTouched({ fullName: false, identityNumber: false });
  return <fieldset className="passenger-ticket-card" disabled={busy} data-seat={seat}><legend>Yolcu Bilgileri — Koltuk {seat}</legend>
    <div className="passenger-ticket-body">
      <div className="passenger-ticket-journey">
        <div className="passenger-ticket-journey-info">
          <strong className="passenger-ticket-company">{journey.companyName}</strong>
          <p className="passenger-ticket-route"><span>{journey.departureCity}</span><span aria-hidden="true">→</span><span>{journey.arrivalCity}</span></p>
          <span className="passenger-ticket-date-label">Kalkış · Türkiye saati</span><time dateTime={journey.departureTime}>{departure.format(new Date(journey.departureTime))}</time>
        </div>
        <img className="passenger-ticket-panorama" src="/images/turkiye-panorama.png" alt="" width={1726} height={911} />
      </div>
      <div className="passenger-ticket-passenger"><div className="passenger-ticket-heading"><h4>Yolcu formu</h4></div>
        <div className="passenger-ticket-inputs">
          <label>Ad soyad<input aria-label={`Ad soyad · Koltuk ${seat}`} required minLength={2} maxLength={120} autoComplete="off" value={passenger.fullName}
            aria-invalid={fullNameError ? true : undefined} aria-describedby={fullNameError ? nameErrorId : undefined}
            onBlur={() => setTouched(t => ({ ...t, fullName: true }))} onInvalid={() => setTouched(t => ({ ...t, fullName: true }))}
            onChange={e => change("fullName", e.target.value)} />
            {fullNameError && <span id={nameErrorId} className="passenger-field-error">{fullNameError}</span>}</label>
          <label>T.C. Kimlik No<input aria-label={`T.C. Kimlik No · Koltuk ${seat}`} required inputMode="numeric" pattern="[1-9][0-9]{10}" minLength={11} maxLength={11} autoComplete="off" value={passenger.identityNumber}
            aria-invalid={identityError ? true : undefined} aria-describedby={identityError ? identityErrorId : undefined}
            ref={input => { if (input) input.setCustomValidity(input.value && !isValidIdentityNumber(input.value) ? "Geçerli bir T.C. kimlik numarası giriniz." : ""); }}
            onBlur={() => setTouched(t => ({ ...t, identityNumber: true }))} onInvalid={() => setTouched(t => ({ ...t, identityNumber: true }))}
            onChange={e => change("identityNumber", e.target.value)} />
            {identityError && <span id={identityErrorId} className="passenger-field-error">{identityError}</span>}</label>
        </div>
        <p className="passenger-gender-summary" aria-label={`Yolcu cinsiyeti · Koltuk ${seat}`}>Yolcu cinsiyeti: <strong className={`passenger-gender-${gender?.toLowerCase() ?? "unknown"}`}>{gender === "Female" ? "Kadın" : gender === "Male" ? "Erkek" : "Seçilmedi"}</strong></p>
        <small>Cinsiyetini değiştirmek için krokideki koltuğa tıklayın.</small>
        {seatError && <p className="seat-compatibility-error" role="alert">{seatError}</p>}
        <div className="passenger-ticket-actions">
          {hasProfile && <button type="button" className="secondary-button" aria-label={`Hesap bilgilerimi kullan · Koltuk ${seat}`} onClick={() => { resetTouched(); useProfile(); }}>Hesap bilgilerimi kullan</button>}
          <button type="button" className="secondary-button" aria-label={`Başka yolcu için bilgileri temizle · Koltuk ${seat}`} onClick={() => { resetTouched(); clear(); }}>Bilgileri temizle</button>
          <button type="button" className="secondary-button passenger-ticket-remove" aria-label={`Koltuğu çıkar · ${seat}`} onClick={remove}>Koltuğu çıkar</button>
        </div>
      </div>
      <div className="passenger-ticket-stub"><div><span>Koltuk</span><strong className="passenger-ticket-seat">{seat}</strong></div><div className="passenger-ticket-fare"><span>İndirim öncesi bilet tutarı</span><strong>{money.format(journey.price)}</strong></div></div>
    </div>
  </fieldset>;
}
