import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { apiUrl } from "../api";
import { buildSeats } from "../seatPlans";
import type { SeatPosition } from "../seatPlans";
import { adjacentSeat } from "../seatCompatibility";
import { planError, seatPositionFlags } from "../physicalSeatPlans";
import SeatMap from "./SeatMap";
import "./PhysicalSeatPlanEditor.css";

export type PhysicalPlanDraft = { layout: string; seats: SeatPosition[]; confirmed: boolean };
export default function PhysicalSeatPlanEditor({ path, capacity, initialLayout = "", initialSeats = [], confirmationScope = "", children, save, saved }: {
  path: string; capacity: number; initialLayout?: string; initialSeats?: SeatPosition[]; confirmationScope?: string; children?: ReactNode;
  save?: (draft: PhysicalPlanDraft) => Promise<string>; saved?: () => void;
}) {
  const [layout, setLayout] = useState(["2+1", "2+2", "Custom"].includes(initialLayout) ? initialLayout : "");
  const [seats, setSeats] = useState<SeatPosition[]>(initialSeats);
  const [selected, setSelected] = useState(0); const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState(""); const [failed, setFailed] = useState(false);
  useEffect(() => { setConfirmed(false); }, [confirmationScope]);
  const error = planError(seats, layout, capacity);
  const seat = seats[selected]; const neighbor = seat && !error ? adjacentSeat(seats, seat) : null;
  const update = (index: number, change: Partial<SeatPosition>) => { setSeats(current => seatPositionFlags(current.map((s, i) => i === index ? { ...s, ...change } : s))); setConfirmed(false); setMessage(""); };
  return <details className="physical-plan-editor" open><summary>Fiziksel koltuk planını tanımla / doğrula</summary>
    <p>Kapasite: <strong>{capacity} koltuk</strong>. Önce gerçek araçtaki düzeni seçiniz. Oluşturulan plan bir taslaktır; gerçek numaraları ve konumları kontrol etmeden doğrulamayınız.</p>
    <div className="plan-toolbar"><label>Koltuk düzeni<select aria-label="Koltuk düzeni" value={layout} disabled={busy} onChange={e => { setLayout(e.target.value); if (e.target.value !== "Custom") setSeats([]); setSelected(0); setConfirmed(false); setMessage(""); }}><option value="">Gerçek düzeni seçiniz</option><option value="2+2">2+2 · İki tarafta ikili</option><option value="2+1">2+1 · İkili ve tekli</option><option value="Custom">Özel yerleşim</option></select></label>
      <button type="button" disabled={busy || !layout} onClick={() => { setSeats(buildSeats(layout === "Custom" ? "2+2" : layout, capacity)); setSelected(0); setConfirmed(false); setMessage(""); }}>{layout === "Custom" ? "Özel plan için 2+2 temel taslağı oluştur" : "Kapasiteye göre taslak oluştur"}</button></div>
    <p className="plan-help">Ön soldadır. Üstteki sütun 1–2 otobüsün sağı, alttaki 4–5 soludur; 3 geniş koridordur. Satırlar önden arkaya ilerler. Komşuluk numaralardan değil bu konumlardan hesaplanır.</p>
    {seats.length > 0 && <>
      {error && <p className="plan-error" role="alert">{error}</p>}
      {!error && <SeatMap seats={seats} layout={layout} mode="edit" selected={seat?.number} disabled={busy} onChoose={number => setSelected(seats.findIndex(s => s.number === number))} />}
      {seat && <fieldset className="seat-position-fields" disabled={busy}><legend>Koltuk {seat.number} · Fiziksel konumu düzenle</legend>
        <label>Koltuk numarası<input type="number" min={1} max={capacity} value={seat.number || ""} onChange={e => update(selected, { number: Number(e.target.value) })} /></label>
        <label>Önden satır<input type="number" min={1} max={1000} value={seat.row || ""} onChange={e => update(selected, { row: Number(e.target.value) })} /></label>
        <label>Fiziksel sütun<select aria-label="Fiziksel sütun" value={seat.column} onChange={e => update(selected, { column: Number(e.target.value) })}><option value={1}>1 · Sağ pencere</option><option value={2}>2 · Sağ koridor</option><option value={4}>4 · Sol koridor / tekli</option>{layout !== "2+1" && <option value={5}>5 · Sol pencere</option>}</select></label>
        <label><input type="checkbox" checked={seat.isBlocked} onChange={e => update(selected, { isBlocked: e.target.checked })} />Satışa kapalı</label>
        <p className="seat-neighbor" role="status">Fiziksel eş koltuk: <strong>{error ? "Planı düzeltiniz" : neighbor ? neighbor.number : "Yok · Tekli koltuk"}</strong>. Koridorun karşısı eş koltuk sayılmaz.</p>
      </fieldset>}
      <details><summary>Tüm koltuk konumlarını düzenle ({seats.length})</summary><div className="plan-table-scroll"><table className="data-table"><thead><tr><th>Koltuk no.</th><th>Önden satır</th><th>Sütun</th><th>Eş koltuk</th><th>Kapalı</th></tr></thead><tbody>{seats.map((s, index) => <tr key={index}><td><input aria-label={`Koltuk numarası · Kayıt ${index + 1}`} type="number" value={s.number || ""} disabled={busy} onChange={e => update(index, { number: Number(e.target.value) })} /></td><td><input aria-label={`Satır · Kayıt ${index + 1}`} type="number" value={s.row || ""} disabled={busy} onChange={e => update(index, { row: Number(e.target.value) })} /></td><td><select aria-label={`Sütun · Kayıt ${index + 1}`} value={s.column} disabled={busy} onChange={e => update(index, { column: Number(e.target.value) })}>{[1, 2, 4, ...(layout === "2+1" ? [] : [5])].map(column => <option key={column} value={column}>{column}</option>)}</select></td><td>{error ? "—" : adjacentSeat(seats, s)?.number ?? "Tekli"}</td><td><input aria-label={`Satışa kapalı · Kayıt ${index + 1}`} type="checkbox" checked={s.isBlocked} disabled={busy} onChange={e => update(index, { isBlocked: e.target.checked })} /></td></tr>)}</tbody></table></div></details>
    </>}
    <fieldset disabled={busy} className="plan-targets">{children}</fieldset>
    <label className="plan-confirmation"><input type="checkbox" checked={confirmed} disabled={busy || !!error} onChange={e => setConfirmed(e.target.checked)} />Planın gerçek otobüsün koltuk numaraları, fiziksel konumları ve eş koltuklarıyla eşleştiğini; seçtiğim seferlere uygulanmasını doğruluyorum.</label>
    <button type="button" className="primary-button" disabled={busy || !confirmed || !!error} onClick={async () => {
      setBusy(true); setMessage(""); setFailed(false);
      try {
        const draft = { layout, seats: seatPositionFlags(seats), confirmed };
        if (save) setMessage(await save(draft));
        else { const response = await fetch(apiUrl(path), { method: "PUT", headers: { "Content-Type": "application/json", Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` }, body: JSON.stringify(draft) }); const data = await response.json(); if (!response.ok) throw new Error(data.message ?? "Koltuk planı kaydedilemedi."); setMessage(data.message); }
        setConfirmed(false); saved?.();
      } catch (e) { setFailed(true); setMessage(e instanceof Error ? e.message : "Koltuk planı kaydedilemedi."); } finally { setBusy(false); }
    }}>{busy ? "Plan kaydediliyor..." : "Doğrulanmış planı kaydet"}</button>
    {message && <p className={failed ? "plan-error" : "plan-success"} role={failed ? "alert" : "status"}>{message}</p>}
  </details>;
}
