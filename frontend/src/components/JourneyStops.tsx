import type { Journey } from "../seatPlans";

export default function JourneyStops({ journey }: { journey: Journey }) {
  const map = (address: string | null | undefined) => address ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}` : null;
  return <section className="journey-stops" aria-label="Terminal ve biniş bilgileri">
    <h3>Terminal ve biniş bilgileri</h3>
    {journey.departureTerminalName || journey.arrivalTerminalName ? <dl className="reservation-facts">
      <div><dt>Kalkış terminali</dt><dd>{journey.departureTerminalName ?? "Belirtilmemiş"}<p>{journey.departureTerminalAddress}</p>{map(journey.departureTerminalAddress) && <a href={map(journey.departureTerminalAddress)!} target="_blank" rel="noopener noreferrer">Kalkış noktasını haritada aç</a>}</dd></div>
      <div><dt>Varış terminali</dt><dd>{journey.arrivalTerminalName ?? "Belirtilmemiş"}<p>{journey.arrivalTerminalAddress}</p>{map(journey.arrivalTerminalAddress) && <a href={map(journey.arrivalTerminalAddress)!} target="_blank" rel="noopener noreferrer">Varış noktasını haritada aç</a>}</dd></div>
    </dl> : <p>Bu eski seferde terminal bilgileri belirtilmemiş. Biniş noktasını firmadan doğrulayın.</p>}
    {journey.boardingInstructions && <p><strong>Biniş açıklaması:</strong> {journey.boardingInstructions}</p>}
  </section>;
}
