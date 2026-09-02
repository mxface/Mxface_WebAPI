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
    /// Body for both POST /admin/clients (create) and PUT /admin/clients/{id} (update / sync groups —
    /// there is no separate group API, so updates must resend every client field plus the full desired
    /// groups array; anything omitted from <see cref="Groups"/> gets deleted).
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
