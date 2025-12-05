# Docker Deployment Guide

Complete guide for running **mostlylucid.mockllmapi** with Docker and Docker Compose, including end-to-end examples with Ollama.

## Table of Contents

- [Quick Start](#quick-start)
- [Prerequisites](#prerequisites)
- [Running with Docker Compose](#running-with-docker-compose)
- [Configuration Methods](#configuration-methods)
- [Building the Image](#building-the-image)
- [Running Standalone](#running-standalone)
- [Advanced Configurations](#advanced-configurations)
- [Troubleshooting](#troubleshooting)

---

## Quick Start

The fastest way to get started with everything configured:

```bash
# Clone or navigate to the repository
cd LLMApi

# Start everything (LLMApi + Ollama with llama3)
docker compose up -d

# Wait for Ollama to download llama3 model (first run only, ~4.7GB)
docker compose logs -f ollama

# Once ready, test the API
curl http://localhost:5116/api/mock/users?shape={"id":0,"name":"","email":""}
```

That's it! The API is now running at `http://localhost:5116` with Ollama backend.

---

## Prerequisites

- **Docker Desktop** or **Docker Engine** (20.10+)
- **Docker Compose** (v2.0+)
- **8GB+ RAM recommended** (for running Ollama with llama3)
- **10GB+ disk space** (for Ollama models)

### System Requirements by Model

| Model | RAM Required | Disk Space | Best For |
|-------|--------------|------------|----------|
| **llama3** (8B) | 8GB | 4.7GB | Default, balanced |
| **gemma3:4b** | 4GB | 2.5GB | Low-resource systems |
| **mistral-nemo** | 16GB | 7.1GB | High quality |

---

## Running with Docker Compose

### 1. End-to-End Setup (Recommended)

The `docker-compose.yml` includes both LLMApi and Ollama services pre-configured:

**File: `docker-compose.yml`**
```yaml
version: '3.8'

services:
  ollama:
    image: ollama/ollama:latest
    container_name: ollama
    ports:
      - "11434:11434"
    volumes:
      - ollama_data:/root/.ollama
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:11434/"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    networks:
      - llmapi-network
    restart: unless-stopped

  llmapi:
    build:
      context: .
      dockerfile: LLMApi/Dockerfile
    container_name: llmapi
    ports:
      - "5116:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - MockLlmApi__BaseUrl=http://ollama:11434/v1/
      - MockLlmApi__ModelName=llama3
      - MockLlmApi__Temperature=1.2
    depends_on:
      ollama:
        condition: service_healthy
    networks:
      - llmapi-network
    restart: unless-stopped

volumes:
  ollama_data:

networks:
  llmapi-network:
    driver: bridge
```

**Start the services:**

```bash
# Start in detached mode
docker compose up -d

# View logs
docker compose logs -f

# View logs for specific service
docker compose logs -f llmapi
docker compose logs -f ollama

# Stop everything
docker compose down

# Stop and remove volumes (clears downloaded models)
docker compose down -v
```

### 2. First Run - Downloading Models

On first run, Ollama will automatically download the llama3 model:

```bash
# Start and watch the download progress
docker compose up

# You'll see output like:
# ollama  | pulling manifest
# ollama  | pulling 6a0746a1ec1a... 100% ▕████████████▏ 4.7 GB
# ollama  | pulling 4fa551d4f938... 100% ▕████████████▏  12 KB
# ollama  | verifying sha256 digest
# ollama  | writing manifest
# ollama  | success
```

**Download times:**
- Fast connection (100Mbps): ~6 minutes
- Medium connection (50Mbps): ~12 minutes
- Slow connection (25Mbps): ~25 minutes

### 3. Using a Different Model

To use a different model like `gemma3:4b`:

```bash
# Method 1: Environment variable
docker compose down
docker compose up -d -e MockLlmApi__ModelName=gemma3:4b

# Method 2: Edit docker-compose.yml
# Change: ModelName=llama3 → ModelName=gemma3:4b
docker compose up -d

# Method 3: Use .env file (see Configuration Methods)
```

---

## Configuration Methods

There are three ways to configure the application in Docker:

### Method 1: Environment Variables (Simplest)

**Using docker-compose.yml:**

```yaml
services:
  llmapi:
    environment:
      - MockLlmApi__BaseUrl=http://ollama:11434/v1/
      - MockLlmApi__ModelName=llama3
      - MockLlmApi__Temperature=1.5
      - MockLlmApi__EnableVerboseLogging=true
      - MockLlmApi__MaxContextWindow=8192
```

**Using .env file:**

```bash
# Create .env file (copy from .env.example)
cp .env.example .env

# Edit .env
nano .env
```

**File: `.env`**
```bash
MockLlmApi__BaseUrl=http://ollama:11434/v1/
MockLlmApi__ModelName=llama3
MockLlmApi__Temperature=1.2
MockLlmApi__EnableVerboseLogging=true
```

**Start with .env:**
```bash
docker compose --env-file .env up -d
```

### Method 2: Volume-Mapped appsettings.json (Most Flexible)

**File: `docker-appsettings.json`**
```json
{
  "MockLlmApi": {
    "BaseUrl": "http://ollama:11434/v1/",
    "ModelName": "llama3",
    "Temperature": 1.2,
    "MaxContextWindow": 8192,
    "EnableVerboseLogging": false,
    "LlmBackends": [
      {
        "Name": "local-ollama",
        "Provider": "ollama",
        "BaseUrl": "http://ollama:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true
      },
      {
        "Name": "gemma-fast",
        "Provider": "ollama",
        "BaseUrl": "http://ollama:11434/v1/",
        "ModelName": "gemma3:4b",
        "Enabled": true
      }
    ]
  }
}
```

**File: `docker-compose.override.yml`**
```yaml
version: '3.8'

services:
  llmapi:
    volumes:
      - ./docker-appsettings.json:/app/appsettings.json:ro
```

**Start with volume mapping:**
```bash
docker compose up -d

# Changes to docker-appsettings.json require restart
docker compose restart llmapi
```

### Method 3: Build-Time Configuration

Embed configuration during build:

**Custom Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["LLMApi/LLMApi.csproj", "LLMApi/"]
COPY ["mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj", "mostlylucid.mockllmapi/"]
RUN dotnet restore "LLMApi/LLMApi.csproj"
COPY . .

# Copy custom appsettings
COPY ["custom-appsettings.json", "LLMApi/appsettings.json"]

WORKDIR "/src/LLMApi"
RUN dotnet publish "./LLMApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LLMApi.dll"]
```

---

## Building the Image

### Build LLMApi Image Only

```bash
# Build from solution root
docker build -f LLMApi/Dockerfile -t llmapi:latest .

# Build with specific tag
docker build -f LLMApi/Dockerfile -t llmapi:v2.1.0 .

# Build with build args
docker build -f LLMApi/Dockerfile \
  --build-arg BUILD_CONFIGURATION=Release \
  -t llmapi:latest .
```

### Build with Docker Compose

```bash
# Build only
docker compose build

# Build with no cache (clean build)
docker compose build --no-cache

# Build and start
docker compose up --build -d
```

---

## Running Standalone

### Option 1: LLMApi Container with External Ollama

If you already have Ollama running on your host:

```bash
# Run LLMApi container pointing to host Ollama
docker run -d \
  --name llmapi \
  -p 5116:8080 \
  -e MockLlmApi__BaseUrl=http://host.docker.internal:11434/v1/ \
  -e MockLlmApi__ModelName=llama3 \
  llmapi:latest

# On Linux, use --add-host instead
docker run -d \
  --name llmapi \
  -p 5116:8080 \
  --add-host=host.docker.internal:host-gateway \
  -e MockLlmApi__BaseUrl=http://host.docker.internal:11434/v1/ \
  -e MockLlmApi__ModelName=llama3 \
  llmapi:latest
```

### Option 2: Run Ollama Container Separately

```bash
# Start Ollama
docker run -d \
  --name ollama \
  -p 11434:11434 \
  -v ollama_data:/root/.ollama \
  ollama/ollama:latest

# Pull llama3 model
docker exec ollama ollama pull llama3

# Start LLMApi
docker run -d \
  --name llmapi \
  -p 5116:8080 \
  --link ollama:ollama \
  -e MockLlmApi__BaseUrl=http://ollama:11434/v1/ \
  -e MockLlmApi__ModelName=llama3 \
  llmapi:latest
```

### Option 3: Minimal Run (No Ollama)

Run LLMApi pointing to an external LLM service:

```bash
# Point to OpenAI
docker run -d \
  --name llmapi \
  -p 5116:8080 \
  -e MockLlmApi__LlmBackends__0__Provider=openai \
  -e MockLlmApi__LlmBackends__0__BaseUrl=https://api.openai.com/v1/ \
  -e MockLlmApi__LlmBackends__0__ModelName=gpt-4 \
  -e MockLlmApi__LlmBackends__0__ApiKey=sk-your-key-here \
  llmapi:latest

# Point to LM Studio
docker run -d \
  --name llmapi \
  -p 5116:8080 \
  -e MockLlmApi__BaseUrl=http://host.docker.internal:1234/v1/ \
  -e MockLlmApi__ModelName=local-model \
  llmapi:latest
```

---

## Advanced Configurations

### Multi-Backend Setup with Docker

**docker-appsettings.json:**
```json
{
  "MockLlmApi": {
    "LlmBackends": [
      {
        "Name": "ollama-local",
        "Provider": "ollama",
        "BaseUrl": "http://ollama:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "Weight": 2
      },
      {
        "Name": "ollama-gemma",
        "Provider": "ollama",
        "BaseUrl": "http://ollama:11434/v1/",
        "ModelName": "gemma3:4b",
        "Enabled": true,
        "Weight": 1
      }
    ]
  }
}
```

**docker-compose.yml:**
```yaml
services:
  ollama:
    image: ollama/ollama:latest
    volumes:
      - ollama_data:/root/.ollama

  llmapi:
    build: .
    volumes:
      - ./docker-appsettings.json:/app/appsettings.json:ro
    depends_on:
      - ollama
```

**Pre-pull multiple models:**
```bash
docker compose up -d ollama
docker exec ollama ollama pull llama3
docker exec ollama ollama pull gemma3:4b
docker compose up -d llmapi
```

### GPU Support for Ollama

For NVIDIA GPU acceleration:

**docker-compose.yml:**
```yaml
services:
  ollama:
    image: ollama/ollama:latest
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]
    environment:
      - NVIDIA_VISIBLE_DEVICES=all
```

**Prerequisites:**
```bash
# Install NVIDIA Container Toolkit
# See: https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html

# Verify GPU is available
docker run --rm --gpus all nvidia/cuda:11.8.0-base-ubuntu22.04 nvidia-smi
```

### Persistent Data with Named Volumes

```yaml
services:
  llmapi:
    volumes:
      # Configuration
      - ./docker-appsettings.json:/app/appsettings.json:ro

      # Logs directory
      - llmapi_logs:/app/logs

      # Cache directory (if using file-based caching)
      - llmapi_cache:/app/cache

volumes:
  ollama_data:
    driver: local
  llmapi_logs:
    driver: local
  llmapi_cache:
    driver: local
```

### Health Checks

**docker-compose.yml:**
```yaml
services:
  llmapi:
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
```

### Resource Limits

**docker-compose.yml:**
```yaml
services:
  ollama:
    deploy:
      resources:
        limits:
          cpus: '4'
          memory: 8G
        reservations:
          memory: 4G

  llmapi:
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
        reservations:
          memory: 512M
```

---

## OpenAPI / Swagger Specifications

The mock API supports automatically generating endpoints from OpenAPI/Swagger specifications. This section covers all methods for loading and using OpenAPI specs in Docker.

### Configuration Methods

There are **four ways** to load OpenAPI specifications:

1. **Volume-mapped spec files** (best for development)
2. **Upload via Management API** (best for runtime)
3. **Load from URL** (best for external specs)
4. **Environment variables** (best for docker-compose)

---

### Method 1: Volume-Mapped Spec Files

Mount local OpenAPI spec files into the container.

**Directory structure:**
```
myproject/
├── docker-compose.yml
└── openapi-specs/
    ├── petstore.yaml
    └── ecommerce.yaml
```

**docker-compose.yml:**
```yaml
version: '3.8'

services:
  ollama:
    image: ollama/ollama:latest
    ports:
      - "11434:11434"
    volumes:
      - ollama_data:/root/.ollama

  llmapi:
    image: llmapi:latest
    ports:
      - "5116:8080"
    volumes:
      # Mount OpenAPI specs directory
      - ./openapi-specs:/app/openapi-specs:ro
    environment:
      - MockLlmApi__BaseUrl=http://ollama:11434/v1/
      - MockLlmApi__ModelName=llama3
      # Configure spec 1
      - MockLlmApi__OpenApiSpecs__0__Name=petstore
      - MockLlmApi__OpenApiSpecs__0__SpecPath=/app/openapi-specs/petstore.yaml
      - MockLlmApi__OpenApiSpecs__0__MountPath=/petstore
      # Configure spec 2
      - MockLlmApi__OpenApiSpecs__1__Name=ecommerce
      - MockLlmApi__OpenApiSpecs__1__SpecPath=/app/openapi-specs/ecommerce.yaml
      - MockLlmApi__OpenApiSpecs__1__MountPath=/shop
    depends_on:
      - ollama

volumes:
  ollama_data:
```

**Start and test:**
```bash
docker compose up -d

# Test Pet Store endpoints
curl http://localhost:5116/petstore/pets
curl http://localhost:5116/petstore/pets/123

# Test E-Commerce endpoints
curl http://localhost:5116/shop/products
curl http://localhost:5116/shop/orders
```

**Sample petstore.yaml:**
```yaml
openapi: 3.0.0
info:
  title: Pet Store API
  version: 1.0.0

paths:
  /pets:
    get:
      summary: List all pets
      responses:
        '200':
          description: Array of pets
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Pet'

  /pets/{petId}:
    get:
      summary: Get a pet
      parameters:
        - name: petId
          in: path
          required: true
          schema:
            type: integer
      responses:
        '200':
          description: Pet details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'

components:
  schemas:
    Pet:
      type: object
      properties:
        id:
          type: integer
        name:
          type: string
        status:
          type: string
```

---

### Method 2: Upload via Management API

Upload specs at runtime using the management API.

**Start the container:**
```bash
docker run -d -p 5116:8080 \
  -e MockLlmApi__BaseUrl=http://host.docker.internal:11434/v1/ \
  -e MockLlmApi__ModelName=llama3 \
  --name llmapi \
  llmapi:latest
```

**Upload OpenAPI spec file:**
```bash
# Upload from local file
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=petstore" \
  -F "mountPath=/petstore" \
  -F "specFile=@petstore.yaml"

# Response:
# {
#   "name": "petstore",
#   "mountPath": "/petstore",
#   "endpoints": [
#     "GET /petstore/pets",
#     "GET /petstore/pets/{petId}",
#     "POST /petstore/pets",
#     "PUT /petstore/pets/{petId}",
#     "DELETE /petstore/pets/{petId}"
#   ]
# }
```

**Upload multiple specs:**
```bash
# Pet Store
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=petstore" \
  -F "mountPath=/petstore" \
  -F "specFile=@petstore.yaml"

# E-Commerce
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=ecommerce" \
  -F "mountPath=/shop" \
  -F "specFile=@ecommerce.yaml"

# User Management
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=users" \
  -F "mountPath=/users" \
  -F "specFile=@users.json"
```

**Test the generated endpoints:**
```bash
# Pet Store
curl http://localhost:5116/petstore/pets
curl http://localhost:5116/petstore/pets/42

# E-Commerce
curl http://localhost:5116/shop/products
curl "http://localhost:5116/shop/products?category=electronics"
curl http://localhost:5116/shop/orders

# User Management
curl http://localhost:5116/users/users
curl http://localhost:5116/users/roles
```

---

### Method 3: Load from URL

Load OpenAPI specs from external URLs (like Swagger Petstore).

**Load public Swagger specs:**
```bash
# Swagger Petstore v3
curl -X POST http://localhost:5116/api/management/openapi \
  -H "Content-Type: application/json" \
  -d '{
    "name": "petstore3",
    "specUrl": "https://petstore3.swagger.io/api/v3/openapi.json",
    "mountPath": "/petstore3"
  }'

# GitHub API spec (if available)
curl -X POST http://localhost:5116/api/management/openapi \
  -H "Content-Type: application/json" \
  -d '{
    "name": "github",
    "specUrl": "https://raw.githubusercontent.com/github/rest-api-description/main/descriptions/api.github.com/api.github.com.json",
    "mountPath": "/github"
  }'
```

**Test loaded specs:**
```bash
# Petstore3
curl http://localhost:5116/petstore3/pet/findByStatus?status=available
curl http://localhost:5116/petstore3/store/inventory
curl http://localhost:5116/petstore3/user/user1
```

**Load from private URL with authentication:**
```bash
# If your spec requires auth, download it first then upload
curl -H "Authorization: Bearer $TOKEN" \
  https://private-api.com/openapi.yaml \
  -o private-spec.yaml

curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=private-api" \
  -F "mountPath=/private" \
  -F "specFile=@private-spec.yaml"
```

---

### Method 4: Environment Variables

Configure specs via environment variables (best for docker-compose).

**Using .env file:**

**File: `.env`**
```bash
# Spec 1: Pet Store
MockLlmApi__OpenApiSpecs__0__Name=petstore
MockLlmApi__OpenApiSpecs__0__SpecUrl=https://petstore3.swagger.io/api/v3/openapi.json
MockLlmApi__OpenApiSpecs__0__MountPath=/petstore

# Spec 2: Local file
MockLlmApi__OpenApiSpecs__1__Name=ecommerce
MockLlmApi__OpenApiSpecs__1__SpecPath=/app/specs/ecommerce.yaml
MockLlmApi__OpenApiSpecs__1__MountPath=/shop

# Spec 3: Another URL
MockLlmApi__OpenApiSpecs__2__Name=jsonplaceholder
MockLlmApi__OpenApiSpecs__2__SpecUrl=https://jsonplaceholder.typicode.com/openapi.json
MockLlmApi__OpenApiSpecs__2__MountPath=/placeholder
```

**docker-compose.yml:**
```yaml
version: '3.8'

services:
  llmapi:
    image: llmapi:latest
    ports:
      - "5116:8080"
    env_file:
      - .env
    volumes:
      - ./specs:/app/specs:ro
    environment:
      - MockLlmApi__BaseUrl=http://ollama:11434/v1/
      - MockLlmApi__ModelName=llama3
```

**Start:**
```bash
docker compose --env-file .env up -d
```

---

### Management Operations

#### List All Loaded Specs

```bash
curl http://localhost:5116/api/management/openapi

# Response:
# [
#   {
#     "name": "petstore",
#     "mountPath": "/petstore",
#     "endpoints": [
#       "GET /petstore/pets",
#       "POST /petstore/pets",
#       "GET /petstore/pets/{petId}",
#       "PUT /petstore/pets/{petId}",
#       "DELETE /petstore/pets/{petId}"
#     ]
#   },
#   {
#     "name": "ecommerce",
#     "mountPath": "/shop",
#     "endpoints": [
#       "GET /shop/products",
#       "GET /shop/products/{productId}",
#       "GET /shop/orders",
#       "POST /shop/orders",
#       "GET /shop/customers"
#     ]
#   }
# ]
```

#### Get Specific Spec Details

```bash
curl http://localhost:5116/api/management/openapi/petstore

# Response:
# {
#   "name": "petstore",
#   "mountPath": "/petstore",
#   "endpoints": [...],
#   "openApiSpec": { ... } # Full OpenAPI spec
# }
```

#### Remove a Spec

```bash
curl -X DELETE http://localhost:5116/api/management/openapi/petstore

# Endpoints under /petstore are now removed
```

#### Reload/Update a Spec

```bash
# Upload new version with same name
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=petstore" \
  -F "mountPath=/petstore" \
  -F "specFile=@petstore-v2.yaml"

# Old version is replaced
```

---

### Complete Example: E-Commerce API

**File: ecommerce.yaml**
```yaml
openapi: 3.0.0
info:
  title: E-Commerce API
  version: 1.0.0

paths:
  /products:
    get:
      summary: List products
      parameters:
        - name: category
          in: query
          schema:
            type: string
        - name: minPrice
          in: query
          schema:
            type: number
      responses:
        '200':
          description: Products list
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Product'

  /products/{id}:
    get:
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Product details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Product'

  /orders:
    get:
      responses:
        '200':
          description: Orders list
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Order'
    post:
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/NewOrder'
      responses:
        '201':
          description: Order created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Order'

components:
  schemas:
    Product:
      type: object
      properties:
        id:
          type: string
        name:
          type: string
        price:
          type: number
        category:
          type: string
        stock:
          type: integer

    Order:
      type: object
      properties:
        id:
          type: string
        customerId:
          type: string
        items:
          type: array
          items:
            type: object
            properties:
              productId:
                type: string
              quantity:
                type: integer
        total:
          type: number
        status:
          type: string

    NewOrder:
      type: object
      required:
        - customerId
        - items
      properties:
        customerId:
          type: string
        items:
          type: array
          items:
            type: object
            properties:
              productId:
                type: string
              quantity:
                type: integer
```

**Upload and test:**
```bash
# Upload the spec
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=ecommerce" \
  -F "mountPath=/shop" \
  -F "specFile=@ecommerce.yaml"

# List all products
curl http://localhost:5116/shop/products

# Filter products
curl "http://localhost:5116/shop/products?category=electronics&minPrice=100"

# Get specific product
curl http://localhost:5116/shop/products/prod-123

# Get all orders
curl http://localhost:5116/shop/orders

# Create new order
curl -X POST http://localhost:5116/shop/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "cust-456",
    "items": [
      {"productId": "prod-123", "quantity": 2},
      {"productId": "prod-789", "quantity": 1}
    ]
  }'
```

---

### Sample Specs Included

The **mostlylucid.mockllmapi.Testing.Examples** project includes ready-to-use OpenAPI specs:

**Location:** `mostlylucid.mockllmapi.Testing.Examples/OpenApiSpecs/`

- **petstore-simple.yaml** - Simple pet store with CRUD operations
- **ecommerce-api.yaml** - Complete e-commerce with products, orders, customers

**Clone and use:**
```bash
git clone https://github.com/scottgal/LLMApi.git
cd LLMApi/mostlylucid.mockllmapi.Testing.Examples/OpenApiSpecs

# Upload samples
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=petstore" \
  -F "mountPath=/petstore" \
  -F "specFile=@petstore-simple.yaml"

curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=shop" \
  -F "mountPath=/shop" \
  -F "specFile=@ecommerce-api.yaml"
```

---

### Troubleshooting OpenAPI

**Issue:** Spec upload fails with "Invalid spec"

**Solution:**
```bash
# Validate your OpenAPI spec first
npm install -g @apidevtools/swagger-cli
swagger-cli validate myspec.yaml

# Or use online validator
# https://editor.swagger.io/
```

**Issue:** Endpoints not generating correctly

**Solution:**
```bash
# Check the loaded spec
curl http://localhost:5116/api/management/openapi/myspec

# Verify mount path doesn't conflict
curl http://localhost:5116/api/management/openapi

# Delete and re-upload
curl -X DELETE http://localhost:5116/api/management/openapi/myspec
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=myspec" \
  -F "mountPath=/api" \
  -F "specFile=@myspec.yaml"
```

**Issue:** File upload returns 404 or 400

**Solution:**
```bash
# Ensure management endpoints are enabled (they are by default)
# Check file format (must be -F for multipart form data)
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=test" \
  -F "mountPath=/test" \
  -F "specFile=@spec.yaml"  # @ is required for file upload

# Check file exists and is readable
ls -la spec.yaml
```

---

## Notes

### Demo Application

The LLMApi demo application includes a Dashboard page that has been temporarily excluded from Docker builds due to compilation issues with CSS-in-C# syntax. All API endpoints function normally - only the web Dashboard UI is unavailable in the Docker image.

If you need the Dashboard:
1. Fix the compilation errors in `Pages/Dashboard.cshtml` (lines 164, 197, 321, 409)
2. Remove the exclusion from `LLMApi/LLMApi.csproj`
3. Rebuild the Docker image

---

## Testing the Deployment

### Basic Tests

```bash
# Health check
curl http://localhost:5116/health

# Simple mock endpoint
curl "http://localhost:5116/api/mock/users?shape={\"id\":0,\"name\":\"\"}"

# With caching
curl "http://localhost:5116/api/mock/products?shape={\"id\":0,\"name\":\"\",\"price\":0.0}&cache=5"

# Error simulation
curl "http://localhost:5116/api/mock/error?error=404&errorMessage=Not%20Found"

# GraphQL
curl -X POST http://localhost:5116/api/mock/graphql \
  -H "Content-Type: application/json" \
  -d '{
    "query": "{ users { id name email } }"
  }'

# Streaming (SSE)
curl -N http://localhost:5116/api/mock/stream/data?shape={\"id\":0,\"value\":\"\"}
```

### Check Ollama Directly

```bash
# List available models
docker exec ollama ollama list

# Test Ollama directly
curl http://localhost:11434/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama3",
    "messages": [{"role": "user", "content": "Hello"}],
    "stream": false
  }'
```

### Performance Testing

```bash
# Install hey (HTTP load generator)
# macOS: brew install hey
# Linux: go install github.com/rakyll/hey@latest

# Load test
hey -n 100 -c 10 "http://localhost:5116/api/mock/users?shape={\"id\":0,\"name\":\"\"}"

# Results will show:
#   Total time
#   Requests/sec
#   Response times (min/max/avg)
```

---

## Troubleshooting

### Ollama Model Not Found

**Error:** `model 'llama3' not found`

**Solution:**
```bash
# Pull the model manually
docker exec ollama ollama pull llama3

# Or restart compose (will auto-pull)
docker compose restart ollama
```

### Connection Refused to Ollama

**Error:** `connection refused` when connecting to Ollama

**Solution:**
```bash
# Check Ollama is running
docker compose ps

# Check Ollama logs
docker compose logs ollama

# Verify network
docker network inspect llmapi_llmapi-network

# Test connection from llmapi container
docker exec llmapi curl http://ollama:11434/
```

### Port Already in Use

**Error:** `port is already allocated`

**Solution:**
```bash
# Find what's using the port
# Windows
netstat -ano | findstr :5116

# Linux/macOS
lsof -i :5116

# Change port in docker-compose.yml
ports:
  - "5117:8080"  # Use 5117 instead of 5116
```

### Out of Memory

**Error:** Ollama crashes or becomes unresponsive

**Solution:**
```bash
# Use a smaller model
docker compose down
# Edit docker-compose.yml: ModelName=gemma3:4b
docker compose up -d

# Or increase Docker memory limit
# Docker Desktop → Settings → Resources → Memory → 10GB+
```

### Slow Response Times

**Issue:** Responses take 30+ seconds

**Solution:**
```bash
# Check if using CPU instead of GPU
docker compose logs ollama

# Reduce max tokens
environment:
  - MockLlmApi__MaxContextWindow=4096

# Use faster model
  - MockLlmApi__ModelName=gemma3:4b

# Check host system load
docker stats
```

### Cannot Access from Host

**Issue:** `curl http://localhost:5116` fails

**Solution:**
```bash
# Check container is running
docker compose ps

# Check port mapping
docker compose port llmapi 8080

# Check firewall (Windows)
# Allow port 5116 in Windows Firewall

# Check Docker network
docker network inspect llmapi_llmapi-network

# Try container IP directly
docker inspect llmapi | grep IPAddress
curl http://<container-ip>:8080/health
```

### Configuration Not Applied

**Issue:** Changes to appsettings.json not taking effect

**Solution:**
```bash
# Restart is required for volume-mapped configs
docker compose restart llmapi

# For environment variables, recreate container
docker compose down
docker compose up -d

# Verify environment variables
docker exec llmapi printenv | grep MockLlmApi
```

---

## Docker Commands Reference

### Common Operations

```bash
# Start services
docker compose up -d

# Stop services
docker compose down

# Restart specific service
docker compose restart llmapi

# View logs (follow mode)
docker compose logs -f

# View logs (last 100 lines)
docker compose logs --tail=100

# Execute command in container
docker exec -it llmapi bash

# Check resource usage
docker stats

# Prune unused resources
docker system prune -a

# Remove specific service
docker compose rm -s -v llmapi
```

### Debugging

```bash
# Shell into llmapi container
docker exec -it llmapi bash

# Shell into ollama container
docker exec -it ollama bash

# Check environment variables
docker exec llmapi env

# Check network connectivity
docker exec llmapi ping ollama
docker exec llmapi curl http://ollama:11434

# View container details
docker inspect llmapi

# Check mounted volumes
docker inspect llmapi | grep Mounts -A 20
```

---

## Production Considerations

### Security

1. **Don't expose Ollama publicly**
   ```yaml
   services:
     ollama:
       ports: []  # Remove port mapping
       expose:
         - "11434"  # Only accessible within Docker network
   ```

2. **Use secrets for API keys**
   ```yaml
   services:
     llmapi:
       secrets:
         - openai_api_key

   secrets:
     openai_api_key:
       external: true
   ```

3. **Run as non-root user**
   ```dockerfile
   FROM base AS final
   RUN useradd -m -u 1000 appuser
   USER appuser
   ```

### Scalability

**Horizontal Scaling:**
```yaml
services:
  llmapi:
    deploy:
      replicas: 3
      restart_policy:
        condition: on-failure
```

**Load Balancer:**
```yaml
services:
  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
    depends_on:
      - llmapi
```

### Monitoring

**Add Prometheus metrics:**
```yaml
services:
  llmapi:
    environment:
      - ASPNETCORE_METRICS_ENABLED=true

  prometheus:
    image: prom/prometheus
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports:
      - "9090:9090"
```

---

## License

This project is released into the public domain under the [Unlicense](https://unlicense.org).
