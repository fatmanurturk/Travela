import { useEffect, useReducer, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../api";
import { currentRole } from "../reservations";
import CheckoutLogin from "./CheckoutLogin";
import { normalizeBookingPhone } from "../bookingContact";
import CouponCheckout from "./CouponCheckout";
import CampaignPriceSummary from "./CampaignPriceSummary";
import PassengerTicketCard from "./PassengerTicketCard";
import type { PassengerTicketJourney } from "./PassengerTicketCard";
import "./PassengerTicketCard.css";
import type { CampaignPrice } from "../campaigns";
import SalesConsent from "./SalesConsent";
import { legalAcceptances } from "../legal";
import type { Gender } from "../seatCompatibility";
import { isValidIdentityNumber } from "../identityNumber";
import { initialPassengerDetails, passengerDetailsReducer } from "../passengerDetails";
import type { SeatSession } from "../seatSelection";

export default function PassengerCheckoutForm({
  tripId, journey, selectionTimer, seats, disabled, onBusy, onError, onConflict, onRemove, genders = {}, seatErrors = {}, boardingStopOrder, alightingStopOrder, selectionSession, onSelectionExpired, selectionExpired,
}: {
  tripId: number;
  journey: PassengerTicketJourney;
  selectionTimer?: ReactNode;
  selectionSession: SeatSession | null;
  onSelectionExpired: () => void;
  selectionExpired: boolean;
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
  const [loginOpen, setLoginOpen] = useState(false);
  const [resumeId, setResumeId] = useState<string | null>(null);
  const [profileError, setProfileError] = useState("");
  const [options, setOptions] = useState<{ provider: string; requiresBuyer: boolean } | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [revision, setRevision] = useState(0);
  const [buyer, setBuyer] = useState({ name: "", surname: "", identityNumber: "", address: "", city: "", country: "Türkiye", zipCode: "" });
  const [couponCode, setCouponCode] = useState(""); const [price, setPrice] = useState<CampaignPrice | null>(null);
  const [acceptedSelection, setAcceptedSelection] = useState<string | null>(null);
  const selection = JSON.stringify({ tripId, selectionSessionId: selectionSession?.id, boardingStopOrder, alightingStopOrder, seats: [...seats].sort((a, b) => a - b), amount: price?.amount, couponCode });
  const salesAccepted = acceptedSelection === selection;
  const key = useRef({ fingerprint: "", value: crypto.randomUUID() }); const token = sessionStorage.getItem("travela_token"); const navigate = useNavigate();
  useEffect(() => { dispatch({ type: "seats", seats, preserveRemoved: selectionExpired }); }, [seats, selectionExpired]);
  useEffect(() => { const abort = new AbortController(); fetch(apiUrl("/api/purchases/options"), { signal: abort.signal }).then(async response => { if (!response.ok) throw new Error("Ödeme seçenekleri alınamadı."); return response.json(); }).then(data => { if (!abort.signal.aborted) { setOptions(data); setError(""); } }).catch(cause => { if (!abort.signal.aborted) setError(cause instanceof TypeError ? "Ödeme seçenekleri alınamadı. Tekrar deneyin." : cause.message); }); return () => abort.abort(); }, [revision]);
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
        if (!abort.signal.aborted) setProfileError("Hesap bilgileri alınamadı. Bilgileri elle doldurun.");
      });
    return () => abort.abort();
  }, [token]);
  useEffect(() => {
    if (token) return;
    const abort = new AbortController();
    fetch(apiUrl("/api/guest/purchases/resume"), { credentials: "include", cache: "no-store", signal: abort.signal })
      .then(async response => response.ok ? response.json() : null).then(data => { if (!abort.signal.aborted) setResumeId(data?.id ?? null); }).catch(() => {});
    return () => abort.abort();
  }, [token]);
  const heading = <header className="passenger-checkout-heading"><div className="passenger-heading-row"><h3>Yolcu bilgileri · {seats.length} yolcu</h3>{selectionTimer}</div>
    <p className="passenger-selection-summary" role="status" aria-atomic="true">{seats.length} yolcu kartı · Koltuklar: {seats.join(", ")}</p>
    <details className="booking-help"><summary>5 dk koltuk ayırma · Ödemede 10 dk <span>Detaylar</span></summary><div>
      <p>Koltuk ve cinsiyet seçiminiz onaylandığında koltuklarınız 5 dakika ayrılır. Ödemeye geçince bu tutma 10 dakikalık ödeme tutmasına dönüşür. Biletiniz ödeme doğrulandığında oluşur.</p>
      <p>En fazla 10 yolcu için bilet alabilirsiniz. Her bilette burada yazılan yolcu bilgileri kullanılır. {token ? "Biletler hesabınızda görünür." : "Üye olmadan satın alabilirsiniz; hesap oluşturulmaz. PNR ve iletişim bilginizle biletinizi sorgulayabilirsiniz."}</p>
      <p>Kartlardaki tutarlar indirim öncesidir. Kupon ve kampanya indirimi siparişin fiyat özetinde gösterilir.</p>
    </div></details>
  </header>;
  if (token && currentRole() !== "Passenger") return <p>Bilet almak için yolcu hesabınızla giriş yapın.</p>;
  // Keep entered state mounted, but show the form only once there is a passenger to complete.
  if (!seats.length) return <div className="passenger-checkout-empty">{heading}<p role="status">Devam etmek için otobüs krokisinden koltuk seçin</p><button type="button" className="site-button primary" disabled>Ödemeye devam et</button></div>;
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
    const phone = normalizeBookingPhone(contact.phone);
    if (!phone) { onError("Geçerli bir cep telefonu giriniz (05… veya +905…)."); return; }
    setBusy(true); onBusy(true); onError("");
    const payload = {
      tripId,
      selectionSessionId: selectionSession?.id,
      boardingStopOrder,
      alightingStopOrder,
      couponCode: couponCode || null,
      expectedAmount: price.amount,
      contactEmail: contact.email.trim(),
      contactPhone: phone,
      passengers: [...seats].sort((a, b) => a - b).map(seatNumber => ({
        seatNumber,
        fullName: passengers[seatNumber].fullName.trim(),
        identityNumber: passengers[seatNumber].identityNumber,
        gender: genders[seatNumber],
      })),
      legalAcceptances: legalAcceptances("DistanceSales"),
      ...(options.requiresBuyer ? { buyer: { ...buyer, phone } } : {}),
    };
    const fingerprint = JSON.stringify({ ...payload, expectedAmount: undefined }); if (key.current.fingerprint !== fingerprint) key.current = { fingerprint, value: crypto.randomUUID() };
    try {
      if (!token) {
        const session = await fetch(apiUrl("/api/guest/purchases/session"), { method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: "{}" });
        if (!session.ok) throw new Error("Misafir oturumu başlatılamadı. Bir süre sonra tekrar deneyin.");
      }
      if (!selectionSession) throw new Error("Önce koltuk seçiminizi sunucuda doğrulayın.");
      const response = await fetch(apiUrl(token ? "/api/purchases" : "/api/guest/purchases"), { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "X-Seat-Token": selectionSession.token, ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify({ ...payload, requestKey: key.current.value }) });
      const data = await response.json().catch(() => null);
      if (data?.code === "selection_expired" || data?.code === "selection_changed") onSelectionExpired();
      if (response.status === 401 && token) {
        sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); setRevision(value => value + 1);
        onError("Oturumunuz sona erdi. Bilgileriniz korundu; misafir olarak devam edebilir veya yeniden giriş yapabilirsiniz."); return;
      }
      if (!response.ok) { if (response.status === 409) onConflict(); throw new Error(data?.message ?? (data?.errors ? Object.values(data.errors).flat().join(" ") : "Sipariş başlatılamadı. Yolcu bilgilerini kontrol edin.")); }
      if (!/^[a-f0-9-]{36}$/i.test(data.id)) throw new Error("Sipariş kimliği alınamadı. Biletlerim sayfasını kontrol edin.");
      navigate(`/purchase/${data.id}`);
    } catch (cause) { onError(cause instanceof TypeError ? "Sunucu yanıtı alınamadı. Aynı bilgilerle tekrar deneyebilirsiniz; mevcut sipariş aynı işlem anahtarıyla bulunur. Sayfayı yenilediyseniz son sipariş bağlantısını kontrol edin." : (cause as Error).message); }
    finally { setBusy(false); onBusy(false); }
  }}>{heading}
    <div className="passenger-ticket-list">{seats.map(seat => <PassengerTicketCard key={seat} seat={seat} journey={journey}
      passenger={passengers[seat] ?? { fullName: "", identityNumber: "" }} gender={genders[seat]} seatError={seatErrors[seat]} busy={busy} hasProfile={!!profile}
      change={(field, value) => dispatch({ type: "passenger", seat, field, value })}
      useProfile={() => dispatch({ type: "use-profile", seat })} clear={() => dispatch({ type: "clear", seat })} remove={() => onRemove(seat)} />)}</div>
    <div className="passenger-checkout-columns"><div className="passenger-checkout-contact">
    {!token && <button type="button" className="site-button secondary" disabled={busy} onClick={() => setLoginOpen(true)}>Hesabınızla giriş yapın (isteğe bağlı)</button>}
    {loginOpen && <CheckoutLogin onClose={() => setLoginOpen(false)} onLogin={() => { setLoginOpen(false); onConflict(); }} />}
    {resumeId && !token && <button type="button" className="site-button secondary" onClick={() => navigate(`/purchase/${resumeId}`)}>Son misafir siparişini görüntüle</button>}
    {error && <p role="alert">{error} <button type="button" onClick={() => setRevision(value => value + 1)}>Tekrar dene</button></p>}
    {profileError && <p role="status">{profileError}</p>}
    <fieldset className="checkout-fields checkout-contact" disabled={busy}><legend>İletişim Bilgileri</legend>
      <p>Bu iletişim bilgileri siparişin tamamı için kullanılır. Biletinizi PNR ile sorgulamak için bu bilgiyi saklayın.</p>
      <label>E-posta<input aria-label="İletişim e-posta" required type="email" maxLength={320} autoComplete="email" value={contact.email} onChange={event => dispatch({ type: "contact", field: "email", value: event.target.value })} /></label>
      <label>Telefon<input aria-label="İletişim telefon" required type="tel" maxLength={24} placeholder="05xxxxxxxxx veya +905xxxxxxxxx" autoComplete="tel" value={contact.phone} onChange={event => dispatch({ type: "contact", field: "phone", value: event.target.value })} /></label>
    </fieldset>
    {options?.requiresBuyer && <fieldset className="checkout-fields" disabled={busy}><legend>Ödemeyi yapan kişi</legend><p>Bu bilgiler iyzico{options.provider === "IyzicoSandbox" ? " sandbox" : ""} ödeme formu için gereklidir. Kart bilgileri yalnızca iyzico sayfasına girilir.</p>{(Object.keys(buyer) as (keyof typeof buyer)[]).map(field => <label key={field} className={field === "address" ? "checkout-field-wide" : undefined}>{({name:"Ad",surname:"Soyad",identityNumber:"Kimlik numarası (11 hane)",address:"Fatura adresi",city:"Şehir",country:"Ülke",zipCode:"Posta kodu"})[field]}<input aria-label={`Alıcı ${field}`} required maxLength={field === "address" ? 500 : field === "identityNumber" ? 11 : 100} pattern={field === "identityNumber" ? "[0-9]{11}" : undefined} value={buyer[field]} onChange={event => setBuyer(current => ({ ...current, [field]: event.target.value }))} /></label>)}</fieldset>}
    </div><div className="passenger-checkout-payment">
    <CouponCheckout tripId={tripId} boardingStopOrder={boardingStopOrder} alightingStopOrder={alightingStopOrder} count={seats.length} code={couponCode} disabled={busy || disabled} onCode={setCouponCode} onQuote={setPrice} showPrice={false} />
    <section className="passenger-checkout-total" aria-label="Fiyat özeti"><h3>Fiyat özeti</h3>
    {price ? <CampaignPriceSummary price={price} /> : <p role="status">Fiyat bilgisi henüz hazır değil.</p>}
    {!!seats.length && (
      <SalesConsent
        checked={salesAccepted}
        onChange={checked => setAcceptedSelection(checked ? selection : null)}
        disabled={busy || disabled || !price}
      />
    )}
    {!!seats.length && <label className="checkout-confirmation"><input type="checkbox" required disabled={busy} /><span>Yolcu bilgilerini kontrol ettim; bu kişiler adına bilet alıyorum.</span></label>}
    <p>Ödemeye geçerken koltuklardan biri doğrulanamazsa ödeme başlatılmaz. Ödeme tek sipariş içindir; iptal ve iade her bilet için ayrı yapılır.</p>
    <button
      type="submit"
      className="site-button primary"
      disabled={!seats.length || disabled || busy || !options || !price || !salesAccepted || seats.some(seat => !genders[seat] || !!seatErrors[seat])}
    >
      {busy ? "Sipariş hazırlanıyor..." : "Ödemeye devam et"}
    </button>
    </section>
    </div></div>
  </form>;
}
