using System.ComponentModel.DataAnnotations;

namespace VideoNest.DTO
{
    public class VideoUploadRequest
    {
        /// <summary>
        /// Título do vídeo. Deve ser fornecido e ter entre 3 e 100 caracteres.
        /// </summary>
        [Required(ErrorMessage = "O título é obrigatório.")]
        [StringLength(100, ErrorMessage = "O título deve até 100 caracteres.")]
        [RegularExpression("^[a-zA-Z0-9 ]*$", ErrorMessage = "O título só pode conter letras, números e espaços.")]
        public string Title { get; set; }
        [Required(ErrorMessage = "A descrição é obrigatório.")]
        public string Description { get; set; }
        public int Duration { get; set; }
    }
}
