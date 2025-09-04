using System;
using System.Collections.Generic;

namespace VideoNest.Models {
    public class VideoDB {
        public int Id { get; set; }
        public string? Title { get; set; } // Anulável
        public string? Description { get; set; } // Anulável
        public int Duration { get; set; }
        public string? FilePath { get; set; } // Anulável
        public string? Status { get; set; } // Anulável
        public DateTime? CreatedAt { get; set; } // Já anulável
        public List<QRCodeResult> QRCodes { get; set; } = new List<QRCodeResult>();
    }

    public class QRCodeResult {
        public int Id { get; set; }
        public int VideoId { get; set; }
        public string? Content { get; set; } // Anulável
        public int Timestamp { get; set; }
        public VideoDB? Video { get; set; } // Anulável
    }
}