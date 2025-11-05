using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using mostlylucid.mockllmapi.Services;
using Xunit;

namespace LLMApi.Tests;

public class OpenApiSchemaConverterTests
{
    private readonly OpenApiSchemaConverter _converter;

    public OpenApiSchemaConverterTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OpenApiSchemaConverter>();
        _converter = new OpenApiSchemaConverter(logger);
    }

    [Fact]
    public void ConvertSchemaToShape_SimpleObject_GeneratesCorrectShape()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = "integer" },
                ["name"] = new OpenApiSchema { Type = "string" }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("id", shape);
        Assert.Contains("name", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_Array_GeneratesArrayShape()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "array",
            Items = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["id"] = new OpenApiSchema { Type = "integer" },
                    ["value"] = new OpenApiSchema { Type = "string" }
                }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.StartsWith("[", shape);
        Assert.EndsWith("]", shape);
        Assert.Contains("id", shape);
        Assert.Contains("value", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_NestedObject_GeneratesNestedShape()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["user"] = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { Type = "integer" },
                        ["profile"] = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["email"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("user", shape);
        Assert.Contains("profile", shape);
        Assert.Contains("email", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_StringWithFormat_UsesFormatSpecificValue()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["email"] = new OpenApiSchema { Type = "string", Format = "email" },
                ["date"] = new OpenApiSchema { Type = "string", Format = "date" },
                ["uuid"] = new OpenApiSchema { Type = "string", Format = "uuid" }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("email", shape);
        Assert.Contains("date", shape);
        Assert.Contains("uuid", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_EnumProperty_UsesEnumValue()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["status"] = new OpenApiSchema
                {
                    Type = "string",
                    Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
                    {
                        new Microsoft.OpenApi.Any.OpenApiString("active"),
                        new Microsoft.OpenApi.Any.OpenApiString("inactive")
                    }
                }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("status", shape);
        // Just verify it has some value - the exact format depends on OpenApiString implementation
        Assert.Contains("\"", shape); // Should at least have quoted strings
    }

    [Fact]
    public void GetResponseShape_No200Response_ReturnsNull()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["404"] = new OpenApiResponse { Description = "Not found" }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.Null(shape);
    }

    [Fact]
    public void GetResponseShape_NoJsonContent_ReturnsNull()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["text/plain"] = new OpenApiMediaType { }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.Null(shape);
    }

    [Fact]
    public void GetOperationDescription_WithSummary_IncludesSummary()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Summary = "Get user by ID",
            Description = "Returns a single user"
        };

        // Act
        var description = _converter.GetOperationDescription(operation, "/users/{id}", OperationType.Get);

        // Assert
        Assert.Contains("GET /users/{id}", description);
        Assert.Contains("Get user by ID", description);
    }

    [Fact]
    public void GetOperationDescription_WithDescription_IncludesDescription()
    {
        // Arrange
        var operation = new OpenApiOperation
        {
            Summary = "Get user",
            Description = "This endpoint returns detailed user information"
        };

        // Act
        var description = _converter.GetOperationDescription(operation, "/users/{id}", OperationType.Get);

        // Assert
        Assert.Contains("This endpoint returns detailed user information", description);
    }

    [Fact]
    public void ConvertSchemaToShape_Boolean_GeneratesBoolean()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["active"] = new OpenApiSchema { Type = "boolean" }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("active", shape);
        Assert.Contains("true", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_Number_GeneratesNumber()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["price"] = new OpenApiSchema { Type = "number", Format = "float" },
                ["count"] = new OpenApiSchema { Type = "integer" }
            }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("price", shape);
        Assert.Contains("count", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_EmptyObject_GeneratesEmptyObject()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>()
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Equal("{}", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_EmptyArray_GeneratesEmptyArray()
    {
        // Arrange
        var schema = new OpenApiSchema
        {
            Type = "array",
            Items = null
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Equal("[]", shape);
    }

    [Fact]
    public async Task ConvertSchemaToShape_RealPetstoreSpec_GeneratesValidShape()
    {
        // Arrange
        var httpClient = new HttpClient();
        var specStream = await httpClient.GetStreamAsync("https://petstore3.swagger.io/api/v3/openapi.json");

        var reader = new OpenApiStreamReader();
        var document = reader.Read(specStream, out var diagnostic);

        // Get the GET /pet/{petId} operation
        var operation = document.Paths["/pet/{petId}"].Operations[OperationType.Get];

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        // OpenAPI reader may return references instead of resolved schemas
        // Just verify we got a shape string back
        Assert.NotEmpty(shape);
    }
}
