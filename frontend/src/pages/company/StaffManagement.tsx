import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { staffDate, staffRequest } from "../../staffProfiles";
import type { StaffProfile } from "../../staffProfiles";
import StaffPhoto from "../../components/StaffPhoto";
import StaffProfileEditor from "../../components/StaffProfileEditor";
import StaffDocuments, { DocumentStatus } from "../../components/StaffDocuments";
import "../Admin.css";
import "../../components/TripCrew.css";
import "./StaffManagement.css";

export default function StaffManagement() {
  const [people, setPeople] = useState<StaffProfile[]>([]); const [search, setSearch] = useState("");
  const [duty, setDuty] = useState(""); const [active, setActive] = useState(""); const [documentStatus, setDocumentStatus] = useState("");
  const [editor, setEditor] = useState<StaffProfile | null | undefined>(undefined); const [detail, setDetail] = useState<StaffProfile | null>(null);
  const [params, setParams] = useSearchParams(); const detailId = Number(params.get("detail")) || null;
  const [error, setError] = useState(""); const [message, setMessage] = useState(""); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [revision, setRevision] = useState(0);
  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    try {
      const query = new URLSearchParams(); if (search.trim()) query.set("search", search.trim()); if (duty) query.set("duty", duty); if (active) query.set("isActive", active); if (documentStatus) query.set("documentStatus", documentStatus);
      const r = await staffRequest(`?${query}`, { signal }); const body = await r.json(); if (!Array.isArray(body)) throw new Error("Personeller alınamadı.");
      if (!signal?.aborted) { setPeople(body); setError(""); }
    } catch (cause) { if (!signal?.aborted) setError(cause instanceof Error ? cause.message : "Personeller alınamadı."); }
    finally { if (!signal?.aborted) setLoading(false); }
  }, [search, duty, active, documentStatus]);
  useEffect(() => { const abort = new AbortController(); void load(abort.signal); return () => abort.abort(); }, [load, revision]);
  useEffect(() => {
    const abort = new AbortController(); setDetail(null);
    if (detailId) void (async () => { try { const r = await staffRequest(`/${detailId}`, { signal: abort.signal }); const body = await r.json(); if (!abort.signal.aborted) setDetail(body); } catch (cause) { if (!abort.signal.aborted) setError(cause instanceof Error ? cause.message : "Profil alınamadı."); } })();
    return () => abort.abort();
  }, [detailId, revision]);
  const changed = async () => { const r = await staffRequest(`/${detailId}`); setDetail(await r.json()); await load(); };
  const open = (person: StaffProfile, edit: boolean) => { setEditor(edit ? person : undefined); setParams({ detail: String(person.id) }); setMessage(""); };
  const deactivate = async (person: StaffProfile) => {
    if (busy) return; setBusy(true); setError("");
    try { await staffRequest(`/${person.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullName: person.fullName, duty: person.duty, phone: person.phone, isActive: false }) }); setRevision(v => v + 1); setMessage("Personel pasife alındı."); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "Personel kaydedilemedi."); } finally { setBusy(false); }
  };
  return <div className="staff-page"><div className="page-header"><h2>Personeller</h2><button type="button" className="primary-button" disabled={busy} onClick={() => { setEditor(null); setParams({}); setMessage(""); }}>Personel ekle</button></div>
    <p>Şoför ve muavin profillerini, mesleki bilgilerini ve belgelerini yönetin.</p>
    {error && <p role="alert" className="error-message">{error} <button type="button" disabled={busy} onClick={() => setRevision(v => v + 1)}>Yeniden yükle</button></p>}{message && <p role="status">{message}</p>}
    {editor !== undefined && <StaffProfileEditor key={editor?.id ?? "new"} person={editor ?? undefined} cancel={() => setEditor(undefined)} saved={async id => { setEditor(undefined); setParams({ detail: String(id) }); setRevision(v => v + 1); setMessage("Personel kaydedildi. Belgeleri profilin Belgeler bölümünden yönetebilirsiniz."); }} />}
    {detailId && !detail && <p role="status">Profil yükleniyor…</p>}
    {detail && <section className="staff-detail" aria-label="Personel profili"><div className="page-header"><h2>{detail.fullName}</h2><div className="detail-actions">{editor === undefined && <button className="secondary-button" type="button" onClick={() => setEditor(detail)}>Profili düzenle</button>}<button className="secondary-button" type="button" onClick={() => { setParams({}); setEditor(undefined); }}>Profili kapat</button></div></div>
      {editor === undefined && <><section className="staff-profile-section"><h3>Kişisel Bilgiler</h3><StaffPhoto id={detail.id} hasPhoto={detail.hasPhoto} revision={revision} /><dl className="staff-facts"><div><dt>Görev / Durum</dt><dd>{detail.duty === "Driver" ? "Şoför" : "Muavin"} · {detail.isActive ? "Aktif" : "Pasif"}</dd></div><div><dt>Telefon</dt><dd>{detail.phone || "Belirtilmedi"}</dd></div><div><dt>Acil durumda aranacak kişi</dt><dd>{detail.emergencyContactName || "Belirtilmedi"} · {detail.emergencyContactRelationship || "Yakınlık belirtilmedi"} · {detail.emergencyContactPhone || "Telefon belirtilmedi"}</dd></div><div><dt>Konuştuğu diller</dt><dd>{detail.languages?.join(", ") || "Belirtilmedi"}</dd></div></dl></section>
      <section className="staff-profile-section"><h3>Mesleki Bilgiler</h3><dl className="staff-facts"><div><dt>Mesleğe başlama yılı / Deneyim</dt><dd>{detail.careerStartYear ? `${detail.careerStartYear} · ${detail.experienceYears} yıl` : "Belirtilmedi"}</dd></div><div><dt>İşe başlama tarihi</dt><dd>{staffDate(detail.employmentStartDate)}</dd></div>{detail.duty === "Driver" ? <><div><dt>Ehliyet sınıfları</dt><dd>{detail.licenseClasses?.join(", ") || "Belirtilmedi"}</dd></div><div><dt>Kullanabildiği araç türleri</dt><dd>{detail.vehicleTypes?.join(", ") || "Belirtilmedi"}</dd></div></> : <>{[["Servis deneyimi", detail.serviceExperience, detail.serviceExperienceNotes], ["Bagaj işlemleri deneyimi", detail.baggageExperience, detail.baggageExperienceNotes], ["Yolcu iletişimi deneyimi", detail.passengerCommunicationExperience, detail.passengerCommunicationExperienceNotes]].map(([title, has, notes]) => <div key={String(title)}><dt>{title}</dt><dd>{has ? "Var" : "Belirtilmedi"}{notes ? ` · ${notes}` : ""}</dd></div>)}</>}</dl></section></>}
      <StaffDocuments key={detail.id} staffId={detail.id} fullName={detail.fullName} documents={detail.documents ?? []} changed={changed} />
    </section>}
    <div className="form-grid"><label>Ad soyada göre ara<input type="search" value={search} onChange={e => setSearch(e.target.value)} /></label>
      <label>Göreve göre filtrele<select value={duty} onChange={e => setDuty(e.target.value)}><option value="">Tüm görevler</option><option value="Driver">Şoför</option><option value="Attendant">Muavin</option></select></label>
      <label>Duruma göre filtrele<select value={active} onChange={e => setActive(e.target.value)}><option value="">Tüm durumlar</option><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
      <label>Belge durumuna göre filtrele<select value={documentStatus} onChange={e => setDocumentStatus(e.target.value)}><option value="">Tüm belgeler</option><option value="Expiring">Süresi yaklaşan belgesi olanlar</option><option value="Expired">Süresi dolmuş belgesi olanlar</option></select></label></div>
    {loading ? <p role="status">Personeller yükleniyor...</p> : <><p>{people.length} personel</p>{!people.length && <p>Aramanızla eşleşen personel bulunamadı.</p>}
      <div className="staff-cards">{people.map(person => <article className="staff-card" key={person.id}><div className="staff-card-heading"><StaffPhoto id={person.id} hasPhoto={person.hasPhoto} revision={revision} /><div><h3>{person.fullName}</h3><p>{person.duty === "Driver" ? "Şoför" : "Muavin"} · {person.isActive ? "Aktif" : "Pasif"}</p></div></div>{person.phone && <p>{person.phone}</p>}{person.experienceYears != null && <p>{person.experienceYears} yıl deneyim</p>}
        {(person.documents ?? []).filter(d => d.validity.status === "Expiring" || d.validity.status === "Expired").map(doc => <div className="staff-list-warning" key={doc.id}><Link to={`/company/staff?detail=${person.id}`} onClick={() => setEditor(undefined)}><strong>{person.fullName} · {doc.name}</strong></Link><p>Son geçerlilik: {staffDate(doc.expiresOn)}</p><DocumentStatus document={doc} /></div>)}
        <div className="detail-actions"><button type="button" className="secondary-button" disabled={busy} onClick={() => open(person, false)}>Profili görüntüle</button><button type="button" className="secondary-button" disabled={busy} onClick={() => open(person, true)}>Düzenle</button>
          {person.isActive && <button type="button" className="secondary-button" disabled={busy} onClick={() => void deactivate(person)}>Pasife al</button>}</div></article>)}</div></>}
  </div>;
}
