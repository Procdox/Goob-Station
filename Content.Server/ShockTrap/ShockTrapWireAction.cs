using Content.Server.Electrocution;
using Content.Shared.Electrocution;
using Content.Server.Power.Components;
using Content.Server.Wires;
using Content.Shared.ShockTrap;
using Content.Shared.Wires;

namespace Content.Server.Power;

// goobstation (entire file) - ShockTrap Wire and Suit Storage Hacking
// Trapped wire action that causes a device to shock anyone who interacts with said device.
// Does NOT control power. Is independent of PowerWireAction, allowing both.
// use Electrified::requirePower to control if this should rely on power
public sealed partial class ShockTrapWireAction : BaseWireAction
{
    public override Color Color { get; set; } = Color.Red;
    public override string Name { get; set; } = "wire-name-shock-trap";

    [DataField("pulseTimeout")]
    private int _pulseTimeout = 30;

    private ElectrocutionSystem _electrocutionSystem = default!;

    public override object StatusKey => ShockTrapWireActionKey.Status;

    public override StatusLightState? GetLightState(Wire wire)
    {
        if (WiresSystem.TryGetData<int>(wire.Owner, ShockTrapWireActionKey.MainWire, out var main)
            && main != wire.Id)
        {
            return null;
        }

        if (!AllWiresMended(wire.Owner)
                || WiresSystem.TryGetData<bool>(wire.Owner, ShockTrapWireActionKey.Pulsed, out var pulsed)
                && pulsed)
        {
            return StatusLightState.On;
        }

        return StatusLightState.Off;
    }

    private bool AllWiresMended(EntityUid owner)
    {
        return WiresSystem.TryGetData<int?>(owner, ShockTrapWireActionKey.CutWires, out var cut)
               && cut == 0;
    }
    private void SetWireCuts(EntityUid owner, bool isCut)
    {
        if (WiresSystem.TryGetData<int?>(owner, ShockTrapWireActionKey.CutWires, out var cut)
            && WiresSystem.TryGetData<int?>(owner, ShockTrapWireActionKey.WireCount, out var count))
        {
            if (cut == count && isCut
                || cut <= 0 && !isCut)
            {
                return;
            }

            cut = isCut ? cut + 1 : cut - 1;
            WiresSystem.SetData(owner, ShockTrapWireActionKey.CutWires, cut);
        }
    }

    private void SetElectrified(EntityUid used, bool setting, ElectrifiedComponent? electrified = null)
    {
        if (electrified == null
            && !EntityManager.TryGetComponent(used, out electrified))
            return;

        _electrocutionSystem.SetElectrifiedWireCut((used, electrified), setting);
        electrified.Enabled = setting;
    }

    /// <returns>false if failed, true otherwise, or if the entity cannot be electrified</returns>
    private bool TrySetElectrocution(EntityUid user, Wire wire, bool timed = false)
    {
        if (!EntityManager.TryGetComponent<ElectrifiedComponent>(wire.Owner, out var electrified))
        {
            return true;
        }

        // always set this to true
        SetElectrified(wire.Owner, true, electrified);

        var electrifiedAttempt = _electrocutionSystem.TryDoElectrifiedAct(wire.Owner, user);

        // if we were electrified, then return false
        return !electrifiedAttempt;

    }

    private void UpdateElectrocution(Wire wire)
    {
        var anyCut = !AllWiresMended(wire.Owner);

        var activePulse = false;

        if (WiresSystem.TryGetData<bool>(wire.Owner, ShockTrapWireActionKey.Pulsed, out var pulsed))
        {
            activePulse = pulsed;
        }

        // if this is actively pulsed,
        // and there's not already an electrification cancel occurring,
        // we need to start that timer immediately
        if (!WiresSystem.HasData(wire.Owner, ShockTrapWireActionKey.ElectrifiedCancel)
            && activePulse
            && IsPowered(wire.Owner)
            && !anyCut)
        {
            WiresSystem.StartWireAction(wire.Owner, _pulseTimeout, ShockTrapWireActionKey.ElectrifiedCancel, new TimedWireEvent(AwaitElectrifiedCancel, wire));
        }
        else
        {
            if (!activePulse && !anyCut)
            {
                SetElectrified(wire.Owner, false);
            }
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        _electrocutionSystem = EntityManager.System<ElectrocutionSystem>();
    }

    public override bool AddWire(Wire wire, int count)
    {
        if (!WiresSystem.HasData(wire.Owner, ShockTrapWireActionKey.CutWires))
        {
            WiresSystem.SetData(wire.Owner, ShockTrapWireActionKey.CutWires, 0);
        }

        if (count == 1)
        {
            WiresSystem.SetData(wire.Owner, ShockTrapWireActionKey.MainWire, wire.Id);
        }

        WiresSystem.SetData(wire.Owner, ShockTrapWireActionKey.WireCount, count);

        return true;
    }

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);
        if (!TrySetElectrocution(user, wire))
            return false;

        SetWireCuts(wire.Owner, true);

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire)
    {
        base.Mend(user, wire);
        if (!TrySetElectrocution(user, wire))
            return false;

        // Mending any power wire restores shorts.
        WiresSystem.TryCancelWireAction(wire.Owner, ShockTrapWireActionKey.PulseCancel);
        WiresSystem.TryCancelWireAction(wire.Owner, ShockTrapWireActionKey.ElectrifiedCancel);

        SetWireCuts(wire.Owner, false);

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire)
    {
        base.Pulse(user, wire);
        WiresSystem.TryCancelWireAction(wire.Owner, ShockTrapWireActionKey.ElectrifiedCancel);

        var electrocuted = !TrySetElectrocution(user, wire, true);

        if (WiresSystem.TryGetData<bool>(wire.Owner, ShockTrapWireActionKey.Pulsed, out var pulsedKey) && pulsedKey)
            return;

        WiresSystem.SetData(wire.Owner, ShockTrapWireActionKey.Pulsed, true);
        WiresSystem.StartWireAction(wire.Owner, _pulseTimeout, ShockTrapWireActionKey.PulseCancel, new TimedWireEvent(AwaitPulseCancel, wire));
    }

    public override void Update(Wire wire)
    {
        UpdateElectrocution(wire);

        if (!IsPowered(wire.Owner))
        {
            if (!WiresSystem.TryGetData<bool>(wire.Owner, ShockTrapWireActionKey.Pulsed, out var pulsed)
                || !pulsed)
            {
                WiresSystem.TryCancelWireAction(wire.Owner, ShockTrapWireActionKey.ElectrifiedCancel);
                WiresSystem.TryCancelWireAction(wire.Owner, ShockTrapWireActionKey.PulseCancel);
            }
        }
    }

    private void AwaitElectrifiedCancel(Wire wire)
    {
        if (AllWiresMended(wire.Owner))
        {
            SetElectrified(wire.Owner, false);
        }
    }

    private void AwaitPulseCancel(Wire wire)
    {
        WiresSystem.SetData(wire.Owner, ShockTrapWireActionKey.Pulsed, false);
    }
}
