namespace BitLocker.Core;

// Common, backend-neutral BitLocker classification. Backends should feed this
// from their best raw status source, then callers can use the resolved visual
// state without duplicating the locked/encrypted/protection ordering rules.
public enum BitLockerLockState
{
    Unknown,
    Unlocked,
    Locked
}

public enum BitLockerEncryptionState
{
    Unknown,
    NotEncrypted,
    Encrypted
}

public enum BitLockerProtectionState
{
    Unknown,
    Off,
    On
}

public enum BitLockerKeyProtectorState
{
    Unknown,
    None,
    Present
}

public enum BitLockerVisualState
{
    Unknown,
    None,
    Locked,
    Unlocked,
    ProtectionOff
}

public readonly record struct BitLockerStateInput(
    BitLockerLockState LockState,
    BitLockerEncryptionState EncryptionState,
    BitLockerProtectionState ProtectionState,
    BitLockerKeyProtectorState KeyProtectorState = BitLockerKeyProtectorState.Unknown);

public readonly record struct BitLockerResolvedState(
    bool IsStatusKnown,
    bool IsBitLockerVolume,
    bool IsLocked,
    bool IsEncrypted,
    bool ProtectionOn,
    bool ProtectionOff,
    bool HasKeyProtectors,
    BitLockerVisualState VisualState)
{
    public bool IsProtectionOff => VisualState == BitLockerVisualState.ProtectionOff;
}

public static class BitLockerVolumeStateResolver
{
    public static BitLockerResolvedState Resolve(BitLockerStateInput input)
    {
        if (input.LockState == BitLockerLockState.Locked)
        {
            return new BitLockerResolvedState(
                IsStatusKnown: true,
                IsBitLockerVolume: true,
                IsLocked: true,
                IsEncrypted: true,
                ProtectionOn: false,
                ProtectionOff: false,
                HasKeyProtectors: input.KeyProtectorState == BitLockerKeyProtectorState.Present,
                VisualState: BitLockerVisualState.Locked);
        }

        // A locked drive is resolved above. If the lock check itself is unknown,
        // expose a problem/unknown visual rather than guessing plain or unlocked.
        if (input.LockState == BitLockerLockState.Unknown)
            return Unknown(input.KeyProtectorState);

        // At this point the drive is known to be unlocked. Encryption Method is
        // the source of truth for whether BitLocker visuals are eligible.
        if (input.EncryptionState == BitLockerEncryptionState.Unknown)
            return Unknown(input.KeyProtectorState);

        if (input.EncryptionState == BitLockerEncryptionState.NotEncrypted)
        {
            return new BitLockerResolvedState(
                IsStatusKnown: true,
                IsBitLockerVolume: false,
                IsLocked: false,
                IsEncrypted: false,
                ProtectionOn: false,
                ProtectionOff: false,
                HasKeyProtectors: input.KeyProtectorState == BitLockerKeyProtectorState.Present,
                VisualState: BitLockerVisualState.None);
        }

        // At this point the drive is encrypted and unlocked. Protection On gets
        // the regular unlocked icon. Protection Off, Unknown, or missing/unread
        // protection status gets the warning/protection-off icon because we
        // cannot confirm the encrypted drive is protected.
        bool protectionOn = input.ProtectionState == BitLockerProtectionState.On;
        bool protectionOff = input.ProtectionState == BitLockerProtectionState.Off;
        BitLockerVisualState visualState = protectionOn
            ? BitLockerVisualState.Unlocked
            : BitLockerVisualState.ProtectionOff;

        return new BitLockerResolvedState(
            IsStatusKnown: true,
            IsBitLockerVolume: true,
            IsLocked: false,
            IsEncrypted: true,
            ProtectionOn: protectionOn,
            ProtectionOff: protectionOff,
            HasKeyProtectors: input.KeyProtectorState == BitLockerKeyProtectorState.Present,
            VisualState: visualState);
    }

    private static BitLockerResolvedState Unknown(BitLockerKeyProtectorState keyProtectorState)
    {
        return new BitLockerResolvedState(
            IsStatusKnown: true,
            IsBitLockerVolume: false,
            IsLocked: false,
            IsEncrypted: false,
            ProtectionOn: false,
            ProtectionOff: false,
            HasKeyProtectors: keyProtectorState == BitLockerKeyProtectorState.Present,
            VisualState: BitLockerVisualState.Unknown);
    }
}
