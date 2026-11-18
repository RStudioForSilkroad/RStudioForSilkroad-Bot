using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

sealed partial class MemorySelectV3Form
{
    readonly CheckBox returnBeforeTravel = new CheckBox(), travelMount = new CheckBox(), backgroundWalk = new CheckBox();
    bool mountPending, mountDesired, caravanRiding;
    long mountSince;

    internal static bool IsCaravanAnimal(uint model, ICollection<int> models)
    {
        return model != 0 && models != null && models.Contains((int)model);
    }

    // True while the character rides one of the saved caravan animals.
    bool RidingCaravanAnimal()
    {
        return snapshot != null && snapshot.MountKnown && snapshot.MountUid != 0 && settings != null &&
            IsCaravanAnimal(snapshot.MountModel, settings.CaravanAnimals);
    }

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
        // A caravan animal is not the stable's mount: no mount or dismount call is sent, the character simply goes on riding it.
        if (RidingCaravanAnimal())
        {
            mountPending = false;
            if (!caravanRiding)
            {
                caravanRiding = true;
                Log("Kervan hayvanına biniliyor (model " + snapshot.MountModel + "): mount / inme komutu gönderilmiyor.");
            }
            return true;
        }
        caravanRiding = false;
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
