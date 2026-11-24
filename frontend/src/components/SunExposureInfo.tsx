import type { SunExposure } from "../seatCompatibility";

export default function SunExposureInfo({ sun }: { sun?: SunExposure }) {
  const unknown = !sun || sun.side === "Unknown";
  return <details className="sun-exposure">
    <summary><span className="sun-exposure-result"><span aria-hidden="true">{sun?.side === "Night" ? "☾" : "☀"}</span> {unknown ? "Güneş yönü hesaplanamadı." : sun.message}</span><span className="sun-exposure-toggle">Detaylar</span></summary>
    <div className="sun-exposure-details">
      {unknown && <p>{sun?.message || "Güneş yönü için yeterli sefer bilgisi bulunmuyor."}</p>}
      <p>Tahminidir. Yol geometrisi boyunca en fazla 5 dakikalık örneklem kullanılır; trafik, duraklamalar, hava ve gölgelenme hesaba katılmaz. Sol/sağ, otobüsün hareket yönüne göredir; ön taraf soldayken otobüsün solu ekranın altındadır.</p>
      {!unknown && <small>Sol: {sun.leftMinutes} dk · Sağ: {sun.rightMinutes} dk · Gece: {sun.nightMinutes} dk</small>}
    </div>
  </details>;
}
