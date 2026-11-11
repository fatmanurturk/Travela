import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { currentRole } from "../reservations";
import CouponCheckout from "./CouponCheckout";
import type { CampaignPrice } from "../campaigns";
import SalesConsent from "./SalesConsent";
import { legalAcceptances } from "../legal";
import type { Gender } from "../seatCompatibility";

type Passenger = { fullName: string; email: string; phone: string };
const empty: Passenger = { fullName: "", email: "", phone: "" };
export default function PassengerCheckoutForm({
  tripId, seats, disabled, onBusy, onError, onConflict, onRemove, genders = {}, seatErrors = {},
}: {
  tripId: number;
  seats: number[];
  disabled: boolean;
  onBusy: (busy: boolean) => void;
  onError: (message: string) => void;
  onConflict: () => void;
  onRemove: (seat: number) => void;
  genders?: Record<number, Gender | "">;
  seatErrors?: Record<number, string>;
}) {
  const [passengers, setPassengers] = useState<Record<number, Passenger>>({}); const [profile, setProfile] = useState<Passenger | null>(null);
  const [options, setOptions] = useState<{ provider: string; requiresBuyer: boolean } | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [revision, setRevision] = useState(0);
  const [buyer, setBuyer] = useState({ name: "", surname: "", identityNumber: "", address: "", city: "", country: "Türkiye", zipCode: "", phone: "" });
  const [couponCode, setCouponCode] = useState(""); const [price, setPrice] = useState<CampaignPrice | null>(null);
  const [acceptedSelection, setAcceptedSelection] = useState<string | null>(null);
  const selection = JSON.stringify({ tripId, seats: [...seats].sort((a, b) => a - b), amount: price?.amount, couponCode });
  const salesAccepted = acceptedSelection === selection;
  const key = useRef({ fingerprint: "", value: crypto.randomUUID() }); const token = sessionStorage.getItem("travela_token"); const navigate = useNavigate();
  useEffect(() => { setPassengers(current => Object.fromEntries(Object.entries(current).filter(([seat]) => seats.includes(Number(seat))))); }, [seats]);
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl("/api/purchases/options"), { signal: abort.signal }).then(async response => { if (!response.ok) throw new Error("Ödeme seçenekleri alınamadı."); return response.json(); }).then(data => { if (!abort.signal.aborted) { setOptions(data); setError(""); } }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [revision]);
  useEffect(() => { if (!token || currentRole() !== "Passenger") return; const abort = new AbortController(); fetch(apiUrl("/api/profile"), { headers: { Authorization: `Bearer ${token}` }, signal: abort.signal }).then(async response => response.ok ? response.json() : null).then(data => { if (data && !abort.signal.aborted) setProfile({ fullName: data.fullName, email: data.email, phone: data.phoneNumber ?? "" }); }).catch(() => {}); return () => abort.abort(); }, [token]);
  if (!token) return <button type="button" className="site-button primary" disabled={!seats.length || disabled} onClick={() => navigate(`/login?returnTo=${encodeURIComponent(`/trips/${tripId}`)}`)}>Giriş yap ve devam et</button>;
  if (currentRole() !== "Passenger") return <p>Bilet almak için yolcu hesabınızla giriş yapın.</p>;
  return <form className="passenger-checkout-form" onSubmit={async event => {
    event.preventDefault();
    if (busy || disabled || !options || !seats.length || !price) return;
    if (seats.some(seat => !genders[seat] || seatErrors[seat])) { onError("Her yolcu için cinsiyet seçiniz ve koltuk uyumluluğunu kontrol ediniz."); return; }
    if (!salesAccepted) {
      onError("Ödemeye geçmeden önce sözleşme ve ön bilgilendirme formunu kabul edin.");
      return;
    }
    setBusy(true); onBusy(true); onError("");
    const payload = {
      tripId,
      couponCode: couponCode || null,
      expectedAmount: price.amount,
      passengers: [...seats].sort((a, b) => a - b).map(seatNumber => ({
        seatNumber,
        ...(passengers[seatNumber] ?? empty),
        gender: genders[seatNumber],
      })),
      legalAcceptances: legalAcceptances("DistanceSales"),
      ...(options.requiresBuyer ? { buyer } : {}),
    };
    const fingerprint = JSON.stringify({ ...payload, expectedAmount: undefined }); if (key.current.fingerprint !== fingerprint) key.current = { fingerprint, value: crypto.randomUUID() };
    try {
      const response = await fetch(apiUrl("/api/purchases"), { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` }, body: JSON.stringify({ ...payload, requestKey: key.current.value }) });
      const data = await response.json().catch(() => null);
      if (response.status === 401) { sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); navigate(`/login?returnTo=${encodeURIComponent(`/trips/${tripId}`)}`); return; }
      if (!response.ok) { if (response.status === 409) onConflict(); throw new Error(data?.message ?? (data?.errors ? Object.values(data.errors).flat().join(" ") : "Sipariş başlatılamadı. Yolcu bilgilerini kontrol edin.")); }
      if (!/^[a-f0-9-]{36}$/i.test(data.id)) throw new Error("Sipariş kimliği alınamadı. Biletlerim sayfasını kontrol edin.");
      navigate(`/purchase/${data.id}`);
    } catch (cause) { onError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Aynı bilgilerle tekrar denemeden önce Biletlerim sayfasını kontrol edin." : (cause as Error).message); }
    finally { setBusy(false); onBusy(false); }
  }}><h3>Yolcu bilgileri</h3><p>En fazla 10 yolcu için bilet alabilirsiniz. Biletler hesabınızda görünür; her bilette burada yazılan yolcu bilgileri kullanılır.</p>
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(value => value + 1)}>Tekrar dene</button></p>}
    {seats.map(seat => <fieldset key={seat} disabled={busy}><legend>Koltuk {seat}</legend>{profile && <button type="button" className="secondary-button" onClick={() => setPassengers(current => ({ ...current, [seat]: { ...profile } }))}>Hesap bilgilerimi kullan · Koltuk {seat}</button>}
      <label>Ad soyad<input aria-label={`Ad soyad · Koltuk ${seat}`} required minLength={2} maxLength={120} autoComplete="off" value={passengers[seat]?.fullName ?? ""} onChange={event => setPassengers(current => ({ ...current, [seat]: { ...(current[seat] ?? empty), fullName: event.target.value } }))} /></label>
      <label>E-posta<input aria-label={`E-posta · Koltuk ${seat}`} required type="email" maxLength={320} autoComplete="off" value={passengers[seat]?.email ?? ""} onChange={event => setPassengers(current => ({ ...current, [seat]: { ...(current[seat] ?? empty), email: event.target.value } }))} /></label>
      <label>Telefon<input aria-label={`Telefon · Koltuk ${seat}`} required type="tel" pattern="\+[1-9][0-9]{7,14}" placeholder="+905xxxxxxxxx" autoComplete="off" value={passengers[seat]?.phone ?? ""} onChange={event => setPassengers(current => ({ ...current, [seat]: { ...(current[seat] ?? empty), phone: event.target.value } }))} /></label>
      <p className="passenger-gender-summary" aria-label={`Yolcu cinsiyeti · Koltuk ${seat}`}>
        Yolcu cinsiyeti: <strong>{genders[seat] === "Female" ? "Kadın" : genders[seat] === "Male" ? "Erkek" : "Seçilmedi"}</strong>
      </p>
      <small>Cinsiyetini değiştirmek için krokideki koltuğa tıklayın.</small>
      {seatErrors[seat] && <p className="seat-compatibility-error" role="alert">{seatErrors[seat]}</p>}
      <button type="button" className="secondary-button" onClick={() => onRemove(seat)}>Koltuğu çıkar · {seat}</button>
    </fieldset>)}
    {options?.requiresBuyer && <fieldset disabled={busy}><legend>Ödemeyi yapan kişi</legend><p>Bu bilgiler iyzico sandbox ödeme formu için gereklidir. Kart bilgileri yalnızca iyzico sayfasına girilir.</p>{(Object.keys(buyer) as (keyof typeof buyer)[]).map(field => <label key={field}>{({name:"Ad",surname:"Soyad",identityNumber:"Kimlik numarası (11 hane)",address:"Fatura adresi",city:"Şehir",country:"Ülke",zipCode:"Posta kodu",phone:"Telefon (+ülke kodu)"})[field]}<input aria-label={`Alıcı ${field}`} required maxLength={field === "address" ? 500 : field === "identityNumber" ? 11 : field === "phone" ? 16 : 100} pattern={field === "identityNumber" ? "[0-9]{11}" : field === "phone" ? "\\+[1-9][0-9]{7,14}" : undefined} value={buyer[field]} onChange={event => setBuyer(current => ({ ...current, [field]: event.target.value }))} /></label>)}</fieldset>}
    <CouponCheckout tripId={tripId} count={seats.length} code={couponCode} disabled={busy || disabled} onCode={setCouponCode} onQuote={setPrice} />
    {!!seats.length && (
      <SalesConsent
        checked={salesAccepted}
        onChange={checked => setAcceptedSelection(checked ? selection : null)}
        disabled={busy || disabled || !price}
      />
    )}
    {!!seats.length && <label><input type="checkbox" required disabled={busy} />Yolcu bilgilerini kontrol ettim; bu kişiler adına bilet alıyorum.</label>}
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
