namespace HockeyIndex.Api.Domain;

public enum EventType
{
    Scrimmage,
    League,
    Tournament,
}

/// <summary>Exactly the five AC-LC-2 states.</summary>
public enum EventStatus
{
    Draft,
    Published,
    Cancelled,
    Hidden,
    Archived,
}

public enum HiddenReason
{
    Admin,
    Reports,
    LinkScan,
    DomainBlocklist,
    HostBanned,
}

/// <summary>Dashboard notice left by <c>PendingScanJob</c> when it ends a parked request without applying it.</summary>
public enum EventNotice
{
    PublishBlocked,
    PublishLimitReached,
    PublishScanExpired,
    JoinInstructionsBlocked,
}
