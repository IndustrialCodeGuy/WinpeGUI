using System.Management;
using BitLocker.Core;
using Shared.Shell.Models;
using Shared.Shell.Utilities;

namespace Shell.Infrastructure.DriveState;

public sealed class DriveStateManager
{
    private const int HResultAccessDenied = unchecked((int)0x80070005);
    private const int HResultNotReady = unchecked((int)0x80070015);
    private const int HResultUnrecognizedVolume = unchecked((int)0x800703ED);
    private const int HResultDeviceNotConnected = unchecked((int)0x8007048F);

    private readonly record struct DriveIssueInfo(
        DriveIssueKind IssueKind,
        int? HResult,
        string? Message)
    {
        public static DriveIssueInfo None => new(DriveIssueKind.None, null, null);

        public static DriveIssueInfo FromException(DriveIssueKind issueKind, Exception ex)
        {
            if (IsBitLockerLockedAccessMessage(ex.Message))
                issueKind = DriveIssueKind.BitLockerLocked;

            return new DriveIssueInfo(issueKind, ex.HResult, ex.Message);
        }

        public static DriveIssueInfo FromIOException(IOException ex)
        {
            if (IsBitLockerLockedAccessMessage(ex.Message))
                return new DriveIssueInfo(DriveIssueKind.BitLockerLocked, ex.HResult, ex.Message);

            DriveIssueKind issueKind = ex.HResult switch
            {
                HResultAccessDenied => DriveIssueKind.AccessDenied,
                HResultNotReady => DriveIssueKind.NotReady,
                HResultUnrecognizedVolume => DriveIssueKind.UnrecognizedVolume,
                HResultDeviceNotConnected => DriveIssueKind.DeviceNotConnected,
                _ => DriveIssueKind.IoError
            };

            return new DriveIssueInfo(issueKind, ex.HResult, ex.Message);
        }
    }

    private readonly record struct BitLockerDriveStatus(
        bool IsStatusKnown,
        bool IsBitLockerVolume,
        bool IsLocked,
        BitLockerVisualState VisualState)
    {
        public bool IsProtectionOff => VisualState == BitLockerVisualState.ProtectionOff;
        public bool HasVisualState => VisualState != BitLockerVisualState.None;
    }

    private readonly BitLockerRuntimeCapabilities _bitLockerCapabilities;
    private readonly Dictionary<string, DriveSnapshot> _drivesByRoot = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, BitLockerDriveStatus> _bitLockerStatusByRoot = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();
    private readonly HashSet<string> _pendingBitLockerRefreshRoots = new(StringComparer.OrdinalIgnoreCase);
    private bool _pendingBitLockerRefreshAll;
    private bool _bitLockerRefreshQueuedOrRunning;

    public event EventHandler<DriveStatesChangedEventArgs>? DriveStatesChanged;

    public DriveStateManager(BitLockerRuntimeCapabilities? bitLockerCapabilities = null)
    {
        _bitLockerCapabilities = bitLockerCapabilities ?? BitLockerRuntimeCapabilities.Detect();
    }

    public void RefreshAll()
    {
        Dictionary<string, DriveSnapshot> newDrivesByRoot = new(StringComparer.OrdinalIgnoreCase);

        foreach (DriveInfo drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            string root = NormalizeDriveRoot(drive.Name);
            newDrivesByRoot[root] = BuildDriveSnapshot(drive, root);
        }

        lock (_sync)
        {
            _drivesByRoot.Clear();

            foreach ((string root, DriveSnapshot snapshot) in newDrivesByRoot)
                _drivesByRoot[root] = snapshot;

            RemoveBitLockerStatusesForMissingDriveRootsNoLock();
        }

        RequestBitLockerStatesRefresh();
    }

    public void RefreshDrive(string pathOrRoot)
    {
        if (string.IsNullOrWhiteSpace(pathOrRoot))
            return;

        string root = NormalizeDriveRoot(pathOrRoot);

        try
        {
            DriveInfo? visibleDrive = null;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (string.Equals(NormalizeDriveRoot(drive.Name), root, StringComparison.OrdinalIgnoreCase))
                {
                    visibleDrive = drive;
                    break;
                }
            }

            if (visibleDrive is null)
            {
                lock (_sync)
                {
                    _drivesByRoot.Remove(root);
                    _bitLockerStatusByRoot.Remove(root);
                }

                return;
            }

            DriveSnapshot snapshot = BuildDriveSnapshot(visibleDrive, root);

            lock (_sync)
            {
                _drivesByRoot[root] = snapshot;
            }
        }
        catch
        {
        }
    }

    public void RequestBitLockerStateRefresh(string pathOrRoot)
    {
        if (!_bitLockerCapabilities.CanReadStatus ||
            string.IsNullOrWhiteSpace(pathOrRoot))
        {
            return;
        }

        QueueBitLockerRefresh(NormalizeDriveRoot(pathOrRoot));
    }

    public void RequestBitLockerStatesRefresh()
    {
        if (!_bitLockerCapabilities.CanReadStatus)
            return;

        QueueBitLockerRefresh(null);
    }

    public IReadOnlyList<DriveSnapshot> GetVisibleDrives()
    {
        lock (_sync)
        {
            return _drivesByRoot.Values
                .OrderBy(d => d.DriveRoot, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public bool TryGetDrive(string pathOrRoot, out DriveSnapshot? drive)
    {
        string root = NormalizeDriveRoot(pathOrRoot);

        lock (_sync)
        {
            if (_drivesByRoot.TryGetValue(root, out DriveSnapshot? found))
            {
                drive = found;
                return true;
            }
        }

        drive = null;
        return false;
    }

    public DriveVisualKind GetVisualKind(string pathOrRoot)
    {
        string root = NormalizeDriveRoot(pathOrRoot);

        bool isBitLockerVolume = false;
        bool isBitLockerLocked = false;
        BitLockerVisualState bitLockerVisualState = BitLockerVisualState.None;

        lock (_sync)
        {
            if (_drivesByRoot.TryGetValue(root, out DriveSnapshot? entry))
                return entry.VisualKind;

            if (_bitLockerStatusByRoot.TryGetValue(root, out var bitLocker))
            {
                isBitLockerVolume = bitLocker.IsBitLockerVolume;
                isBitLockerLocked = bitLocker.IsLocked;
                bitLockerVisualState = bitLocker.VisualState;
            }
        }

        try
        {
            DriveInfo drive = new(root);

            DriveType driveType;
            try
            {
                driveType = drive.DriveType;
            }
            catch
            {
                driveType = DriveType.Unknown;
            }

            bool isSystemDrive = IsSystemDrive(root);
            return ResolveDriveVisualKind(
                driveType,
                isSystemDrive,
                isBitLockerVolume,
                isBitLockerLocked,
                bitLockerVisualState);
        }
        catch
        {
            return DriveVisualKind.Fixed;
        }
    }

    public string GetDisplayName(string pathOrRoot)
    {
        string root = NormalizeDriveRoot(pathOrRoot);

        lock (_sync)
        {
            if (_drivesByRoot.TryGetValue(root, out DriveSnapshot? entry))
                return entry.DisplayName;
        }

        try
        {
            DriveInfo drive = new(root);
            return BuildDriveDisplayName(drive);
        }
        catch
        {
            return root.TrimEnd('\\');
        }
    }

    public void RemoveDrive(string pathOrRoot)
    {
        string root = NormalizeDriveRoot(pathOrRoot);
        bool changed;

        lock (_sync)
        {
            changed = _drivesByRoot.Remove(root);
            changed |= _bitLockerStatusByRoot.Remove(root);
        }

        if (changed)
            OnDriveStatesChanged(root);
    }

    public void RemoveDrives(IEnumerable<string> pathOrRoots)
    {
        string[] roots = pathOrRoots
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeDriveRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
            return;

        bool changed = false;

        lock (_sync)
        {
            foreach (string root in roots)
            {
                changed |= _drivesByRoot.Remove(root);
                changed |= _bitLockerStatusByRoot.Remove(root);
            }
        }

        if (changed)
            OnDriveStatesChanged(roots);
    }

    private void RemoveBitLockerStatusesForMissingDriveRootsNoLock()
    {
        if (_bitLockerStatusByRoot.Count == 0)
            return;

        foreach (string root in _bitLockerStatusByRoot.Keys.ToArray())
        {
            if (!_drivesByRoot.ContainsKey(root))
                _bitLockerStatusByRoot.Remove(root);
        }
    }

    private DriveSnapshot BuildDriveSnapshot(DriveInfo drive, string root)
    {
        DriveType driveType;
        bool isReady = false;
        long? totalSizeBytes = null;
        long? freeSpaceBytes = null;
        string? volumeLabel = null;

        try
        {
            driveType = drive.DriveType;
        }
        catch
        {
            driveType = DriveType.Unknown;
        }

        try
        {
            isReady = drive.IsReady;
            if (isReady)
            {
                totalSizeBytes = drive.TotalSize;
                freeSpaceBytes = drive.AvailableFreeSpace;
                volumeLabel = drive.VolumeLabel;
            }
        }
        catch
        {
        }

        bool isSystemDrive = IsSystemDrive(root);
        bool isBitLockerVolume = false;
        bool isBitLockerLocked = false;
        BitLockerVisualState bitLockerVisualState = BitLockerVisualState.None;

        lock (_sync)
        {
            if (_bitLockerStatusByRoot.TryGetValue(root, out var bitLocker))
            {
                isBitLockerVolume = bitLocker.IsBitLockerVolume;
                isBitLockerLocked = bitLocker.IsLocked;
                bitLockerVisualState = bitLocker.VisualState;
            }
        }

        DriveIssueInfo issue = ClassifyDriveIssue(driveType, isReady, isBitLockerLocked, root);

        return new DriveSnapshot
        {
            DriveRoot = root,
            DisplayName = BuildDriveDisplayName(root, volumeLabel, isReady),
            VolumeLabel = volumeLabel,
            DriveType = driveType,
            IsReady = isReady,
            IsPresent = true,
            IsSystemDrive = isSystemDrive,
            IsBitLockerProtected = isBitLockerVolume,
            IsBitLockerLocked = isBitLockerLocked,
            IssueKind = issue.IssueKind,
            IssueHResult = issue.HResult,
            IssueMessage = issue.Message,
            TotalSizeBytes = totalSizeBytes,
            FreeSpaceBytes = freeSpaceBytes,
            VisualKind = ResolveDriveVisualKind(
                driveType,
                isSystemDrive,
                isBitLockerVolume,
                isBitLockerLocked,
                bitLockerVisualState,
                issue.IssueKind)
        };
    }

    private static DriveIssueInfo ClassifyDriveIssue(
        DriveType driveType,
        bool isReady,
        bool isBitLockerLocked,
        string root)
    {
        if (isReady)
            return DriveIssueInfo.None;

        if (driveType == DriveType.CDRom)
            return new DriveIssueInfo(DriveIssueKind.OpticalNoMedia, null, null);

        if (isBitLockerLocked)
            return new DriveIssueInfo(DriveIssueKind.BitLockerLocked, null, null);

        if (driveType is DriveType.Fixed or DriveType.Removable)
        {
            DriveIssueInfo issue = ProbeDriveRootIssue(root);

            if (driveType == DriveType.Removable && issue.IssueKind == DriveIssueKind.NotReady)
            {
                return issue with
                {
                    IssueKind = DriveIssueKind.RemovableNoMediaOrUnavailable
                };
            }

            return issue;
        }

        return new DriveIssueInfo(DriveIssueKind.Unknown, null, null);
    }

    private static DriveIssueInfo ProbeDriveRootIssue(string rootPath)
    {
        try
        {
            using IEnumerator<string> enumerator =
                Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator();

            _ = enumerator.MoveNext();
            return DriveIssueInfo.None;
        }
        catch (UnauthorizedAccessException ex)
        {
            return DriveIssueInfo.FromException(DriveIssueKind.AccessDenied, ex);
        }
        catch (IOException ex)
        {
            return DriveIssueInfo.FromIOException(ex);
        }
        catch (Exception ex)
        {
            return DriveIssueInfo.FromException(DriveIssueKind.Unknown, ex);
        }
    }

    private static bool IsBitLockerLockedAccessMessage(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
               message.Contains("BitLocker", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("locked", StringComparison.OrdinalIgnoreCase);
    }

    private static DriveVisualKind ResolveDriveVisualKind(
        DriveType driveType,
        bool isSystemDrive,
        bool isBitLockerVolume,
        bool isBitLockerLocked,
        BitLockerVisualState bitLockerVisualState = BitLockerVisualState.None,
        DriveIssueKind issueKind = DriveIssueKind.None)
    {
        if (driveType == DriveType.CDRom)
            return DriveVisualKind.Optical;

        if (isBitLockerLocked || bitLockerVisualState == BitLockerVisualState.Locked || issueKind == DriveIssueKind.BitLockerLocked)
            return DriveVisualKind.BitLockerLocked;

        if (bitLockerVisualState == BitLockerVisualState.Unknown)
            return DriveVisualKind.BitLockerStatusUnknown;

        if (bitLockerVisualState == BitLockerVisualState.ProtectionOff)
        {
            if (isSystemDrive)
                return DriveVisualKind.SystemBitLockerProtectionOff;

            return DriveVisualKind.BitLockerProtectionOff;
        }

        if (isBitLockerVolume || bitLockerVisualState == BitLockerVisualState.Unlocked)
        {
            if (isSystemDrive)
                return DriveVisualKind.SystemBitLockerUnlocked;

            return DriveVisualKind.BitLockerUnlocked;
        }

        if (driveType == DriveType.Network)
            return DriveVisualKind.Network;

        if (driveType == DriveType.Removable)
            return DriveVisualKind.Removable;

        if (isSystemDrive)
            return DriveVisualKind.System;

        return DriveVisualKind.Fixed;
    }

    public static string NormalizeDriveRoot(string path)
    {
        string root = Path.GetPathRoot(path) ?? path;
        return root.TrimEnd('\\') + "\\";
    }

    public static bool IsSystemDrive(string driveRoot)
    {
        return DriveSystemDetector.IsSystemVisualDrive(driveRoot);
    }

    public static string BuildDriveDisplayName(DriveInfo drive)
    {
        string root;

        try
        {
            root = NormalizeDriveRoot(drive.Name).TrimEnd('\\');
        }
        catch
        {
            return string.Empty;
        }

        bool isReady = false;
        string? volumeLabel = null;

        try
        {
            isReady = drive.IsReady;
            if (isReady)
                volumeLabel = drive.VolumeLabel;
        }
        catch
        {
        }

        return BuildDriveDisplayName(root, volumeLabel, isReady);
    }

    private static string BuildDriveDisplayName(string driveRoot, string? volumeLabel, bool isReady)
    {
        string root = driveRoot.TrimEnd('\\');

        if (isReady && !string.IsNullOrWhiteSpace(volumeLabel))
            return $"{volumeLabel} ({root})";

        return root;
    }

    private void QueueBitLockerRefresh(string? driveRoot)
    {
        if (!_bitLockerCapabilities.CanReadStatus)
            return;

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                _pendingBitLockerRefreshAll = true;
                _pendingBitLockerRefreshRoots.Clear();
            }
            else if (!_pendingBitLockerRefreshAll)
            {
                _pendingBitLockerRefreshRoots.Add(NormalizeDriveRoot(driveRoot));
            }

            if (_bitLockerRefreshQueuedOrRunning)
                return;

            _bitLockerRefreshQueuedOrRunning = true;
        }

        _ = Task.Run(ProcessBitLockerRefreshQueue);
    }

    private void ProcessBitLockerRefreshQueue()
    {
        while (true)
        {
            bool refreshAll;
            string[] driveRoots;

            lock (_sync)
            {
                refreshAll = _pendingBitLockerRefreshAll;
                driveRoots = _pendingBitLockerRefreshRoots.ToArray();

                _pendingBitLockerRefreshAll = false;
                _pendingBitLockerRefreshRoots.Clear();

                if (!refreshAll && driveRoots.Length == 0)
                {
                    _bitLockerRefreshQueuedOrRunning = false;
                    return;
                }
            }

            if (refreshAll)
                RefreshAllBitLockerStatusesFromWmi();
            else
                RefreshBitLockerStatusesFromWmi(driveRoots);
        }
    }

    private void RefreshAllBitLockerStatusesFromWmi()
    {
        if (!TryQueryBitLockerStatuses(out Dictionary<string, BitLockerDriveStatus> newStatuses))
            return;

        string[] affectedRoots = Array.Empty<string>();
        string[] rootsToRebuild = Array.Empty<string>();

        lock (_sync)
        {
            if (!BitLockerMapsEqual(_bitLockerStatusByRoot, newStatuses))
            {
                affectedRoots = GetChangedBitLockerRoots(_bitLockerStatusByRoot, newStatuses);
                _bitLockerStatusByRoot = newStatuses;
                rootsToRebuild = _drivesByRoot.Keys.ToArray();
            }
        }

        if (affectedRoots.Length == 0 && rootsToRebuild.Length == 0)
            return;

        Dictionary<string, DriveSnapshot> rebuiltSnapshots =
            BuildDriveSnapshotsForBitLockerUpdate(rootsToRebuild);

        HashSet<string> affected = new(affectedRoots, StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            foreach (string root in rootsToRebuild)
            {
                if (ApplyBitLockerStatusToDriveSnapshotNoLock(root, rebuiltSnapshots))
                    affected.Add(root);
            }

            affectedRoots = affected.ToArray();
        }

        if (affectedRoots.Length > 0)
            OnDriveStatesChanged(affectedRoots);
    }

    private void RefreshBitLockerStatusesFromWmi(string[] driveRoots)
    {
        string[] roots = [.. driveRoots
        .Where(static root => !string.IsNullOrWhiteSpace(root))
        .Select(NormalizeDriveRoot)
        .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (roots.Length == 0)
            return;

        Dictionary<string, BitLockerDriveStatus> refreshedStatuses =
        new(StringComparer.OrdinalIgnoreCase);

        List<string> queriedRoots = [];

        foreach (string root in roots)
        {
            if (!TryQueryBitLockerStatus(root, out var status))
                continue;

            queriedRoots.Add(root);

            if (status.HasVisualState)
                refreshedStatuses[root] = status;
        }

        if (queriedRoots.Count == 0)
            return;

        List<string> rootsToRebuild = [];

        lock (_sync)
        {
            foreach (string root in queriedRoots)
            {
                bool hadCurrent = _bitLockerStatusByRoot.TryGetValue(root, out var currentStatus);
                bool hasUpdated = refreshedStatuses.TryGetValue(root, out var updatedStatus);

                if (hasUpdated)
                    _bitLockerStatusByRoot[root] = updatedStatus;
                else
                    _bitLockerStatusByRoot.Remove(root);

                if (hadCurrent == hasUpdated &&
                    (!hasUpdated || currentStatus == updatedStatus))
                {
                    continue;
                }

                rootsToRebuild.Add(root);
            }
        }

        if (rootsToRebuild.Count == 0)
            return;

        Dictionary<string, DriveSnapshot> rebuiltSnapshots =
        BuildDriveSnapshotsForBitLockerUpdate(rootsToRebuild);

        List<string> affectedRoots = [];

        lock (_sync)
        {
            foreach (string root in rootsToRebuild)
            {
                if (ApplyBitLockerStatusToDriveSnapshotNoLock(root, rebuiltSnapshots))
                    affectedRoots.Add(root);
            }
        }

        if (affectedRoots.Count > 0)
            OnDriveStatesChanged(affectedRoots.ToArray());
    }

    private Dictionary<string, DriveSnapshot> BuildDriveSnapshotsForBitLockerUpdate(IEnumerable<string> roots)
    {
        Dictionary<string, DriveSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

        foreach (string root in roots)
        {
            try
            {
                snapshots[root] = BuildDriveSnapshot(new DriveInfo(root), root);
            }
            catch
            {
            }
        }

        return snapshots;
    }

    private bool ApplyBitLockerStatusToDriveSnapshotNoLock(
        string root,
        IReadOnlyDictionary<string, DriveSnapshot> rebuiltSnapshots)
    {
        if (!_drivesByRoot.TryGetValue(root, out DriveSnapshot? current))
            return false;

        bool isBitLockerVolume = false;
        bool isBitLockerLocked = false;
        BitLockerVisualState bitLockerVisualState = BitLockerVisualState.None;

        if (_bitLockerStatusByRoot.TryGetValue(root, out var bitLocker))
        {
            isBitLockerVolume = bitLocker.IsBitLockerVolume;
            isBitLockerLocked = bitLocker.IsLocked;
            bitLockerVisualState = bitLocker.VisualState;
        }

        DriveSnapshot updated;

        if (rebuiltSnapshots.TryGetValue(root, out DriveSnapshot? rebuilt))
        {
            updated = rebuilt;
        }
        else
        {
            bool isSystemDrive = IsSystemDrive(root);

            updated = current with
            {
                IsSystemDrive = isSystemDrive,
                IsBitLockerProtected = isBitLockerVolume,
                IsBitLockerLocked = isBitLockerLocked,
                VisualKind = ResolveDriveVisualKind(
                    current.DriveType,
                    isSystemDrive,
                    isBitLockerVolume,
                    isBitLockerLocked,
                    bitLockerVisualState,
                    current.IssueKind)
            };
        }

        if (updated == current)
            return false;

        _drivesByRoot[root] = updated;
        return true;
    }

    private static string[] GetChangedBitLockerRoots(
    IReadOnlyDictionary<string, BitLockerDriveStatus> currentStatuses,
    IReadOnlyDictionary<string, BitLockerDriveStatus> newStatuses)
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);

        foreach (string root in currentStatuses.Keys)
            candidates.Add(root);

        foreach (string root in newStatuses.Keys)
            candidates.Add(root);

        List<string> changed = [];

        foreach (string root in candidates)
        {
            bool hadCurrent = currentStatuses.TryGetValue(root, out var current);
            bool hasNew = newStatuses.TryGetValue(root, out var updated);

            if (hadCurrent != hasNew)
            {
                changed.Add(root);
                continue;
            }

            if (!hadCurrent)
                continue;

            if (current.IsBitLockerVolume != updated.IsBitLockerVolume ||
                current.IsLocked != updated.IsLocked ||
                current.VisualState != updated.VisualState)
            {
                changed.Add(root);
            }
        }

        return changed.ToArray();
    }

    private static bool BitLockerMapsEqual(
        Dictionary<string, BitLockerDriveStatus> left,
        Dictionary<string, BitLockerDriveStatus> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach ((string key, var value) in left)
        {
            if (!right.TryGetValue(key, out var other))
                return false;

            if (value.IsBitLockerVolume != other.IsBitLockerVolume ||
                value.IsLocked != other.IsLocked ||
                value.VisualState != other.VisualState)
            {
                return false;
            }
        }

        return true;
    }

    private void OnDriveStatesChanged(params string[] affectedDriveRoots)
    {
        DriveStatesChanged?.Invoke(this, new DriveStatesChangedEventArgs(affectedDriveRoots));
    }

    private static bool TryQueryBitLockerStatuses(
    out Dictionary<string, BitLockerDriveStatus> result)
    {
        result = new Dictionary<string, BitLockerDriveStatus>(StringComparer.OrdinalIgnoreCase);

        try
        {
            ConnectionOptions options = new()
            {
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy
            };

            ManagementScope scope = new(
                @"\\.\Root\CIMV2\Security\MicrosoftVolumeEncryption",
                options);

            scope.Connect();

            ObjectQuery query = new(
                "SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter IS NOT NULL");

            using ManagementObjectSearcher searcher = new(scope, query);
            using ManagementObjectCollection volumes = searcher.Get();

            foreach (ManagementObject volume in volumes.Cast<ManagementObject>())
            {
                using (volume)
                {
                    try
                    {
                        string? driveLetter = volume["DriveLetter"] as string;
                        if (string.IsNullOrWhiteSpace(driveLetter))
                            continue;

                        string root = NormalizeDriveRoot(driveLetter);
                        var status = ReadBitLockerVolumeStatus(volume);

                        if (status.HasVisualState)
                            result[root] = status;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryQueryBitLockerStatus(
    string driveRoot,
    out BitLockerDriveStatus status)
    {
        status = default;

        try
        {
            string normalizedRoot = NormalizeDriveRoot(driveRoot);
            string driveLetter = normalizedRoot.TrimEnd('\\').Replace("'", "''");

            ConnectionOptions options = new()
            {
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy
            };

            ManagementScope scope = new(
                @"\\.\Root\CIMV2\Security\MicrosoftVolumeEncryption",
                options);

            scope.Connect();

            ObjectQuery query = new(
                $"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = '{driveLetter}'");

            using ManagementObjectSearcher searcher = new(scope, query);
            using ManagementObjectCollection volumes = searcher.Get();

            foreach (ManagementObject volume in volumes.Cast<ManagementObject>())
            {
                using (volume)
                {
                    status = ReadBitLockerVolumeStatus(volume);
                    return true;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static BitLockerDriveStatus ReadBitLockerVolumeStatus(
        ManagementObject volume)
    {
        uint? protectionStatus = BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetProtectionStatus",
            "ProtectionStatus");

        uint? lockStatus = BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetLockStatus",
            "LockStatus");

        uint? encryptionMethod = BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetEncryptionMethod",
            "EncryptionMethod");

        BitLockerResolvedState resolvedState = BitLockerVolumeStateResolver.Resolve(new BitLockerStateInput(
            ToLockState(lockStatus),
            ToEncryptionState(encryptionMethod),
            ToProtectionState(protectionStatus)));

        return new BitLockerDriveStatus(
            resolvedState.IsStatusKnown,
            resolvedState.IsBitLockerVolume,
            resolvedState.IsLocked,
            resolvedState.VisualState);
    }

    private static BitLockerLockState ToLockState(uint? lockStatus)
    {
        return lockStatus switch
        {
            1 => BitLockerLockState.Locked,
            0 => BitLockerLockState.Unlocked,
            _ => BitLockerLockState.Unknown
        };
    }

    private static BitLockerProtectionState ToProtectionState(uint? protectionStatus)
    {
        return protectionStatus switch
        {
            1 => BitLockerProtectionState.On,
            0 => BitLockerProtectionState.Off,
            _ => BitLockerProtectionState.Unknown
        };
    }

    private static BitLockerEncryptionState ToEncryptionState(uint? encryptionMethod)
    {
        return encryptionMethod switch
        {
            null => BitLockerEncryptionState.Unknown,
            0 => BitLockerEncryptionState.NotEncrypted,
            uint.MaxValue => BitLockerEncryptionState.Unknown,
            _ => BitLockerEncryptionState.Encrypted
        };
    }
}
