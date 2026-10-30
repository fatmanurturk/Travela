import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import SiteHeader from "../components/SiteHeader";

type UserProfile = {
  id: number;
  fullName: string;
  email: string;
  role: string;
};

async function getErrorMessage(
  response: Response,
  fallback: string
): Promise<string> {
  const data = await response.json().catch(() => null);
  return typeof data?.message === "string"
    ? data.message
    : fallback;
}

function Profile() {
  const navigate = useNavigate();

  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [fullName, setFullName] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const [currentPassword, setCurrentPassword] = useState("");
const [newPassword, setNewPassword] = useState("");
const [confirmPassword, setConfirmPassword] = useState("");
const [changingPassword, setChangingPassword] = useState(false);
const [passwordError, setPasswordError] = useState("");

  useEffect(() => {
    const controller = new AbortController();

    async function loadProfile() {
      setLoading(true);
      setError("");

      try {
        const token = sessionStorage.getItem("travela_token");

        if (!token) {
          navigate("/login", { replace: true });
          return;
        }

        const response = await fetch("/api/profile", {
          headers: {
            Authorization: `Bearer ${token}`,
          },
          signal: controller.signal,
        });

        if (response.status === 401) {
          sessionStorage.removeItem("travela_token");
          sessionStorage.removeItem("travela_user");
          navigate("/login", { replace: true });
          return;
        }

        if (!response.ok) {
          throw new Error(
            await getErrorMessage(
              response,
              "Profil bilgileri yüklenemedi."
            )
          );
        }

        const data: UserProfile = await response.json();

        if (!controller.signal.aborted) {
          setProfile(data);
          setFullName(data.fullName);
        }
      } catch (err) {
        if (!controller.signal.aborted) {
          setError(
            err instanceof Error
              ? err.message
              : "Profil yüklenirken bir hata oluştu."
          );
        }
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      }
    }

    void loadProfile();

    return () => controller.abort();
  }, [navigate, reloadKey]);

  const handleSave = async (
    event: React.FormEvent<HTMLFormElement>
  ) => {
    event.preventDefault();

    if (saving) return;

    const trimmedName = fullName.trim();

    if (!trimmedName) {
      setError("Ad soyad boş olamaz.");
      return;
    }

    if (trimmedName === profile?.fullName) return;

    setSaving(true);
    setError("");

    try {
      const token = sessionStorage.getItem("travela_token");

      if (!token) {
        navigate("/login", { replace: true });
        return;
      }

      const response = await fetch("/api/profile", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          fullName: trimmedName,
        }),
      });

      if (response.status === 401) {
        sessionStorage.removeItem("travela_token");
        sessionStorage.removeItem("travela_user");
        navigate("/login", { replace: true });
        return;
      }

      if (!response.ok) {
        throw new Error(
          await getErrorMessage(
            response,
            "Profil güncellenemedi."
          )
        );
      }

      window.alert(
        "Profiliniz güncellendi. Lütfen yeniden giriş yapın."
      );

      sessionStorage.removeItem("travela_token");
      sessionStorage.removeItem("travela_user");

      navigate("/login", { replace: true });
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Kaydetme sırasında bir hata oluştu."
      );
    } finally {
      setSaving(false);
    }
  };
const handleChangePassword = async (
  event: React.FormEvent<HTMLFormElement>
) => {
  event.preventDefault();

  if (changingPassword) return;

  setPasswordError("");

  if (!currentPassword || !newPassword || !confirmPassword) {
    setPasswordError("Tüm şifre alanlarını doldurun.");
    return;
  }

  if (newPassword.length < 8) {
    setPasswordError("Yeni şifre en az 8 karakter olmalıdır.");
    return;
  }

  if (newPassword !== confirmPassword) {
    setPasswordError("Yeni şifre ile tekrar alanı eşleşmiyor.");
    return;
  }

  if (currentPassword === newPassword) {
    setPasswordError("Yeni şifre mevcut şifre ile aynı olamaz.");
    return;
  }

  setChangingPassword(true);

  try {
    const token = sessionStorage.getItem("travela_token");

    if (!token) {
      navigate("/login", { replace: true });
      return;
    }

    const response = await fetch("/api/profile/change-password", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        currentPassword,
        newPassword,
        confirmPassword,
      }),
    });

    if (!response.ok) {
      throw new Error(
        await getErrorMessage(
          response,
          "Şifre değiştirilemedi. Oturumunuz sona ermişse yeniden giriş yapın."
        )
      );
    }

    setCurrentPassword("");
    setNewPassword("");
    setConfirmPassword("");

    sessionStorage.removeItem("travela_token");
    sessionStorage.removeItem("travela_user");

    window.alert(
      "Şifreniz değiştirildi. Yeni şifrenizle tekrar giriş yapın."
    );

    navigate("/login", { replace: true });
  } catch (err) {
    setPasswordError(
      err instanceof Error
        ? err.message
        : "Şifre değiştirilirken bir hata oluştu."
    );
  } finally {
    setChangingPassword(false);
  }
};
  return (
    <>
      <SiteHeader />

      <main className="passenger-profile">
        <Link to="/">← Ana sayfaya dön</Link>

        <h1>Profilim</h1>
        <p>Hesap bilgilerini görüntüle ve adını güncelle.</p>

        {loading ? (
          <p role="status">Profil bilgileri yükleniyor...</p>
        ) : !profile ? (
          <div>
            <p role="alert">
              {error || "Profil bilgileri alınamadı."}
            </p>

            <button
              type="button"
              className="site-button secondary"
              onClick={() => setReloadKey((value) => value + 1)}
            >
              Tekrar dene
            </button>
          </div>
               ) : (
          <>
          <form onSubmit={handleSave}>
            <div className="profile-field">

              <label htmlFor="profile-full-name">Ad soyad</label>

              <input
                id="profile-full-name"
                autoComplete="name"
                value={fullName}
                onChange={(event) => setFullName(event.target.value)}
                disabled={saving}
                required
              />
            </div>

            <div className="profile-field">
              <label htmlFor="profile-email">E-posta</label>
              <input
                id="profile-email"
                type="email"
                value={profile.email}
                readOnly
              />
              <small>
                E-posta adresi bu ekrandan değiştirilemez.
              </small>
            </div>

            {error && (
              <p className="profile-error" role="alert">
                {error}
              </p>
            )}

            <button
              type="submit"
              className="site-button primary"
             disabled={
  saving ||
  changingPassword ||
  !fullName.trim() ||
  fullName.trim() === profile.fullName
}
            >
              {saving ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
            </button>
                 </form>

        <section className="profile-password-section">
          <h2>Şifre Değiştir</h2>
          <p>Yeni şifreniz en az 8 karakter olmalıdır.</p>

          <form onSubmit={handleChangePassword}>
            <div className="profile-field">
              <label htmlFor="current-password">Mevcut şifre</label>
              <input
                id="current-password"
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) =>
                  setCurrentPassword(event.target.value)
                }
                disabled={changingPassword || saving}
                required
              />
            </div>

            <div className="profile-field">
              <label htmlFor="new-password">Yeni şifre</label>
              <input
                id="new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                disabled={changingPassword || saving}
                minLength={8}
                required
              />
            </div>

            <div className="profile-field">
              <label htmlFor="confirm-password">
                Yeni şifre tekrar
              </label>
              <input
                id="confirm-password"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) =>
                  setConfirmPassword(event.target.value)
                }
                disabled={changingPassword || saving}
                minLength={8}
                required
              />
            </div>

            {passwordError && (
              <p className="profile-error" role="alert">
                {passwordError}
              </p>
            )}

            <button
              type="submit"
              className="site-button primary"
              disabled={changingPassword || saving}
            >
              {changingPassword
                ? "Şifre değiştiriliyor..."
                : "Şifreyi Değiştir"}
            </button>
          </form>
        </section>
        </>
      )}
      </main>
    </>
  );
}

export default Profile;