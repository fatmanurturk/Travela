import { useEffect, useReducer, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { currentRole } from "../reservations";
import CouponCheckout from "./CouponCheckout";
import type { CampaignPrice } from "../campaigns";
import SalesConsent from "./SalesConsent";
import { legalAcceptances } from "../legal";
import type { Gender } from "../seatCompatibility";
import { isValidIdentityNumber } from "../identityNumber";
import { tripPath } from "../journeySelection";
import { initialPassengerDetails, passengerDetailsReducer } from "../passengerDetails";

export default function PassengerCheckoutForm({
  tripId, seats, disabled, onBusy, onError, onConflict, onRemove, genders = {}, seatErrors = {}, boardingStopOrder, alightingStopOrder,
}: {
  tripId: number;
  boardingStopOrder?: number | null;
  alightingStopOrder?: number | null;
  seats: number[];
  disabled: boolean;
  onBusy: (busy: boolean) => void;
  onError: (message: string) => void;
  onConflict: () => void;
  onRemove: (seat: number) => void;
  genders?: Record<number, Gender | "">;
  seatErrors?: Record<number, string>;
}) {
  const [{ passengers, profile, contact }, dispatch] = useReducer(passengerDetailsReducer, undefined, initialPassengerDetails);
  const [profileError, setProfileError] = useState("");
  const [options, setOptions] = useState<{ provider: string; requiresBuyer: boolean } | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [revision, setRevision] = useState(0);
  const [buyer, setBuyer] = useState({ name: "", surname: "", identityNumber: "", address: "", city: "", country: "Türkiye", zipCode: "" });
  const [couponCode, setCouponCode] = useState(""); const [price, setPrice] = useState<CampaignPrice | null>(null);
  const [acceptedSelection, setAcceptedSelection] = useState<string | null>(null);
  const selection = JSON.stringify({ tripId, boardingStopOrder, alightingStopOrder, seats: [...seats].sort((a, b) => a - b), amount: price?.amount, couponCode });
  const salesAccepted = acceptedSelection === selection;
  const key = useRef({ fingerprint: "", value: crypto.randomUUID() }); const token = sessionStorage.getItem("travela_token"); const navigate = useNavigate();
  useEffect(() => { dispatch({ type: "seats", seats }); }, [seats]);
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl("/api/purchases/options"), { signal: abort.signal }).then(async response => { if (!response.ok) throw new Error("Ödeme seçenekleri alınamadı."); return response.json(); }).then(data => { if (!abort.signal.aborted) { setOptions(data); setError(""); } }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [revision]);
  useEffect(() => {
    if (!token || currentRole() !== "Passenger") return;
    const abort = new AbortController();
    fetch(apiUrl("/api/profile"), { headers: { Authorization: `Bearer ${token}` }, signal: abort.signal, cache: "no-store" })
      .then(async response => {
        if (!response.ok) throw new Error("Profile unavailable");
        const data = await response.json();
        if (!data || typeof data !== "object" || Array.isArray(data)) throw new Error("Invalid profile");
        return data;
      }).then(data => {
        if (abort.signal.aborted) return;
        const text = (value: unknown) => typeof value === "string" ? value : "";
        dispatch({ type: "profile", profile: { fullName: text(data.fullName), identityNumber: text(data.identityNumber),
          email: text(data.email), phone: text(data.phoneNumber) } });
        setProfileError("");
      }).catch(() => {
        if (!abort.signal.aborted) setProfileError("Hesap bilgileri alınamadı. Yolcu ve iletişim bilgilerini elle doldurabilirsiniz.");
      });
    return () => abort.abort();
  }, [token]);
  const returnPath = tripPath(tripId, { boardingStopOrder, alightingStopOrder });
  if (!token) return <button type="button" className="site-button primary" disabled={!seats.length || disabled} onClick={() => navigate(`/login?returnTo=${encodeURIComponent(returnPath)}`)}>Giriş yap ve devam et</button>;
  if (currentRole() !== "Passenger") return <p>Bilet almak için yolcu hesabınızla giriş yapın.</p>;
  // Keep entered state mounted, but show the form only once there is a passenger to complete.
  if (!seats.length) return <button type="button" className="site-button primary" disabled>Ödemeye geç</button>;
  return <form className="passenger-checkout-form" onSubmit={async event => {
    event.preventDefault();
    if (busy || disabled || !options || !seats.length || !price) return;
    const invalidSeat = seats.find(seat => (passengers[seat]?.fullName.trim().length ?? 0) < 2 || !isValidIdentityNumber(passengers[seat]?.identityNumber ?? ""));
    if (invalidSeat !== undefined) { onError(`Koltuk ${invalidSeat} için ad soyad ve geçerli T.C. kimlik numarası giriniz.`); return; }
    if (seats.some(seat => !genders[seat] || seatErrors[seat])) { onError("Her yolcu için cinsiyet seçiniz ve koltuk uyumluluğunu kontrol ediniz."); return; }
    if (!salesAccepted) {
      onError("Ödemeye geçmeden önce sözleşme ve ön bilgilendirme formunu kabul edin.");
      return;
    }
    setBusy(true); onBusy(true); onError("");
    const payload = {
      tripId,
      boardingStopOrder,
      alightingStopOrder,
      couponCode: couponCode || null,
      expectedAmount: price.amount,
      contactEmail: contact.email.trim(),
      contactPhone: contact.phone.trim(),
      passengers: [...seats].sort((a, b) => a - b).map(seatNumber => ({
        seatNumber,
        fullName: passengers[seatNumber].fullName.trim(),
        identityNumber: passengers[seatNumber].identityNumber,
        gender: genders[seatNumber],
      })),
      legalAcceptances: legalAcceptances("DistanceSales"),
      ...(options.requiresBuyer ? { buyer: { ...buyer, phone: contact.phone.trim() } } : {}),
    };
    const fingerprint = JSON.stringify({ ...payload, expectedAmount: undefined }); if (key.current.fingerprint !== fingerprint) key.current = { fingerprint, value: crypto.randomUUID() };
    try {
      const response = await fetch(apiUrl("/api/purchases"), { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` }, body: JSON.stringify({ ...payload, requestKey: key.current.value }) });
      const data = await response.json().catch(() => null);
      if (response.status === 401) { sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); navigate(`/login?returnTo=${encodeURIComponent(returnPath)}`); return; }
      if (!response.ok) { if (response.status === 409) onConflict(); throw new Error(data?.message ?? (data?.errors ? Object.values(data.errors).flat().join(" ") : "Sipariş başlatılamadı. Yolcu bilgilerini kontrol edin.")); }
      if (!/^[a-f0-9-]{36}$/i.test(data.id)) throw new Error("Sipariş kimliği alınamadı. Biletlerim sayfasını kontrol edin.");
      navigate(`/purchase/${data.id}`);
    } catch (cause) { onError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Aynı bilgilerle tekrar denemeden önce Biletlerim sayfasını kontrol edin." : (cause as Error).message); }
    finally { setBusy(false); onBusy(false); }
  }}><h3>Yolcu bilgileri</h3><p>En fazla 10 yolcu için bilet alabilirsiniz. Biletler hesabınızda görünür; her bilette burada yazılan yolcu bilgileri kullanılır.</p>
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(value => value + 1)}>Tekrar dene</button></p>}
    {profileError && <p role="status">{profileError}</p>}
    <fieldset className="checkout-fields" disabled={busy}><legend>İletişim Bilgileri</legend>
      <p>Bilet ve rezervasyon bildirimleri bu e-posta adresine ve telefona gönderilir.</p>
      <label>E-posta<input aria-label="İletişim e-posta" required type="email" maxLength={320} autoComplete="email" value={contact.email} onChange={event => dispatch({ type: "contact", field: "email", value: event.target.value })} /></label>
      <label>Telefon<input aria-label="İletişim telefon" required type="tel" pattern="\+[1-9][0-9]{7,14}" maxLength={16} placeholder="+905xxxxxxxxx" autoComplete="tel" value={contact.phone} onChange={event => dispatch({ type: "contact", field: "phone", value: event.target.value })} /></label>
    </fieldset>
    {seats.map(seat => <fieldset className="checkout-fields" key={seat} disabled={busy}><legend>Yolcu Bilgileri — Koltuk {seat}</legend>{profile && <button type="button" className="secondary-button" onClick={() => dispatch({ type: "use-profile", seat })}>Hesap bilgilerimi kullan · Koltuk {seat}</button>}
      <button type="button" className="secondary-button" onClick={() => dispatch({ type: "clear", seat })}>Başka yolcu için bilgileri temizle · Koltuk {seat}</button>
      <label>Ad soyad<input aria-label={`Ad soyad · Koltuk ${seat}`} required minLength={2} maxLength={120} autoComplete="off" value={passengers[seat]?.fullName ?? ""} onChange={event => dispatch({ type: "passenger", seat, field: "fullName", value: event.target.value })} /></label>
      <label>T.C. Kimlik No<input aria-label={`T.C. Kimlik No · Koltuk ${seat}`} required inputMode="numeric" pattern="[1-9][0-9]{10}" minLength={11} maxLength={11} autoComplete="off" value={passengers[seat]?.identityNumber ?? ""} ref={input => {
        if (input) input.setCustomValidity(input.value && !isValidIdentityNumber(input.value) ? "Geçerli bir T.C. kimlik numarası giriniz." : "");
      }} onChange={event => {
        dispatch({ type: "passenger", seat, field: "identityNumber", value: event.target.value });
      }} /></label>
      <p className="passenger-gender-summary" aria-label={`Yolcu cinsiyeti · Koltuk ${seat}`}>
        Yolcu cinsiyeti: <strong>{genders[seat] === "Female" ? "Kadın" : genders[seat] === "Male" ? "Erkek" : "Seçilmedi"}</strong>
      </p>
      <small>Cinsiyetini değiştirmek için krokideki koltuğa tıklayın.</small>
      {seatErrors[seat] && <p className="seat-compatibility-error" role="alert">{seatErrors[seat]}</p>}
      <button type="button" className="secondary-button" onClick={() => onRemove(seat)}>Koltuğu çıkar · {seat}</button>
    </fieldset>)}
    {options?.requiresBuyer && <fieldset className="checkout-fields" disabled={busy}><legend>Ödemeyi yapan kişi</legend><p>Bu bilgiler iyzico sandbox ödeme formu için gereklidir. Kart bilgileri yalnızca iyzico sayfasına girilir.</p>{(Object.keys(buyer) as (keyof typeof buyer)[]).map(field => <label key={field} className={field === "address" ? "checkout-field-wide" : undefined}>{({name:"Ad",surname:"Soyad",identityNumber:"Kimlik numarası (11 hane)",address:"Fatura adresi",city:"Şehir",country:"Ülke",zipCode:"Posta kodu"})[field]}<input aria-label={`Alıcı ${field}`} required maxLength={field === "address" ? 500 : field === "identityNumber" ? 11 : 100} pattern={field === "identityNumber" ? "[0-9]{11}" : undefined} value={buyer[field]} onChange={event => setBuyer(current => ({ ...current, [field]: event.target.value }))} /></label>)}</fieldset>}
    <CouponCheckout tripId={tripId} boardingStopOrder={boardingStopOrder} alightingStopOrder={alightingStopOrder} count={seats.length} code={couponCode} disabled={busy || disabled} onCode={setCouponCode} onQuote={setPrice} />
    {!!seats.length && (
      <SalesConsent
        checked={salesAccepted}
        onChange={checked => setAcceptedSelection(checked ? selection : null)}
        disabled={busy || disabled || !price}
      />
    )}
    {!!seats.length && <label className="checkout-confirmation"><input type="checkbox" required disabled={busy} /><span>Yolcu bilgilerini kontrol ettim; bu kişiler adına bilet alıyorum.</span></label>}
    <p>Koltuklardan biri alınamazsa hiçbir koltuk ayrılmaz. Ödeme tek sipariş içindir; iptal ve iade her bilet için ayrı yapılır.</p>
    <button
      type="submit"
      className="site-button primary"
      disabled={!seats.length || disabled || busy || !options || !price || !salesAccepted || seats.some(seat => !genders[seat] || !!seatErrors[seat])}
    >
      {busy ? "Sipariş hazırlanıyor..." : "Ödemeye geç"}
    </button>
  </form>;
}
