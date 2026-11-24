import { useEffect, useRef, useState } from "react";
import { checkStaffFile, documentTypes, staffDate, staffRequest } from "../staffProfiles";
import type { StaffDocument } from "../staffProfiles";
import { getTodayInTurkey } from "../dateUtils";

const blank = { name: "", documentType: "Professional", issuedOn: "", expiresOn: "", isIndefinite: false };
export function DocumentStatus({ document }: { document: StaffDocument }) {
  return <span className={`staff-document-status status-${document.validity.status}`}><strong>{document.validity.label}</strong> · {document.validity.message}</span>;
}
export default function StaffDocuments({ staffId, fullName, documents, changed }: { staffId: number; fullName: string; documents: StaffDocument[]; changed: () => Promise<void> }) {
  const [form, setForm] = useState(blank); const [editing, setEditing] = useState<number | null>(null); const [show, setShow] = useState(false);
  const [file, setFile] = useState<File | null>(null); const [busy, setBusy] = useState(false); const [error, setError] = useState(""); const [message, setMessage] = useState("");
  const [preview, setPreview] = useState<{ url: string; type: string; name: string } | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { if (preview) dialog.current?.showModal(); return () => { if (preview) URL.revokeObjectURL(preview.url); }; }, [preview]);
  const action = async (work: () => Promise<void>) => {
    if (busy) return; setBusy(true); setError(""); setMessage("");
    try { await work(); } catch (cause) { setError(cause instanceof Error ? cause.message : "Belge işlemi tamamlanamadı."); } finally { setBusy(false); }
  };
  const save = () => action(async () => {
    if (file) checkStaffFile(file, false);
    const body = new FormData(); Object.entries(form).forEach(([key, value]) => { if (key !== "expiresOn" || !form.isIndefinite) body.append(key, String(value)); });
    if (file) body.append("file", file);
    await staffRequest(`/${staffId}/documents${editing ? `/${editing}` : ""}`, { method: editing ? "PUT" : "POST", body });
    setShow(false); setFile(null); setMessage("Belge kaydedildi."); await changed();
  });
  const openFile = (doc: StaffDocument, download: boolean) => action(async () => {
    const r = await staffRequest(`/${staffId}/documents/${doc.id}/file${download ? "?download=true" : ""}`);
    const blob = await r.blob(); const url = URL.createObjectURL(blob);
    if (download) { const a = document.createElement("a"); a.href = url; a.download = doc.fileName; document.body.appendChild(a); a.click(); a.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 1000); setMessage("Belge indirmesi başlatıldı."); }
    else setPreview({ url, type: doc.contentType, name: doc.name });
  });
  return <section className="staff-profile-section" aria-label="Belgeler"><div className="page-header"><h3>Belgeler</h3><button type="button" className="secondary-button" disabled={busy} onClick={() => { setForm(blank); setEditing(null); setFile(null); setShow(true); setError(""); }}>Belge ekle</button></div>
    <p>PDF, JPEG, PNG veya WebP · Belge başına en fazla 10 MB. Tarihler Türkiye saatine göre değerlendirilir.</p>
    {error && <p className="error-message" role="alert">{error}</p>}{message && <p role="status">{message}</p>}{busy && <p role="status">İşlem sürüyor, dosya yükleniyor veya alınıyor…</p>}
    {show && <form className="user-form" aria-label="Belge kaydı" onSubmit={e => { e.preventDefault(); void save(); }}><h4>{editing ? "Belgeyi güncelle" : "Yeni belge"}</h4><fieldset disabled={busy}><div className="form-grid">
      <label>Belge adı<input required maxLength={160} value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} /></label>
      <label>Belge türü<select value={form.documentType} onChange={e => setForm(f => ({ ...f, documentType: e.target.value }))}>{Object.entries(documentTypes).map(([key, title]) => <option key={key} value={key}>{title}</option>)}</select></label>
      <label>Düzenlenme tarihi<input required type="date" max={getTodayInTurkey()} value={form.issuedOn} onChange={e => setForm(f => ({ ...f, issuedOn: e.target.value }))} /></label>
      <label className="staff-check"><input type="checkbox" checked={form.isIndefinite} onChange={e => setForm(f => ({ ...f, isIndefinite: e.target.checked }))} />Süresiz belge</label>
      {!form.isIndefinite && <label>Son geçerlilik tarihi<input required type="date" min={form.issuedOn || undefined} value={form.expiresOn} onChange={e => setForm(f => ({ ...f, expiresOn: e.target.value }))} /></label>}
      <label>{editing ? "Dosyayı değiştir (isteğe bağlı)" : "Belge dosyası"}<input key={editing ?? "new"} required={!editing} type="file" accept="application/pdf,image/jpeg,image/png,image/webp" onChange={e => { const selected = e.target.files?.[0] ?? null; try { if (selected) checkStaffFile(selected, false); setFile(selected); setError(""); } catch (cause) { setFile(null); e.target.value = ""; setError((cause as Error).message); } }} /></label>
    </div><div className="detail-actions"><button className="primary-button" disabled={busy} type="submit">Belgeyi kaydet</button><button className="secondary-button" type="button" onClick={() => setShow(false)}>Vazgeç</button></div></fieldset></form>}
    {!documents.length && <p>Henüz belge eklenmedi.</p>}
    <div className="staff-document-list">{documents.map(doc => <article className="staff-document" key={doc.id}><h4>{doc.name}</h4><p>{fullName} · {documentTypes[doc.documentType] ?? doc.documentType}</p><DocumentStatus document={doc} /><p>Düzenlenme: {staffDate(doc.issuedOn)} · Son geçerlilik: {doc.isIndefinite ? "Süresiz" : staffDate(doc.expiresOn)}</p><p>{doc.fileName} · {(doc.size / 1024 / 1024).toFixed(2)} MB</p>
      <div className="detail-actions"><button type="button" className="secondary-button" disabled={busy} onClick={() => void openFile(doc, false)}>Görüntüle</button><button type="button" className="secondary-button" disabled={busy} onClick={() => void openFile(doc, true)}>İndir</button>
      <button type="button" className="secondary-button" disabled={busy} onClick={() => { setEditing(doc.id); setForm({ name: doc.name, documentType: doc.documentType, issuedOn: doc.issuedOn, expiresOn: doc.expiresOn ?? "", isIndefinite: doc.isIndefinite }); setFile(null); setShow(true); setError(""); }}>Belgeyi düzenle</button>
      <button type="button" className="secondary-button" disabled={busy} onClick={() => { if (window.confirm(`“${doc.name}” belgesi ve dosyası silinsin mi?`)) void action(async () => { await staffRequest(`/${staffId}/documents/${doc.id}?confirm=true`, { method: "DELETE" }); setMessage("Belge silindi."); if (editing === doc.id) setShow(false); await changed(); }); }}>Belgeyi sil</button></div>
    </article>)}</div>
    {preview && <dialog ref={dialog} className="staff-file-preview" aria-label={preview.name} onCancel={() => setPreview(null)}><div className="page-header"><h3>{preview.name}</h3><button type="button" className="secondary-button" onClick={() => setPreview(null)}>Önizlemeyi kapat</button></div>{preview.type === "application/pdf" ? <iframe src={preview.url} title={preview.name} /> : <img src={preview.url} alt={preview.name} />}</dialog>}
  </section>;
}
