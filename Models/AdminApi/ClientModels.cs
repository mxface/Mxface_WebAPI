namespace MxfaceWebAPI.Models.AdminApi
{
    public sealed class AdminClientGroup
    {
        /// <summary>Null for a new group. An existing numeric id updates that group (name is immutable).</summary>
        public int? Id { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Body for POST /admin/clients (create). Also usable for a full PUT /admin/clients/{id}
    /// (e.g. renaming the org alongside other fields) — but for groups-only changes, prefer
    /// <see cref="ClientGroupsPatchRequest"/>/<c>UpdateClientGroupsAsync</c> instead: since
    /// 02-Sep-2026 PUT is merge-patch (a key not sent is left alone), so there's no need to
    /// resend every field just to touch groups.
    /// </summary>
    public sealed class AdminCreateClientRequest
    {
        public string OrganizationName { get; set; } = string.Empty;

        /// <summary>Exactly 6 alphanumerics. Only meaningful on create — ignored/immutable on update.</summary>
        public string ClientId { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        /// <summary>0-3. faceCount+fingerCount+irisCount must not all be zero.</summary>
        public int FaceCount { get; set; }

        /// <summary>0-10.</summary>
        public int FingerCount { get; set; }

        /// <summary>0-2.</summary>
        public int IrisCount { get; set; }

        /// <summary>Minimum 1.</summary>
        public int MaxEnrollmentLimit { get; set; }

        /// <summary>At least one entry, at least one with IsDefault = true. Full-replace semantics.</summary>
        public List<AdminClientGroup> Groups { get; set; } = new();

        /// <summary>Digits only if sent (no '+', spaces, or dashes).</summary>
        public string? OrganizationContact { get; set; }

        /// <summary>Digits only if sent.</summary>
        public string? TechnicalContact { get; set; }

        public string? TechnicalPersonEmail { get; set; }
        public string? TechnicalPersonName { get; set; }

        /// <summary>Omit or blank for UTC. A non-blank invalid value is a 400, not a silent fallback.</summary>
        public string? Timezone { get; set; }

        public string? Country { get; set; }
        public string? OrganizationAddress { get; set; }

        public bool? AllowUnknownPositionMatch { get; set; }
        public bool? AllowUnknownPositionIdentify { get; set; }
        public bool? AllowUnknownPositionAuth { get; set; }
    }

    /// <summary>Partial update of one existing group within a client's groups delta (see
    /// <see cref="GroupsDelta"/>) — only <see cref="Id"/> is required; omitted
    /// <see cref="IsDefault"/>/<see cref="Description"/> are left unchanged server-side
    /// (merge-patch semantics since 02-Sep-2026). GroupName is immutable and not included here.</summary>
    public sealed class AdminClientGroupUpdate
    {
        public int Id { get; set; }
        public bool? IsDefault { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>The merge-patch "groups" delta accepted by PUT /admin/clients/{id} since
    /// 02-Sep-2026 — add/update/remove only the groups named here; every other group, and every
    /// other client field, is left untouched.</summary>
    public sealed class GroupsDelta
    {
        public List<AdminClientGroup>? Add { get; set; }
        public List<AdminClientGroupUpdate>? Update { get; set; }
        public List<int>? Remove { get; set; }
    }

    /// <summary>Body for PUT /admin/clients/{id} when only touching groups — merge-patch means
    /// no other client field needs to be (or should be) included.</summary>
    public sealed class ClientGroupsPatchRequest
    {
        public GroupsDelta Groups { get; set; } = new();
    }

    public sealed class AdminClientResponse
    {
        /// <summary>The numeric database id — required by every later step (groups, roles' clientId is text, users' key API).</summary>
        public int Id { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public List<AdminClientGroup> Groups { get; set; } = new();
        public DateTimeOffset? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
