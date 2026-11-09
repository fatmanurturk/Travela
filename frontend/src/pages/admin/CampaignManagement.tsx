import { useEffect, useState } from "react";
import { apiUrl } from "../../api";
import { dateTime, money } from "../../reservations";
import { campaignStatus, discountLabel } from "../../campaigns";
import type { Campaign } from "../../campaigns";
import "../../campaigns.css";

type Option = { id: number; name: string };
type CampaignOptions = { companies: Option[]; routes: Option[] };
type LoadResult = { revision: number; status: "success"; items: Campaign[]; options: CampaignOptions }
  | { revision: number; status: "error"; message: string };
type Form = { companyId: string; routeId: string; name: string; description: string; code: string; discountType: "Percentage" | "Fixed"; value: string;
  minimumSubtotal: string; maximumDiscount: string; usageLimit: string; perUserLimit: string; startsAt: string; endsAt: string;
  departureFrom: string; departureUntil: string; isActive: boolean; isPublic: boolean; version?: string };
type Usage = { id: string; originalAmount: number; discountAmount: number; amount: number; paymentStatus: string; createdAt: string; refunded: number; tickets: number };
const localTime = (value: string) => new Date(new Date(value).getTime() + 3 * 3600000).toISOString().slice(0, 16);
const defaults = (companyId = ""): Form => ({ companyId, routeId: "", name: "", description: "", code: "", discountType: "Percentage", value: "10", minimumSubtotal: "0", maximumDiscount: "", usageLimit: "", perUserLimit: "1", startsAt: localTime(new Date().toISOString()), endsAt: localTime(new Date(Date.now() + 7 * 86400000).toISOString()), departureFrom: "", departureUntil: "", isActive: true, isPublic: true });
const fromCampaign = (c: Campaign): Form => ({ companyId: String(c.companyId), routeId: c.routeId == null ? "" : String(c.routeId), name: c.name, description: c.description, code: c.code ?? "", discountType: c.discountType, value: String(c.value), minimumSubtotal: String(c.minimumSubtotal), maximumDiscount: c.maximumDiscount == null ? "" : String(c.maximumDiscount), usageLimit: c.usageLimit == null ? "" : String(c.usageLimit), perUserLimit: String(c.perUserLimit), startsAt: localTime(c.startsAt), endsAt: localTime(c.endsAt), departureFrom: c.departureFrom ? localTime(c.departureFrom) : "", departureUntil: c.departureUntil ? localTime(c.departureUntil) : "", isActive: c.isActive, isPublic: c.isPublic, version: c.version });
const payload = (f: Form) => ({ ...f, companyId: Number(f.companyId), routeId: f.routeId ? Number(f.routeId) : null, code: f.code.trim() || null, value: Number(f.value), minimumSubtotal: Number(f.minimumSubtotal), maximumDiscount: f.maximumDiscount ? Number(f.maximumDiscount) : null, usageLimit: f.usageLimit ? Number(f.usageLimit) : null, perUserLimit: Number(f.perUserLimit), startsAt: new Date(f.startsAt + "+03:00").toISOString(), endsAt: new Date(f.endsAt + "+03:00").toISOString(), departureFrom: f.departureFrom ? new Date(f.departureFrom + "+03:00").toISOString() : null, departureUntil: f.departureUntil ? new Date(f.departureUntil + "+03:00").toISOString() : null });
const headers = () => ({ Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" });
async function dataOf(response: Response, operation = "İşlem") {
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.message ?? (data?.errors ? Object.values(data.errors).flat().join(" ")
    : `${operation} tamamlanamadı (HTTP ${response.status}).${response.status === 404 && import.meta.env.DEV ? " API endpoint'i bulunamadı. Backend'in güncel sürümle çalıştığını kontrol edin." : ""}`));
  if (data == null) throw new Error(`${operation}: sunucudan geçerli bir JSON yanıtı alınamadı.`);
  return data;
}

export default function CampaignManagement() {
  const [loadResult, setLoadResult] = useState<LoadResult | null>(null);
  const [form, setForm] = useState<Form>(() => defaults()); const [editing, setEditing] = useState<number | null>(null); const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [message, setMessage] = useState("");
  const [revision, setRevision] = useState(0); const [search, setSearch] = useState(""); const [status, setStatus] = useState("");
  const [historyId, setHistoryId] = useState<number | null>(null); const [history, setHistory] = useState<Usage[]>([]); const [historyError, setHistoryError] = useState(""); const [loadedHistoryKey, setLoadedHistoryKey] = useState("");
  const loading = loadResult?.revision !== revision;
  const result = !loading && loadResult?.status === "success" ? loadResult : null;
  const loadError = !loading && loadResult?.status === "error" ? loadResult.message : "";
  const items = result?.items ?? []; const options = result?.options ?? { companies: [], routes: [] };
  const historyKey = `${historyId}:${revision}`; const historyLoading = loadedHistoryKey !== historyKey;
  useEffect(() => {
    const abort = new AbortController();
    // StrictMode replays effects in development; cleanup cancels only the obsolete request pair.
    void Promise.all([
      fetch(apiUrl("/api/campaigns"), { headers: headers(), signal: abort.signal }).then(r => dataOf(r, "Kampanya listesinin yüklenmesi")),
      fetch(apiUrl("/api/campaigns/options"), { headers: headers(), signal: abort.signal }).then(r => dataOf(r, "Kampanya seçeneklerinin yüklenmesi")),
    ]).then(([campaigns, choices]) => {
      if (abort.signal.aborted) return;
      if (!Array.isArray(campaigns) || !Array.isArray(choices?.companies) || !Array.isArray(choices?.routes))
        throw new Error("Kampanya verileri beklenen biçimde alınamadı.");
      setLoadResult({ revision, status: "success", items: campaigns, options: choices });
    }).catch(e => { if (!abort.signal.aborted) setLoadResult({ revision, status: "error", message: e instanceof TypeError ? "Kampanya servisine bağlanılamadı. Bağlantınızı kontrol edip tekrar deneyin." : e.message }); });
    return () => abort.abort();
  }, [revision]);
  useEffect(() => {
    if (historyId == null) return;
    const abort = new AbortController();
    void fetch(apiUrl(`/api/campaigns/${historyId}/usage`), { headers: headers(), signal: abort.signal }).then(dataOf).then(data => { if (!abort.signal.aborted) { setHistory(data); setHistoryError(""); } })
      .catch(e => { if (!abort.signal.aborted) setHistoryError(e.message); }).finally(() => { if (!abort.signal.aborted) setLoadedHistoryKey(historyKey); });
    return () => abort.abort();
  }, [historyId, revision, historyKey]);
  const update = <K extends keyof Form>(key: K, value: Form[K]) => setForm(f => ({ ...f, [key]: value }));
  const save = async (values: Form, id: number | null, close: boolean) => {
    if (busy) return; setBusy(true); setError(""); setMessage("");
    try { await fetch(apiUrl(`/api/campaigns${id == null ? "" : `/${id}`}`), { method: id == null ? "POST" : "PUT", headers: headers(), body: JSON.stringify(payload(values)) }).then(dataOf);
      setMessage(id == null ? "Kampanya oluşturuldu." : "Kampanya güncellendi. Mevcut siparişlerin fiyatı korunur.");
      if (close) { setOpen(false); setEditing(null); } setRevision(v => v + 1);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  const visible = items.filter(c => (!status || campaignStatus(c) === status) && `${c.name} ${c.code ?? ""} ${c.companyName}`.toLocaleLowerCase("tr").includes(search.toLocaleLowerCase("tr")));
  return <div className="campaign-management"><div className="dashboard-heading"><span className="eyebrow">SATIŞ VE PAZARLAMA</span><h1>Kuponlar ve kampanyalar</h1><p>Yolculara özel fırsatlar oluşturun, koşulları belirleyin ve kullanımını takip edin.</p></div>
    {error && <p role="alert" className="results-message error">{error}</p>}{message && <p role="status">{message}</p>}
    {loadError && <div role="alert" className="results-message error"><p>{loadError}</p><button type="button" className="site-button secondary" disabled={busy} onClick={() => setRevision(v => v + 1)}>Tekrar dene</button></div>}
    {result && <div className="campaign-stats" role="region" aria-label="Kampanya istatistikleri"><div><span>Aktif kampanya</span><strong>{items.filter(c => campaignStatus(c) === "Aktif").length}</strong></div><div><span>Tamamlanan kullanım</span><strong>{items.reduce((n, c) => n + c.used, 0)}</strong></div><div><span>Sağlanan toplam indirim</span><strong>{money.format(items.reduce((n, c) => n + c.totalDiscount, 0))}</strong></div></div>}
    <div className="campaign-toolbar"><div className="coupon-input-row"><label>Ara<input disabled={!result} placeholder="Kampanya, kod veya firma" value={search} onChange={e => setSearch(e.target.value)} /></label><label>Durum<select disabled={!result} value={status} onChange={e => setStatus(e.target.value)}><option value="">Tümü</option>{["Aktif", "Yakında", "Sona erdi", "Pasif", "Limit doldu"].map(s => <option key={s}>{s}</option>)}</select></label></div>
      <div><button type="button" className="site-button secondary" disabled={loading || busy} onClick={() => setRevision(v => v + 1)}>Yenile</button> <button type="button" className="site-button primary" disabled={!result || busy || !options.companies.length} onClick={() => { setForm(defaults(options.companies.length === 1 ? String(options.companies[0].id) : "")); setEditing(null); setOpen(true); setError(""); }}>+ Yeni kampanya</button></div></div>
    {result && open && <form className="campaign-form" onSubmit={e => { e.preventDefault(); void save(form, editing, true); }}><h2>{editing == null ? "Yeni kampanya oluştur" : "Kampanyayı düzenle"}</h2><p className="campaign-muted">Tüm tarihler Türkiye saatidir. Bitiş anı kapsam dışıdır. Boş kupon kodu otomatik kampanya oluşturur.</p><fieldset disabled={busy} style={{ border: 0, padding: 0, minWidth: 0 }}><div className="campaign-form-grid">
      <label>Firma<select required disabled={editing != null || options.companies.length === 1} value={form.companyId} onChange={e => update("companyId", e.target.value)}><option value="">Firma seçin</option>{options.companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}{editing != null && !options.companies.some(c => String(c.id) === form.companyId) && <option value={form.companyId}>Kayıtlı firma #{form.companyId}</option>}</select></label>
      <label>Kampanya adı<input required maxLength={120} value={form.name} onChange={e => update("name", e.target.value)} placeholder="Hafta sonu fırsatı" /></label>
      <label>Kupon kodu (isteğe bağlı)<input maxLength={32} minLength={3} pattern="[A-Za-z0-9][A-Za-z0-9_-]{2,31}" value={form.code} onChange={e => update("code", e.target.value.toUpperCase())} placeholder="Boş bırak: otomatik indirim" /></label>
      <label className="campaign-wide">Açıklama<textarea rows={2} maxLength={1000} value={form.description} onChange={e => update("description", e.target.value)} placeholder="Yolcuların göreceği kampanya açıklaması" /></label>
      <label>İndirim türü<select value={form.discountType} onChange={e => update("discountType", e.target.value as Form["discountType"])}><option value="Percentage">Yüzde (%)</option><option value="Fixed">Sabit tutar (TL / sipariş)</option></select></label>
      <label>İndirim değeri<input required type="number" min="0.01" step="0.01" max={form.discountType === "Percentage" ? 99 : 9999999} value={form.value} onChange={e => update("value", e.target.value)} /></label>
      <label>İndirim tavanı (TL)<input type="number" min="0.01" step="0.01" max="99999999" value={form.maximumDiscount} onChange={e => update("maximumDiscount", e.target.value)} placeholder="Sınırsız" /></label>
      <label>Minimum sepet (TL)<input required type="number" min="0" step="0.01" max="99999999" value={form.minimumSubtotal} onChange={e => update("minimumSubtotal", e.target.value)} /></label>
      <label>Toplam kullanım limiti<input type="number" min="1" max="1000000" value={form.usageLimit} onChange={e => update("usageLimit", e.target.value)} placeholder="Sınırsız" /></label>
      <label>Hesap başına sipariş limiti<input required type="number" min="1" max="1000000" value={form.perUserLimit} onChange={e => update("perUserLimit", e.target.value)} /></label>
      <label>Satın alma başlangıcı<input required type="datetime-local" value={form.startsAt} onChange={e => update("startsAt", e.target.value)} /></label>
      <label>Satın alma bitişi<input required type="datetime-local" min={form.startsAt} value={form.endsAt} onChange={e => update("endsAt", e.target.value)} /></label>
      <label>Geçerli güzergâh<select value={form.routeId} onChange={e => update("routeId", e.target.value)}><option value="">Firmanın tüm güzergâhları</option>{options.routes.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}{form.routeId && !options.routes.some(r => String(r.id) === form.routeId) && <option value={form.routeId}>Kayıtlı güzergâh #{form.routeId}</option>}</select></label>
      <label>İlk kalkış (isteğe bağlı)<input type="datetime-local" value={form.departureFrom} onChange={e => update("departureFrom", e.target.value)} /></label>
      <label>Son kalkış sınırı (isteğe bağlı)<input type="datetime-local" min={form.departureFrom} value={form.departureUntil} onChange={e => update("departureUntil", e.target.value)} /></label>
      <div><label className="campaign-check"><input type="checkbox" checked={form.isActive} onChange={e => update("isActive", e.target.checked)} />Kampanya aktif</label><label className="campaign-check"><input type="checkbox" checked={form.isPublic} onChange={e => update("isPublic", e.target.checked)} />Kampanyalar sayfasında yayınla</label></div>
    </div><p className="campaign-muted">Kullanım bir sipariş için sayılır; bekleyen ödeme kontenjanı geçici ayırır. Başarısız/süresi dolan ödeme hakkı bırakır. Düzenlenmiş bilet iptal edilirse kullanım hakkı geri verilmez. İndirimler birleşmez; kod yoksa en avantajlı otomatik kampanya seçilir. Her bilet için en az 0,01 TL ödenir.</p><div className="campaign-form-actions"><button type="submit" className="site-button primary">{busy ? "Kaydediliyor..." : "Kampanyayı kaydet"}</button><button type="button" className="site-button secondary" onClick={() => setOpen(false)}>Vazgeç</button></div></fieldset></form>}
    {loading ? <p role="status">Kampanyalar yükleniyor...</p> : result ? <div className="campaign-table-wrap"><table className="campaign-table"><thead><tr><th>Kampanya</th><th>İndirim / koşul</th><th>Tarihler (Türkiye)</th><th>Kullanım</th><th>Durum</th><th>İşlemler</th></tr></thead><tbody>{visible.map(c => <tr key={c.id}><td><strong>{c.name}</strong><small>{c.companyName}</small><small>{c.code ?? "Otomatik kampanya"} · {c.isPublic ? "Yayında" : "Gizli"}</small></td><td>{discountLabel(c)}<small>Min. {money.format(c.minimumSubtotal)}{c.maximumDiscount != null && ` · Tavan ${money.format(c.maximumDiscount)}`}</small><small>{c.routeName ?? "Tüm güzergâhlar"}</small></td><td>{dateTime.format(new Date(c.startsAt))}<small>→ {dateTime.format(new Date(c.endsAt))}</small></td><td>{c.used} kullanıldı · {c.reserved} ayrıldı<small>Toplam {c.usageLimit ?? "sınırsız"} · Hesap başına {c.perUserLimit}</small><small>{money.format(c.totalDiscount)} indirim</small></td><td><span className="campaign-pill">{campaignStatus(c)}</span></td><td><button type="button" disabled={busy} onClick={() => { setForm(fromCampaign(c)); setEditing(c.id); setOpen(true); setError(""); window.scrollTo({ top: 0, behavior: "smooth" }); }}>Düzenle</button><button type="button" disabled={busy} onClick={() => void save({ ...fromCampaign(c), isActive: !c.isActive }, c.id, false)}>{c.isActive ? "Duraklat" : "Etkinleştir"}</button><button type="button" onClick={() => setHistoryId(c.id)}>Kullanımlar</button></td></tr>)}{!visible.length && <tr><td colSpan={6}>{items.length ? "Bu filtreye uygun kampanya bulunamadı." : "Henüz kampanya oluşturulmamış. Yeni kampanya oluşturarak başlayabilirsiniz."}</td></tr>}</tbody></table></div> : null}
    {result && historyId != null && <section className="campaign-form"><div className="campaign-toolbar"><h2>{items.find(c => c.id === historyId)?.name} · Son 50 sipariş</h2><button type="button" onClick={() => setHistoryId(null)}>Kapat</button></div>{historyLoading && <p role="status">Kullanımlar yükleniyor...</p>}{!historyLoading && historyError && <p role="alert">{historyError}</p>}{!historyLoading && !historyError && <div className="campaign-table-wrap"><table className="campaign-table"><thead><tr><th>Sipariş / tarih</th><th>Ödeme</th><th>İndirim öncesi</th><th>İndirim</th><th>Ödenen / beklenen</th><th>İade</th></tr></thead><tbody>{history.map(h => <tr key={h.id}><td>{h.id}<small>{dateTime.format(new Date(h.createdAt))} · {h.tickets} bilet</small></td><td>{({ Paid: "Doğrulandı", Pending: "Bekliyor", Failed: "Başarısız" })[h.paymentStatus] ?? h.paymentStatus}</td><td>{money.format(h.originalAmount)}</td><td>{money.format(h.discountAmount)}</td><td>{money.format(h.amount)}</td><td>{money.format(h.refunded)}</td></tr>)}{!history.length && <tr><td colSpan={6}>Bu kampanya henüz kullanılmadı.</td></tr>}</tbody></table></div>}</section>}
    {result && <p className="campaign-muted">Liste son 500 kampanyayı gösterir. Kayıtlar geçmiş siparişlerin izlenebilmesi için silinmez; duraklatılabilir.</p>}
  </div>;
}
