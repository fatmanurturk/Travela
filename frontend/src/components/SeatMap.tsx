import type { CSSProperties } from "react";
import type { SeatPosition } from "../seatPlans";
import { seatConflict } from "../seatCompatibility";
import type { Gender, SeatOccupancy, SunExposure } from "../seatCompatibility";
import "./SeatMap.css";

export default function SeatMap({ seats, layout, occupied = [], selected = null, mode = "select", disabled = false, onChoose, occupancies = [], genders = {}, selectionGender = "", sun, onIncompatible }: {
  seats: SeatPosition[]; layout: string; occupied?: number[]; selected?: number | number[] | null; mode?: "select" | "block" | "readonly" | "edit"; disabled?: boolean; onChoose?: (number: number) => void;
  occupancies?: SeatOccupancy[]; genders?: Record<number, Gender | "">; selectionGender?: Gender | ""; sun?: SunExposure; onIncompatible?: (message: string) => void;
}) {
  const known = ["2+1", "2+2", "Custom"].includes(layout);
  const dimensions = {
    "--seat-rows": Math.max(1, ...seats.map(seat => seat.row)),
    "--lower-rows": seats.some(seat => seat.column === 5) ? 2 : 1,
  } as CSSProperties;
  return <div className="physical-seat-map" style={dimensions}>
    <p>{known ? (layout === "Custom" ? "Özel" : layout) + " koltuk düzeni · Ön taraf solda ←" : "Eski sefer: fiziksel koltuk yerleşimi doğrulanmamış."}</p>
    <div className="bus-scroll" tabIndex={0} aria-label="Otobüs koltuk planı">
    <div className={"physical-seat-grid " + (known ? "layout-known" : "layout-legacy")} role="group" aria-label="Numaralı koltuk planı">
      <div className="seat-front"><span className="driver-wheel" aria-hidden="true"><svg viewBox="0 0 40 40" fill="none" stroke="currentColor" strokeWidth="3"><circle cx="20" cy="20" r="17" /><circle cx="20" cy="20" r="4" /><path d="M4 15 16 19M36 15 24 19M20 24V37" /></svg></span><span>Şoför</span><b>← Ön</b></div>
      {known && <span className="seat-corridor" style={{ gridColumn: '2 / -2', gridRow: 3 }} aria-hidden="true">← Hareket yönü · Koridor</span>}
      {known && (sun?.side === "Left" || sun?.side === "Right") && <span className={"bus-sun sun-" + sun.side.toLowerCase()} title={"Güneş: otobüsün " + (sun.side === "Left" ? "sol" : "sağ") + " tarafı"}>☀</span>}
      {seats.map(seat => {
        const occupancy = occupancies.find(s => s.seatNumber === seat.number);
        const taken = occupied.includes(seat.number) || !!occupancy;
        const chosen = Array.isArray(selected) ? selected.includes(seat.number) : selected === seat.number;
        const chosenGender = chosen && !taken && !seat.isBlocked ? genders[seat.number] : "";
        const conflict = mode === "select" ? seatConflict(seats, seat.number, chosen ? genders[seat.number] : selectionGender, occupancies, genders) : "";
        const status = taken ? occupancy?.kind === "Hold" ? "Geçici" : occupancy?.gender === "Female" ? "Kadın" : occupancy?.gender === "Male" ? "Erkek" : "Dolu" : seat.isBlocked ? "Kapalı" : chosen ? "Seçili" : conflict ? "Uyumsuz" : "Boş";
        const position = [seat.isWindow ? "pencere" : "", seat.isAisle ? "koridor yanı" : ""].filter(Boolean).join(", ");
        const genderLabel = chosenGender === "Female" ? ", Kadın" : chosenGender === "Male" ? ", Erkek" : "";
        const label = "Koltuk " + seat.number + ", " + status + genderLabel +
          (position ? ", " + position : "") + (conflict ? ". " + conflict : "");
        const selectedClass = "selected" + (chosenGender === "Female" ? " female" : chosenGender === "Male" ? " male" : "");
        const seatClass = taken
          ? "occupied " + (occupancy?.kind === "Hold" ? "held" :
            occupancy?.gender === "Female" ? "female" : occupancy?.gender === "Male" ? "male" : "")
          : seat.isBlocked ? "blocked" : chosen ? selectedClass : conflict ? "incompatible" : "available";
        return <button type="button" key={seat.number} style={known ? { gridColumn: seat.row + 1, gridRow: seat.column } : undefined} title={label} aria-label={label} aria-pressed={mode === "block" ? seat.isBlocked : chosen}
          className={"physical-seat " + seatClass} aria-haspopup={mode === "select" ? "dialog" : undefined}
          disabled={disabled || mode === "readonly" || taken || (mode === "select" && seat.isBlocked)} onClick={() => { if (conflict && !chosen) onIncompatible?.(conflict); else onChoose?.(seat.number); }}>
          <span className="seat-number">{seat.number}</span><small>{status}</small>
          {mode === "select" && chosen && !taken && !seat.isBlocked &&
            <span className="seat-selected-mark" aria-hidden="true">✓</span>}
        </button>;
      })}
    </div></div><div className="physical-seat-legend"><span className="legend-available">□ Boş</span><span className="legend-male">♂ Dolu – Erkek</span><span className="legend-female">♀ Dolu – Kadın</span><span className="legend-selected">✓ Seçili</span><span className="legend-blocked">▧ Kapalı / Geçici</span>{mode === "block" && <span>Kapatmak veya açmak için koltuğa tıklayın.</span>}</div>
    {sun && <div className="sun-exposure" role="status"><strong>{sun.side === "Night" ? "☾" : "☀"} {sun.message}</strong><p>Tahminidir. Yol geometrisi boyunca en fazla 5 dakikalık örneklem kullanılır; trafik, duraklamalar, hava ve gölgelenme hesaba katılmaz. Sol/sağ, otobüsün hareket yönüne göredir; ön taraf soldayken otobüsün solu ekranın altındadır.</p>{sun.side !== "Unknown" && <small>Sol: {sun.leftMinutes} dk · Sağ: {sun.rightMinutes} dk · Gece: {sun.nightMinutes} dk</small>}</div>}
  </div>;
}
