import type { SeatPosition } from "../seatPlans";
import "./SeatMap.css";

export default function SeatMap({ seats, layout, occupied = [], selected = null, mode = "select", disabled = false, onChoose }: {
  seats: SeatPosition[]; layout: string; occupied?: number[]; selected?: number | number[] | null; mode?: "select" | "block" | "readonly"; disabled?: boolean; onChoose?: (number: number) => void;
}) {
  const known = layout === "2+1" || layout === "2+2";
  return <div className="physical-seat-map">
    <p>{known ? `${layout} koltuk düzeni` : "Eski sefer: fiziksel koltuk yerleşimi doğrulanmamış."}</p>
    <div className={`physical-seat-grid ${layout === "2+1" ? "layout-21" : known ? "layout-22" : "layout-legacy"}`} role="group" aria-label="Numaralı koltuk planı">
      <div className="seat-front">Otobüsün önü ↑</div>
      {known && <span className="seat-corridor" style={{ gridColumn: 3, gridRow: 2 }} aria-hidden="true">Koridor</span>}
      {seats.map(seat => {
        const taken = occupied.includes(seat.number);
        const chosen = Array.isArray(selected) ? selected.includes(seat.number) : selected === seat.number;
        const position = [seat.isWindow ? "pencere" : "", seat.isAisle ? "koridor yanı" : ""].filter(Boolean).join(", ");
        const label = `Koltuk ${seat.number}, ${taken ? "dolu" : seat.isBlocked ? "satışa kapalı" : chosen ? "seçili" : "boş"}${position ? ", " + position : ""}`;
        const style = known ? { gridColumn: seat.column, gridRow: seat.row + 1 } : undefined;
        return <button type="button" key={seat.number} style={style} title={label} aria-label={label} aria-pressed={mode === "block" ? seat.isBlocked : chosen}
          className={`physical-seat ${taken ? "occupied" : seat.isBlocked ? "blocked" : chosen ? "selected" : "available"}`}
          disabled={disabled || mode === "readonly" || taken || (mode === "select" && seat.isBlocked)} onClick={() => onChoose?.(seat.number)}>
          {seat.number}<small>{seat.isBlocked ? "Kapalı" : taken ? "Dolu" : seat.isWindow ? "Pencere" : seat.isAisle ? "Koridor" : "Boş"}</small>
        </button>;
      })}
    </div><p className="physical-seat-legend">Boş · Dolu · Satışa kapalı{mode === "block" ? " — Kapatmak veya açmak için koltuğa tıklayın." : ""}</p>
  </div>;
}
