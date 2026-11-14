extern alias GSD1;

using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Core.Treasure;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    internal TreasureShuffleProfile? TreasureProfile { get; private set; }
    internal bool TreasureActive => IsAdmitted && AvatarPlugin.ConfiguredTreasureEnabled && TreasureProfile is not null;
    private bool _treasurePrepared;
    private string TreasureSeed => _fixedProfile is not null ? _fixedSeed : Profile.Seed;

    private void ResetTreasureCampaign()
    {
        TreasureProfile = null;
        _treasurePrepared = false;
    }

    private void BeginTreasureCampaign()
    {
        TreasureProfile = AvatarPlugin.ConfiguredTreasureShuffle ? TreasureShuffleProfile.Create(TreasureSeed) : null;
        _treasurePrepared = true;
        if (TreasureProfile is not null)
            Trace("TREASURE NEW GAME: seed=" + TreasureProfile.Seed + "; mapping=" + TreasureProfile.Fingerprint);
    }

    private void RestoreTreasureCampaign(GSD1.SaveDataManager.SaveData save)
    {
        if (!CanExecute || (_fixedProfile is null && State.Origin == RoleAvatarCampaignOrigin.UnboundSave)) return;
        string identity = AvatarSaveIdentity.Compute(save);
        var binding = TreasureBindingStore.Read(identity, CampaignFingerprint, TreasureSeed);
        if (binding is not null)
        {
            TreasureProfile = binding.Profile;
            _treasurePrepared = true;
            Trace(TreasureProfile is null ? "TREASURE RESTORED: disabled for this saved campaign."
                : "TREASURE RESTORED: mapping=" + TreasureProfile.Fingerprint + "; saved policy retained.");
            return;
        }

        BeginTreasureCampaign();
        if (TreasureProfile is not null)
            Trace("TREASURE ADOPTED: unopened chests only; existing collection flags retained; saved campaign seed="
                + TreasureProfile.Seed + "; mapping=" + TreasureProfile.Fingerprint);
    }

    internal void CheckTreasureBindingWritable()
    {
        if (!_treasurePrepared) throw new InvalidOperationException("Treasure campaign policy is not ready for saving.");
        TreasureBindingStore.CheckWritable();
    }

    internal void WriteTreasureBinding(GSD1.SaveDataManager.SaveData save)
    {
        if (!IsActive || !_treasurePrepared)
            throw new InvalidOperationException("Treasure campaign policy is not ready for this native snapshot.");
        TreasureBindingStore.Write(AvatarSaveIdentity.Compute(save), CampaignFingerprint, TreasureSeed, TreasureProfile);
    }
}
