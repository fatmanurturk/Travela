import { useEffect, useState } from "react";
import { staffRequest } from "../staffProfiles";

export default function StaffPhoto({ id, hasPhoto, revision = 0, localFile, removed = false }: { id?: number; hasPhoto?: boolean; revision?: number; localFile?: File | null; removed?: boolean }) {
  const [url, setUrl] = useState(""); const [error, setError] = useState(false);
  useEffect(() => {
    let active = true; let objectUrl = ""; const abort = new AbortController(); setUrl(""); setError(false);
    if (!removed && localFile) { objectUrl = URL.createObjectURL(localFile); setUrl(objectUrl); }
    else if (!removed && id && hasPhoto) void (async () => {
      try { const r = await staffRequest(`/${id}/photo`, { signal: abort.signal }); const blob = await r.blob(); if (active) { objectUrl = URL.createObjectURL(blob); setUrl(objectUrl); } }
      catch { if (active) setError(true); }
    })();
    return () => { active = false; abort.abort(); if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [id, hasPhoto, revision, localFile, removed]);
  return <div className="staff-photo">{url && !error ? <img src={url} alt="Personel profil fotoğrafı" onError={() => setError(true)} /> : <span>{error ? "Fotoğraf yüklenemedi" : hasPhoto && !removed ? "Yükleniyor…" : "Fotoğraf yok"}</span>}</div>;
}
