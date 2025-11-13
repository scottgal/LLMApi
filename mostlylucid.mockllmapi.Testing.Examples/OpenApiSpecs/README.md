# OpenAPI Specification Examples

This directory contains sample OpenAPI specifications for testing with **mostlylucid.mockllmapi**.

## Files

- **petstore-simple.yaml** - Simple pet store API with basic CRUD operations
- **ecommerce-api.yaml** - Complete e-commerce API with products, orders, and customers

## Usage with Docker

### Option 1: Volume Map Spec Files

```bash
# Map the specs directory into the container
docker run -d -p 5116:8080 \
  -v $(pwd)/OpenApiSpecs:/app/specs:ro \
  -e MockLlmApi__BaseUrl=http://host.docker.internal:11434/v1/ \
  -e MockLlmApi__ModelName=llama3 \
  -e "MockLlmApi__OpenApiSpecs__0__Name=petstore" \
  -e "MockLlmApi__OpenApiSpecs__0__SpecPath=/app/specs/petstore-simple.yaml" \
  -e "MockLlmApi__OpenApiSpecs__0__MountPath=/petstore" \
  llmapi:latest
```

### Option 2: Upload Specs via API

```bash
# Upload the spec file
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=petstore" \
  -F "mountPath=/petstore" \
  -F "specFile=@petstore-simple.yaml"

# Upload another spec
curl -X POST http://localhost:5116/api/management/openapi \
  -F "name=ecommerce" \
  -F "mountPath=/shop" \
  -F "specFile=@ecommerce-api.yaml"
```

### Option 3: Load from URL

```bash
curl -X POST http://localhost:5116/api/management/openapi \
  -H "Content-Type: application/json" \
  -d '{
    "name": "petstore3",
    "specUrl": "https://petstore3.swagger.io/api/v3/openapi.json",
    "mountPath": "/petstore3"
  }'
```

## Testing Generated Endpoints

Once loaded, the API will generate mock endpoints based on the spec:

```bash
# Pet Store API
curl http://localhost:5116/petstore/pets
curl http://localhost:5116/petstore/pets/123
curl -X POST http://localhost:5116/petstore/pets \
  -H "Content-Type: application/json" \
  -d '{"name": "Fluffy", "status": "available"}'

# E-Commerce API
curl http://localhost:5116/shop/products
curl "http://localhost:5116/shop/products?category=electronics&minPrice=10"
curl http://localhost:5116/shop/orders
curl http://localhost:5116/shop/customers/cust-123
```

## Listing Loaded Specs

```bash
# Get all loaded OpenAPI specs
curl http://localhost:5116/api/management/openapi

# Response example:
# [
#   {
#     "name": "petstore",
#     "mountPath": "/petstore",
#     "endpoints": [
#       "GET /petstore/pets",
#       "POST /petstore/pets",
#       "GET /petstore/pets/{petId}"
#     ]
#   }
# ]
```

## Removing Specs

```bash
# Remove a loaded spec
curl -X DELETE http://localhost:5116/api/management/openapi/petstore
```

## See Also

- [OpenAPI Features Guide](../../../docs/OPENAPI-FEATURES.md)
- [Docker Deployment Guide](../../../docs/DOCKER_GUIDE.md)
- [Testing Examples](../Program.cs)
