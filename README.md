# VideoNest

API para upload, gerenciamento e processamento assíncrono de vídeos com foco em análise de QR Codes.

Projeto desenvolvido no contexto do Hackathon FIAP 7NETT.

## 🚀 Objetivo

Permitir:

Upload de vídeos com validação
Processamento assíncrono via fila
Consulta de status e resultados
Escalabilidade baseada em microsserviços

## 🏗️ Arquitetura
.NET 8 (Web API)
RabbitMQ (mensageria)
MongoDB (persistência)
Redis (cache)
SignalR (tempo real)
Docker (containerização)

## Fluxo:

Upload → Validação → Armazenamento → RabbitMQ → Processamento → Consulta

## 📡 Endpoints
Método	Rota	Descrição
`POST`	/api/videos	Upload de vídeo
`GET`	/api/videos/{id}/status	Status do processamento
`GET`	/health	Healthcheck da aplicação

## Swagger:

http://localhost:5001/swagger
# ⚙️ Como rodar localmente

1. Subir infraestrutura
`docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 -e RABBITMQ_DEFAULT_USER=admin -e RABBITMQ_DEFAULT_PASS=admin rabbitmq:3-management`

Opcional:

`docker run -d --name mongodb -p 27017:27017 mongo`
`docker run -d --name redis -p 6379:6379 redis`

2. Rodar API
`dotnet run`
3. Executar testes
`dotnet test`

## 🧪 Testes

Cobertura:

Controllers
DTOs
Serviços
RabbitMQ
Repositórios

## Resultado atual:

24 testes
0 falhas
🔄 CI/CD (Fase 2)

# Pipeline implementado com GitHub Actions:

CI
Restore
Build
Test
Geração de artefato
CD
Build de imagem Docker
Publicação no GitHub Container Registry (GHCR)
📦 Artifact gerado

A pipeline gera:

videonest-api-{run_number}

### Contendo:

DLL da aplicação
dependências
pronto para deploy

### 🐳 Docker
Build local
`docker build -t videonest .`
Run
`docker run -p 5001:8080 videonest`

### 🧠 Decisões técnicas
RabbitMQ → desacoplamento e resiliência
MongoDB → flexibilidade de dados
Redis → performance de leitura
SignalR → atualização em tempo real
Docker → portabilidade

### 📈 Diferenciais
Processamento assíncrono
Dead Letter Queue configurada
Retry automático
Cache com expiração
Logs estruturados
Pipeline completa CI/CD

### 📊 Healthcheck
`GET /health`

Retorna status da aplicação.
