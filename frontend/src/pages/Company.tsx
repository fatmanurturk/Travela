import { useEffect, useState } from "react";

const API_URL = "http://localhost:5062/api/buses";

type Bus = {
  id: number;
  companyId: number;
  plateNumber: string;
  brand: string;
  model: string;
  seatCapacity: number;
  isActive: boolean;
};

function Company() {
  const [buses, setBuses] = useState<Bus[]>([]);
  const [plateNumber, setPlateNumber] = useState("");
  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [seatCapacity, setSeatCapacity] = useState(46);
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);

  const storedUser = sessionStorage.getItem("travela_user");
  const user = storedUser ? JSON.parse(storedUser) : null;
  const token = sessionStorage.getItem("travela_token");

  const companyId = user?.companyId;

  const loadBuses = async () => {
    try {
      const response = await fetch(API_URL, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        throw new Error(`Otobüsler alınamadı (${response.status})`);
      }

      const data = await response.json();
      setBuses(data);
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "Bir hata oluştu."
      );
    }
  };

  useEffect(() => {
    void loadBuses();
  }, []);

  const handleAddBus = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    if (!companyId) {
      setMessage("Firma bilgisi bulunamadı.");
      return;
    }

    setLoading(true);
    setMessage("");

    try {
      const response = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          companyId,
          plateNumber,
          brand,
          model,
          seatCapacity,
          isActive: true,
        }),
      });

      if (!response.ok) {
        throw new Error(`Otobüs eklenemedi (${response.status})`);
      }

      setMessage("Otobüs başarıyla eklendi.");

      setPlateNumber("");
      setBrand("");
      setModel("");
      setSeatCapacity(46);

      await loadBuses();
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "Bir hata oluştu."
      );
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ padding: "30px", maxWidth: "1100px", margin: "auto" }}>
      <h1>Travela - Firma Paneli</h1>
      <h2>Otobüs Yönetimi</h2>

      <form
        onSubmit={handleAddBus}
        style={{
          display: "grid",
          gap: "12px",
          maxWidth: "450px",
          marginBottom: "30px",
        }}
      >
        <h3>Yeni Otobüs Ekle</h3>

        <input
          placeholder="Plaka"
          value={plateNumber}
          onChange={(e) => setPlateNumber(e.target.value)}
          required
        />

        <input
          placeholder="Marka"
          value={brand}
          onChange={(e) => setBrand(e.target.value)}
          required
        />

        <input
          placeholder="Model"
          value={model}
          onChange={(e) => setModel(e.target.value)}
          required
        />

        <label>Koltuk Kapasitesi</label>
        <input
          type="number"
          min="1"
          value={seatCapacity}
          onChange={(e) => setSeatCapacity(Number(e.target.value))}
          required
        />

        <button type="submit" disabled={loading}>
          {loading ? "Kaydediliyor..." : "Otobüs Ekle"}
        </button>
      </form>

      {message && <p>{message}</p>}

      <h3>Kayıtlı Otobüsler</h3>

      <table
        border={1}
        cellPadding={12}
        style={{ width: "100%", borderCollapse: "collapse" }}
      >
        <thead>
          <tr>
            <th>Plaka</th>
            <th>Marka</th>
            <th>Model</th>
            <th>Koltuk Kapasitesi</th>
            <th>Durum</th>
          </tr>
        </thead>

        <tbody>
          {buses.map((bus) => (
            <tr key={bus.id}>
              <td>{bus.plateNumber}</td>
              <td>{bus.brand}</td>
              <td>{bus.model}</td>
              <td>{bus.seatCapacity}</td>
              <td>{bus.isActive ? "Aktif" : "Pasif"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default Company;