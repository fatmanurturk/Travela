import { useState } from "react";
import { apiUrl } from "../api";
import type { Purchase } from "../payments";
import { downloadPrivatePdf } from "../privatePdf";
import SiteHeader from "../components/SiteHeader";
import TicketCard from "../components/TicketCard";
import "../guestTickets.css";

type FoundTicket = Purchase & { passengerIdentityNumber?: string | null };
export default function TicketLookupPage() {
  const [pnr, setPnr] = useState(""); const [contact, setContact] = useState("");
  const [ticket, setTicket] = useState<FoundTicket | null>(null); const [error, setError] = useState("");
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState("");
  const [challenge, setChallenge] = useState<string | null>(null); const [code, setCode] = useState("");
  async function request(path: string, body?: object) {
    const response = await fetch(apiUrl(`/api/ticket-lookup${path}`), { method: "POST", credentials: "include", cache: "no-store",
      headers: { "Content-Type": "application/json" }, ...(body ? { body: JSON.stringify(body) } : { body: "{}" }) });
    const data = await response.json().catch(() => null);
    if (response.status === 401) { setTicket(null); setChallenge(null); }
    if (!response.ok) throw new Error(data?.message ?? "İşlem tamamlanamadı. Bir süre sonra tekrar deneyin.");
    return data;
  }
  async function run(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setError("");
    try { await action(); } catch (cause) { setError(cause instanceof TypeError ? "Sunucuya ulaşılamadı. İşlem durumunu yeniden sorgulayın." : (cause as Error).message); }
    finally { setBusy(false); }
  }
  return <div className="travela-site"><SiteHeader /><main className="ticket-lookup-page">
    <h1>Biletimi Sorgula</h1><p>Üye veya misafir olarak aldığınız bileti PNR ve satın alma sırasında verdiğiniz cep telefonu ya da e-posta ile sorgulayın.</p>
    <form className="ticket-lookup-form" onSubmit={event => { event.preventDefault(); void run(async () => {
      setTicket(null); setChallenge(null); setNotice(""); setCode("");
      const data = await request("", { pnr: pnr.trim().toUpperCase(), contact: contact.trim() }); setTicket(data.ticket);
    }); }}>
      <label htmlFor="lookup-pnr">PNR numarası<input id="lookup-pnr" required maxLength={128} autoComplete="off" autoCapitalize="characters" value={pnr} onChange={event => setPnr(event.target.value)} /></label>
      <label htmlFor="lookup-contact">Cep telefonu veya e-posta<input id="lookup-contact" required maxLength={320} autoComplete="off" placeholder="05xxxxxxxxx veya e-posta adresi" value={contact} onChange={event => setContact(event.target.value)} /></label>
      <button className="site-button primary" disabled={busy}>{busy ? "İşlem sürüyor..." : "Biletimi Sorgula"}</button>
    </form>
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {ticket && <section aria-label="Sorgulanan bilet" aria-live="polite">
      <h2>{ticket.passengerName}</h2><p>{ticket.passengerIdentityNumber && <>T.C. Kimlik No: {ticket.passengerIdentityNumber}<br /></>}{ticket.passengerEmail} · {ticket.passengerPhone}</p>
      <TicketCard item={ticket} showCheckout={false} publicAccess />
      <p>Bu bilet için erişim kısa sürelidir. Süre dolarsa yeniden sorgulayabilirsiniz.</p>
      <div className="guest-ticket-actions"><button type="button" className="site-button primary" disabled={busy} onClick={() => void run(() => downloadPrivatePdf("/api/ticket-lookup/pdf"))}>PDF indir</button>
        {ticket.canCancel && <button type="button" className="site-button secondary" disabled={busy} onClick={() => void run(async () => {
          const data = await request("/cancellation-code"); setChallenge(data.challengeId); setCode(""); setNotice(`${data.message} ${data.destination ?? ""}`);
        })}>İptal için doğrulama kodu gönder</button>}
      </div>
      {ticket.canCancel && challenge && <form className="ticket-cancel-code" onSubmit={event => { event.preventDefault(); void run(async () => {
        const data = await request("/cancel", { challengeId: challenge, code }); setTicket(data.ticket); setChallenge(null); setCode("");
        setNotice("Bilet iptal edildi. İade, orijinal ödeme yönteminize mevcut sağlayıcı süreciyle yapılacak; durumunu yeniden sorgulayabilirsiniz.");
      }); }}><label htmlFor="ticket-cancel-code">6 haneli iptal kodu<input id="ticket-cancel-code" required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6} value={code} onChange={event => setCode(event.target.value)} /></label>
        <p>Kod 5 dakika ve en fazla 5 deneme için geçerlidir. Onay yalnızca bu bileti iptal eder.</p>
        <button className="site-button reservation-cancel" disabled={busy}>Kodu doğrula ve bileti iptal et</button></form>}
    </section>}
    <p>Eski biletlerde satın alma anında kaydedilmiş iletişim bilgisi kullanılır. İletişim kaydı olmayan eski biletler için hesabınızın Biletlerim bölümünü veya destek kanalını kullanın.</p>
  </main></div>;
}
