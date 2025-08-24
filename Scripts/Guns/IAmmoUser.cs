using TMPro;

public interface IAmmoUser
{
    /// Return true if this item wants an ammo HUD.
    bool WantsAmmoUI { get; }

    /// Bind (or unbind with null) the shared ammo TMP label.
    void SetAmmoText(TMP_Text hud);
}
