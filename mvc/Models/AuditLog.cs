using System.ComponentModel.DataAnnotations;

namespace mvc.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime TimestampUtc { get; set; }

        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? UserName { get; set; }

        [MaxLength(128)]
        public string Controller { get; set; } = string.Empty;

        [MaxLength(128)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(16)]
        public string HttpMethod { get; set; } = string.Empty;

        public string? RouteValues { get; set; }

        [MaxLength(64)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        public bool Success { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }
    }
}