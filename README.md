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


## CI/CD com GitHub Actions e IIS

O projeto possui uma esteira de CI/CD configurada com GitHub Actions para automatizar o processo de build, testes, publicação e deploy da aplicação no IIS local.

A pipeline é executada automaticamente em pushes e pull requests para a branch `main`, além de permitir execução manual pelo `workflow_dispatch`.

### Etapas de CI

A etapa de CI é executada em um runner Linux hospedado pelo GitHub (`ubuntu-latest`) e realiza as seguintes validações:

1. Checkout do código-fonte.
2. Instalação do SDK do .NET 8.
3. Restauração dos pacotes NuGet da API.
4. Restauração dos pacotes NuGet do projeto de testes.
5. Build da API em modo `Release`.
6. Build do projeto de testes em modo `Release`.
7. Execução dos testes automatizados.
8. Publicação dos resultados dos testes em formato `.trx`.
9. Publicação da API em uma pasta de saída.
10. Upload do artefato publicado para ser utilizado na etapa de deploy.

Essa etapa garante que somente uma versão compilável e validada pelos testes avance para o processo de entrega.

### Etapas de CD

A etapa de CD é executada somente na branch `main`, após a conclusão bem-sucedida do CI.

O deploy é realizado em um runner self-hosted Windows, configurado com acesso ao IIS local. O processo executa as seguintes ações:

1. Download do artefato publicado pela etapa de CI.
2. Parada do site `VideoNest` no IIS.
3. Parada do Application Pool `VideoNestPool`.
4. Limpeza da pasta de deploy em `C:\deploy\videonest`.
5. Cópia dos arquivos publicados para a pasta final da aplicação.
6. Criação da pasta de logs, caso ainda não exista.
7. Configuração automática do `web.config`.
8. Definição do ambiente da aplicação como `Demo`.
9. Concessão de permissões ao Application Pool sobre a pasta da aplicação.
10. Inicialização do Application Pool e do site no IIS.
11. Execução de health check nas URLs configuradas.
12. Coleta de diagnósticos e logs em caso de falha.

### Configuração de ambiente

Durante o deploy, o arquivo `web.config` é ajustado automaticamente para configurar a variável de ambiente:

```xml
<environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Demo" />