import { useState } from "react";
import StaffPhoto from "./StaffPhoto";
import { checkStaffFile, staffRequest } from "../staffProfiles";
import type { StaffProfile } from "../staffProfiles";
import { getTodayInTurkey } from "../dateUtils";

const blank = { fullName: "", duty: "Driver", phone: "", isActive: true, careerStartYear: "", employmentStartDate: "", emergencyContactName: "", emergencyContactRelationship: "", emergencyContactPhone: "", languages: [] as string[], licenseClasses: [] as string[], vehicleTypes: [] as string[], serviceExperience: false, serviceExperienceNotes: "", baggageExperience: false, baggageExperienceNotes: "", passengerCommunicationExperience: false, passengerCommunicationExperienceNotes: "" };
type Form = typeof blank;
function initial(person?: StaffProfile): Form { if (!person) return { ...blank }; return Object.fromEntries(Object.entries(blank).map(([key, value]) => [key, person[key as keyof StaffProfile] ?? value]).map(([key, value]) => [key, key === "careerStartYear" ? String(value) : value])) as Form; }
function Choices({ label, values, selected, change }: { label: string; values: string[]; selected: string[]; change: (items: string[]) => void }) {
  return <fieldset className="staff-choices"><legend>{label}</legend>{Array.from(new Set([...values, ...selected])).map(value => <label className="staff-check" key={value}><input type="checkbox" checked={selected.includes(value)} onChange={e => change(e.target.checked ? [...selected, value] : selected.filter(v => v !== value))} />{value}</label>)}</fieldset>;
}
export default function StaffProfileEditor({ person, saved, cancel }: { person?: StaffProfile; saved: (id: number) => Promise<void>; cancel: () => void }) {
  const [form, setForm] = useState<Form>(() => initial(person)); const [photo, setPhoto] = useState<File | null>(null); const [removePhoto, setRemovePhoto] = useState(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const today = getTodayInTurkey(); const year = Number(today.slice(0, 4));
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm(f => ({ ...f, [key]: value }));
  const text = (key: keyof Form, label: string, maxLength: number, type = "text") => <label>{label}<input type={type} maxLength={maxLength} value={String(form[key])} onChange={e => set(key, e.target.value)} /></label>;
  const submit = async () => {
    if (busy) return; setBusy(true); setError("");
    try {
      if (Number(form.careerStartYear) > year || form.employmentStartDate > today) throw new Error("Mesleğe başlama yılı ve işe başlama tarihi gelecekte olamaz.");
      const body = new FormData(); body.append("profile", JSON.stringify({ ...form, careerStartYear: form.careerStartYear ? Number(form.careerStartYear) : null, employmentStartDate: form.employmentStartDate || null }));
      body.append("removePhoto", String(removePhoto)); if (photo) { checkStaffFile(photo, true); body.append("photo", photo); }
      const response = await staffRequest(person ? `/${person.id}/profile` : "/profile", { method: person ? "PUT" : "POST", body });
      const result = await response.json(); await saved(result.id);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Personel kaydedilemedi."); }
    finally { setBusy(false); }
  };
  return <form className="user-form staff-editor" aria-label="Personel kaydı" onSubmit={e => { e.preventDefault(); void submit(); }}><h3>{person ? "Personel düzenle" : "Yeni personel"}</h3>
    {error && <p className="error-message" role="alert">{error}</p>}{busy && <p role="status">Profil ve fotoğraf kaydediliyor…</p>}
    <fieldset disabled={busy}><section className="staff-profile-section"><h3>Kişisel Bilgiler</h3><div className="staff-photo-editor"><StaffPhoto id={person?.id} hasPhoto={person?.hasPhoto} localFile={photo} removed={removePhoto} /><div><label>Profil fotoğrafı<input key={removePhoto ? "removed" : "photo"} type="file" accept="image/jpeg,image/png,image/webp" onChange={e => { const selected = e.target.files?.[0] ?? null; try { if (selected) checkStaffFile(selected, true); setPhoto(selected); setRemovePhoto(false); setError(""); } catch (cause) { e.target.value = ""; setError((cause as Error).message); } }} /></label><p>JPEG, PNG veya WebP · En fazla 5 MB</p>{(photo || person?.hasPhoto) && !removePhoto && <button className="secondary-button" type="button" onClick={() => { setPhoto(null); setRemovePhoto(true); }}>Fotoğrafı kaldır</button>}</div></div>
      <div className="form-grid"><label>Ad soyad<input required minLength={2} maxLength={120} value={form.fullName} onChange={e => set("fullName", e.target.value)} /></label>
      <label>Görev<select aria-label="Görev" value={form.duty} onChange={e => set("duty", e.target.value)}><option value="Driver">Şoför</option><option value="Attendant">Muavin</option></select></label>
      {text("phone", "Telefon (isteğe bağlı)", 32, "tel")}<label>Durum<select aria-label="Durum" value={String(form.isActive)} onChange={e => set("isActive", e.target.value === "true")}><option value="true">Aktif</option><option value="false">Pasif</option></select></label>
      {text("emergencyContactName", "Acil durumda aranacak kişi", 120)}{text("emergencyContactRelationship", "Yakınlık derecesi", 80)}{text("emergencyContactPhone", "Acil durum telefonu", 32, "tel")}</div>
      <Choices label="Konuştuğu diller (birden fazla seçilebilir)" values={["Türkçe", "İngilizce", "Almanca", "Arapça", "Rusça", "Fransızca", "İspanyolca", "Kürtçe", "Farsça", "İtalyanca", "Gürcüce", "Bulgarca"]} selected={form.languages} change={v => set("languages", v)} />
    </section><section className="staff-profile-section"><h3>Mesleki Bilgiler</h3><div className="form-grid"><label>Mesleğe başlama yılı<input type="number" min={1900} max={year} value={form.careerStartYear} onChange={e => set("careerStartYear", e.target.value)} />{form.careerStartYear && Number(form.careerStartYear) <= year && <small>Deneyim: {year - Number(form.careerStartYear)} yıl</small>}</label><label>İşe başlama tarihi<input type="date" max={today} value={form.employmentStartDate} onChange={e => set("employmentStartDate", e.target.value)} /></label></div>
      {form.duty === "Driver" ? <><Choices label="Ehliyet sınıfları" values={["M", "A1", "A2", "A", "B1", "B", "BE", "C1", "C1E", "C", "CE", "D1", "D1E", "D", "DE", "F"]} selected={form.licenseClasses} change={v => set("licenseClasses", v)} /><Choices label="Kullanabildiği araç türleri" values={["Otobüs", "Midibüs", "Minibüs", "Otomobil", "Kamyon", "Çekici"]} selected={form.vehicleTypes} change={v => set("vehicleTypes", v)} /></> : <div className="form-grid">{([
        ["serviceExperience", "serviceExperienceNotes", "Servis deneyimi"], ["baggageExperience", "baggageExperienceNotes", "Bagaj işlemleri deneyimi"], ["passengerCommunicationExperience", "passengerCommunicationExperienceNotes", "Yolcu iletişimi deneyimi"],
      ] as const).map(([key, notes, label]) => <div key={key}><label className="staff-check"><input type="checkbox" checked={form[key]} onChange={e => set(key, e.target.checked)} />{label}</label><label>{label} açıklaması (isteğe bağlı)<textarea maxLength={1000} value={form[notes]} onChange={e => set(notes, e.target.value)} /></label></div>)}</div>}
    </section>{!person && <section className="staff-profile-section"><h3>Belgeler</h3><p>Personeli kaydettikten sonra açılan profilden birden fazla belge ekleyebilirsiniz.</p></section>}
    <div className="detail-actions"><button className="primary-button" disabled={busy} type="submit">Personeli kaydet</button><button className="secondary-button" type="button" disabled={busy} onClick={cancel}>Vazgeç</button></div></fieldset>
  </form>;
}
