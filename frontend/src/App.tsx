
import { useState } from "react";

function App() {
  const [mesaj, setMesaj] = useState("");

  const apiTest = async () => {
    try {
      const response = await fetch("http://localhost:5062/api/test");

      if (!response.ok) {
        throw new Error("API isteği başarısız.");
      }

      const data = await response.json();
      setMesaj(data.message);
    } catch (error) {
      console.error(error);
      setMesaj("Backend bağlantısı kurulamadı!");
    }
  };

  return (
    <div style={{ textAlign: "center", marginTop: "100px" }}>
      <h1>Travela'ya Hoş Geldiniz!</h1>
      <p>Otobüs biletinizi kolayca bulun ve satın alın.</p>

      <button onClick={apiTest}>
        Sefer Ara
      </button>

      <p>{mesaj}</p>
    </div>
  );
}

export default App;
