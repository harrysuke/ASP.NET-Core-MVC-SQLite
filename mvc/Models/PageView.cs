using System.ComponentModel.DataAnnotations;

namespace mvc.Models
{
    public class PageView
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

        [MaxLength(64)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }
    }
}