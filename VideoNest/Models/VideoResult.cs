using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace VideoNest.Models {
    public class VideoResult {
        [BsonId]
        [BsonRepresentation(BsonType.Int32)]
        public int VideoId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? FilePath { get; set; }

        public string Status { get; set; } = "Na Fila";

        public DateTime? CreatedAt { get; set; }

        public int Duration { get; set; }

        public string? ErrorMessage { get; set; }

        public List<QRCodeResult> QRCodes { get; set; } = new List<QRCodeResult>();
    }

    public class QRCodeResult {
        public string? Content { get; set; }
        public int Timestamp { get; set; }
    }

    public class VideoCounter  // Evita conflito com Prometheus.Counter
    {
        [BsonId]
        public string Id { get; set; } = null!;
        public int Sequence { get; set; }
    }
}