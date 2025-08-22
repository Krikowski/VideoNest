using Microsoft.AspNetCore.Mvc;
using VideoNest.Service;

namespace VideoNest.Controllers {
    [ApiController]
    [Route("API/[controller]")]
    public class TesteEnviaParaFilaRabbitMQ : Controller {

        [HttpGet("teste")]
        public string teste() {
            try {
                Producer.SendMessage("Olá, RabbitMQ!");
                return "Publicação na fila feito com sucesso;";
            } catch (Exception ex) {
                return ex.Message;
            }
        }

    }
}
