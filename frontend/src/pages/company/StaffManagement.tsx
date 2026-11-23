import { useCallback, useEffect, useState } from "react";
import { apiUrl } from "../../api";
import type { StaffMember } from "../../components/TripCrewFields";
import "../Admin.css";
import "../../components/TripCrew.css";

const blank = { fullName: "", duty: "Driver", phone: "", isActive: true };
export default function StaffManagement() {
  const [people, setPeople] = useState<StaffMember[]>([]); const [search, setSearch] = useState("");
  const [duty, setDuty] = useState(""); const [active, setActive] = useState("");
  const [form, setForm] = useState(blank); const [editing, setEditing] = useState<number | null>(null); const [show, setShow] = useState(false);
  const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const headers = () => ({ Authorization: `Bearer ${sessionStorage.getItem("travela_token")}`, "Content-Type": "application/json" });
  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    try {
      const params = new URLSearchParams(); if (search.trim()) params.set("search", search.trim()); if (duty) params.set("duty", duty); if (active) params.set("isActive", active);
      const r = await fetch(apiUrl(`/api/company/staff?${params}`), { headers: headers(), signal, cache: "no-store" });
      const body = await r.json(); if (!r.ok || !Array.isArray(body)) throw new Error(body.message ?? "Personeller alınamadı.");
      if (!signal?.aborted) { setPeople(body); setError(""); }
    } catch (cause) { if (!signal?.aborted) setError(cause instanceof Error ? cause.message : "Personeller alınamadı."); }
    finally { if (!signal?.aborted) setLoading(false); }
  }, [search, duty, active]);
  useEffect(() => { const abort = new AbortController(); void load(abort.signal); return () => abort.abort(); }, [load]);
  const save = async (id: number | null, value: typeof blank) => {
    if (busy) return; setBusy(true); setError(""); setMessage("");
    try {
      const r = await fetch(apiUrl(`/api/company/staff${id ? `/${id}` : ""}`), { method: id ? "PUT" : "POST", headers: headers(), body: JSON.stringify(value) });
      const body = await r.json(); if (!r.ok) throw new Error(body.message ?? (body.errors ? Object.values(body.errors).flat().join(" ") : "Personel kaydedilemedi."));
      setShow(false); setForm(blank); setEditing(null); await load(); setMessage("Personel kaydedildi.");
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Personel kaydedilemedi."); }
    finally { setBusy(false); }
  };
  return <div className="staff-page"><div className="page-header"><h2>Personeller</h2><button type="button" className="primary-button" disabled={busy} onClick={() => { setEditing(null); setForm(blank); setShow(true); setMessage(""); }}>Personel ekle</button></div>
    <p>Şoför ve muavin kayıtlarını ve aktifliklerini yönetin.</p>
    {error && <p role="alert" className="error-message">{error} <button type="button" disabled={busy} onClick={() => void load()}>Yeniden yükle</button></p>}{message && <p role="status">{message}</p>}
    {show && <form className="user-form" aria-label="Personel kaydı" onSubmit={event => { event.preventDefault(); void save(editing, form); }}><h3>{editing ? "Personel düzenle" : "Yeni personel"}</h3><div className="form-grid">
      <label>Ad soyad<input required minLength={2} maxLength={120} value={form.fullName} disabled={busy} onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))} /></label>
      <label>Görev<select aria-label="Görev" value={form.duty} disabled={busy} onChange={e => setForm(f => ({ ...f, duty: e.target.value }))}><option value="Driver">Şoför</option><option value="Attendant">Muavin</option></select></label>
      <label>Telefon (isteğe bağlı)<input type="tel" maxLength={32} value={form.phone} disabled={busy} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} /></label>
      <label>Durum<select aria-label="Durum" value={String(form.isActive)} disabled={busy} onChange={e => setForm(f => ({ ...f, isActive: e.target.value === "true" }))}><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
    </div><div className="detail-actions"><button className="primary-button" disabled={busy} type="submit">Personeli kaydet</button><button type="button" className="secondary-button" disabled={busy} onClick={() => setShow(false)}>Vazgeç</button></div></form>}
    <div className="form-grid"><label>Ad soyada göre ara<input type="search" value={search} onChange={e => setSearch(e.target.value)} /></label>
      <label>Göreve göre filtrele<select value={duty} onChange={e => setDuty(e.target.value)}><option value="">Tüm görevler</option><option value="Driver">Şoför</option><option value="Attendant">Muavin</option></select></label>
      <label>Duruma göre filtrele<select value={active} onChange={e => setActive(e.target.value)}><option value="">Tüm durumlar</option><option value="true">Aktif</option><option value="false">Pasif</option></select></label></div>
    {loading ? <p role="status">Personeller yükleniyor...</p> : <><p>{people.length} personel</p>{!people.length && <p>Aramanızla eşleşen personel bulunamadı.</p>}
      <div className="staff-cards">{people.map(person => <article className="staff-card" key={person.id}><h3>{person.fullName}</h3><p>{person.duty === "Driver" ? "Şoför" : "Muavin"} · {person.isActive ? "Aktif" : "Pasif"}</p>{person.phone && <p>{person.phone}</p>}
        <div className="detail-actions"><button type="button" className="secondary-button" disabled={busy} onClick={() => { setEditing(person.id); setForm({ fullName: person.fullName, duty: person.duty, phone: person.phone ?? "", isActive: person.isActive }); setShow(true); }}>Düzenle</button>
          {person.isActive && <button type="button" className="secondary-button" disabled={busy} onClick={() => void save(person.id, { fullName: person.fullName, duty: person.duty, phone: person.phone ?? "", isActive: false })}>Pasife al</button>}</div></article>)}</div></>}
  </div>;
}
