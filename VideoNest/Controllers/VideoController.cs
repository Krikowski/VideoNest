using Microsoft.AspNetCore.Mvc;
using VideoNest.DTO;
using VideoNest.Repositories;
using VideoNest.Models;
using VideoNest.Services;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace VideoNest.Controllers {
    [ApiController]
    [Route("API/[controller]")]

    public class VideoController : Controller {

        //injeção de dependencia
        private readonly IVideoService _videoService;

        public VideoController(IVideoService videoService) {
            _videoService = videoService;
        }

        /// <summary>
        /// Faz o upload de um vídeo, salvando os metadados no banco de dados.
        /// </summary>
        /// <param name="request">Os metadados do vídeo a ser enviado.</param>
        /// <returns>Confirmação de sucesso ou erro.</returns>
        [HttpPost("VideoUpload")]
        public async Task<IActionResult> UploadVideo([FromBody] VideoUploadRequest request) {
            try {
                var videoId = await _videoService.CreateVideoAsync(request);

                return Ok(new { Message = "Vídeo salvo com sucesso.", VideoId = videoId });
            } catch (Exception ex) {
                return StatusCode(500, new { Message = "Erro interno no servidor.", Details = ex.Message });
            }
        }

        /// <summary>
        /// Busca um vídeo pelo ID.
        /// </summary>
        /// <param name="id">ID do vídeo.</param>
        /// <returns>Os dados do vídeo, ou erro se não encontrado.</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetVideo(int id) {
            try {
                var video = await _videoService.BuscaVideoPorIDService(id);
                return Ok(video);
            } catch (KeyNotFoundException ex) {
                return NotFound(new { Message = ex.Message });
            } catch (Exception ex) {
                //_logger.LogError(ex, "Erro ao buscar vídeo com ID {Id}", id);
                return StatusCode(500, new { Message = "Erro interno no servidor." });
            }
        }
    }
}

