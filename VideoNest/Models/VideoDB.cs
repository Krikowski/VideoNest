namespace VideoNest.Models {
    public class VideoDB {
        public int Id { get; set; } // Chave primária
        public string Title { get; set; }
        public string Description { get; set; }
        public int Duration { get; set; }
        public DateTime CreatedAt { get; set; } // Exemplo de campo adicional
    }
}