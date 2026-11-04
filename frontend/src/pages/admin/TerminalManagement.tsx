import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiUrl } from "../../api";

type Terminal = { id: number; name: string; city: string; address: string; isActive: boolean };
const empty = { name: "", city: "", address: "", isActive: true };
export default function TerminalManagement() {
  const navigate = useNavigate();
  const [items, setItems] = useState<Terminal[]>([]); const [form, setForm] = useState(empty); const [id, setId] = useState<number | null>(null);
  const [revision, setRevision] = useState(0); const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const request = useCallback(async (path: string, options?: RequestInit) => {
    const response = await fetch(apiUrl(path), { ...options, headers: { Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" }, cache: "no-store" });
    if (response.status === 401) { sessionStorage.removeItem("travela_token"); sessionStorage.removeItem("travela_user"); navigate("/login", { replace: true }); }
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.message ?? "Terminal işlemi tamamlanamadı."); return data;
  }, [navigate]);
  useEffect(() => { const abort = new AbortController(); request("/api/terminals?includeInactive=true", { signal: abort.signal }).then(data => { if (!abort.signal.aborted) setItems(data); }).catch(cause => { if (!abort.signal.aborted) setError(cause.message); }); return () => abort.abort(); }, [request, revision]);
  return <div className="admin-dashboard"><div className="dashboard-heading"><h1>Terminal yönetimi</h1><p>Otogar ve biniş noktalarının şehir, açık adres ve kullanım durumunu yönetin.</p></div>
    {error && <p role="alert" className="form-alert error">{error}</p>}{message && <p role="status" className="form-alert success">{message}</p>}
    <section className="dashboard-section"><h2>{id ? "Terminali düzenle" : "Terminal ekle"}</h2><form className="user-form" onSubmit={async event => {
      event.preventDefault(); if (busy) return; setBusy(true); setError(""); setMessage("");
      try { const result = await request(`/api/terminals${id ? "/" + id : ""}`, { method: id ? "PUT" : "POST", body: JSON.stringify(form) }); setMessage(result.message); setForm(empty); setId(null); setRevision(value => value + 1); }
      catch (cause) { setError(cause instanceof Error ? cause.message : "Terminal kaydedilemedi."); } finally { setBusy(false); }
    }}><div className="form-grid">
      <label>Terminal adı<input required maxLength={160} value={form.name} disabled={busy} onChange={event => setForm(current => ({ ...current, name: event.target.value }))} /></label>
      <label>Şehir<input required maxLength={100} value={form.city} disabled={busy} onChange={event => setForm(current => ({ ...current, city: event.target.value }))} /></label>
      <label>Açık adres<textarea aria-label="Açık adres" required maxLength={500} value={form.address} disabled={busy} onChange={event => setForm(current => ({ ...current, address: event.target.value }))} /></label>
      <label>Terminal durumu<select aria-label="Terminal durumu" value={String(form.isActive)} disabled={busy} onChange={event => setForm(current => ({ ...current, isActive: event.target.value === "true" }))}><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
    </div><p>Adres düzenlemeleri mevcut seferlerin kayıtlı biniş bilgisini değiştirmez. Pasif terminal yeni satışlara kapatılır.</p><div className="detail-actions"><button type="submit" className="primary-button" disabled={busy}>{busy ? "Kaydediliyor..." : "Terminali kaydet"}</button>{id && <button type="button" className="secondary-button" disabled={busy} onClick={() => { setId(null); setForm(empty); }}>Vazgeç</button>}</div></form></section>
    <section className="dashboard-section"><div className="table-wrap"><table className="data-table"><thead><tr><th>Terminal</th><th>Şehir</th><th>Adres</th><th>Durum</th><th>İşlem</th></tr></thead><tbody>{items.map(item => <tr key={item.id}><td>{item.name}</td><td>{item.city}</td><td style={{ overflowWrap: "anywhere" }}>{item.address}</td><td>{item.isActive ? "Aktif" : "Pasif"}</td><td><button type="button" className="link-button" onClick={() => { setId(item.id); setForm(item); setMessage(""); setError(""); }}>Düzenle</button></td></tr>)}</tbody></table></div>{!items.length && <p>Henüz terminal eklenmemiş.</p>}</section>
  </div>;
}
