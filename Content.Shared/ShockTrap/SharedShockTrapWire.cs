using Robust.Shared.Serialization;

namespace Content.Shared.ShockTrap;

// goobstation (entire file) - ShockTrap Wire and Suit Storage Hacking
[Serializable, NetSerializable]
public enum ShockTrapWireActionKey : byte
{
    Key,
    Status,
    Pulsed,
    Electrified,
    PulseCancel,
    ElectrifiedCancel,
    MainWire,
    WireCount,
    CutWires
}
