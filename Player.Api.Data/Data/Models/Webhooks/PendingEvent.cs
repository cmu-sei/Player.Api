using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models.Webhooks
{
    public class PendingEventEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public EventType EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid EffectedEntityId { get; set; }
    }
}