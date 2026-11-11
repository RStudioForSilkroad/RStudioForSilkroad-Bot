using System;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form
{
    readonly CheckBox returnBeforeTravel = new CheckBox(), travelMount = new CheckBox();
    bool mountPending, mountDesired;
    long mountSince;

    bool EnsureTravelMount(bool mounted, long now)
    {
        // When disabled, preserve manual riding except before an NPC/portal transition.
        if (!travelMount.Checked && !mountPending)
            return true;
        if (!snapshot.MountKnown)
        {
            Stop("Binili durum okunamadı; mount işlemi yapılmadı.");
            return false;
        }
        if ((snapshot.MountUid != 0) == mounted)
        {
            mountPending = false;
            return true;
        }
        ReleaseKey();
        if (!reader.IsForeground())
            return false;
        if (mountPending)
        {
            if (mountDesired != mounted)
            {
                Stop("Önceki mount işlemi tamamlanmadı.");
                return false;
            }
            if (now - mountSince > 20000)
            {
                Stop("Ata binme / inme doğrulanamadı; tekrar çağrı gönderilmedi.");
                return false;
            }
            status.Text = mounted ? "Aktif mount çağrıldı; binme doğrulanıyor." : "Binekten inme doğrulanıyor.";
            return false;
        }
        routeWalker.Reset();
        bool accepted = sender.ChangeMount(reader, mounted, snapshot.MountUid);
        if (!accepted)
        {
            Stop(mounted ? "Aktif mount çağrılamadı; Outfit > Mount seçimini kontrol et." :
                "Binekten inme isteği reddedildi.");
            return false;
        }
        Log(mounted ? "Aktif mount çağırma isteği gönderildi." : "Binekten inme isteği gönderildi.");
        mountPending = true;
        mountDesired = mounted;
        mountSince = now;
        status.Text = mounted ? "Oyundaki aktif mount çağrılıyor." : "Geçiş öncesi binekten iniliyor.";
        return false;
    }
}
