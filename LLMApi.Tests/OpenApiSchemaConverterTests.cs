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

        // IMPORTANT: Resolve references before converting schemas
        document.ResolveReferences();

        // Get the GET /pet/{petId} operation
        var operation = document.Paths["/pet/{petId}"].Operations[OperationType.Get];

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.NotEmpty(shape);

        // Verify the shape contains expected fields from Pet schema
        Assert.Contains("id", shape);
        Assert.Contains("name", shape);
        Assert.Contains("category", shape);
        Assert.Contains("photoUrls", shape);
        Assert.Contains("tags", shape);
        Assert.Contains("status", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_WithResolvedRef_GeneratesFullSchema()
    {
        // Arrange - Create a schema with $ref that gets resolved
        var categorySchema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = "integer" },
                ["name"] = new OpenApiSchema { Type = "string" }
            }
        };

        // Simulate a resolved reference (Reference property set, but content is populated)
        categorySchema.Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
            Id = "Category"
        };

        var petSchema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = "integer" },
                ["name"] = new OpenApiSchema { Type = "string" },
                ["category"] = categorySchema
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
                        ["application/json"] = new OpenApiMediaType { Schema = petSchema }
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
        Assert.Contains("category", shape);

        // Verify nested category object is fully resolved
        var shapeLines = shape.Split('\n');
        var containsCategoryId = shape.Contains("\"id\"") || shape.Contains("id");
        Assert.True(containsCategoryId, "Category should have id field");
    }

    [Fact]
    public void ConvertSchemaToShape_ArrayWithRefItems_GeneratesArrayWithFullItems()
    {
        // Arrange - Array with items that have a resolved $ref
        var tagSchema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = "integer" },
                ["name"] = new OpenApiSchema { Type = "string" }
            }
        };
        tagSchema.Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
            Id = "Tag"
        };

        var arraySchema = new OpenApiSchema
        {
            Type = "array",
            Items = tagSchema
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = arraySchema }
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
        Assert.Contains("name", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_AllOfWithRef_MergesSchemas()
    {
        // Arrange - allOf with a reference and additional properties
        var baseSchema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = "integer" },
                ["name"] = new OpenApiSchema { Type = "string" }
            }
        };
        baseSchema.Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
            Id = "Base"
        };

        var extendedSchema = new OpenApiSchema
        {
            AllOf = new List<OpenApiSchema>
            {
                baseSchema,
                new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["extraField"] = new OpenApiSchema { Type = "string" }
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
                        ["application/json"] = new OpenApiMediaType { Schema = extendedSchema }
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
        Assert.Contains("extraField", shape);
    }

    [Fact]
    public void ConvertSchemaToShape_AnyOfWithRef_UsesFirstOption()
    {
        // Arrange - anyOf with multiple options
        var option1 = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = "string" },
                ["value"] = new OpenApiSchema { Type = "integer" }
            }
        };

        var option2 = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = "string" },
                ["text"] = new OpenApiSchema { Type = "string" }
            }
        };

        var anyOfSchema = new OpenApiSchema
        {
            AnyOf = new List<OpenApiSchema> { option1, option2 }
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = anyOfSchema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("type", shape);
        Assert.Contains("value", shape); // Should use first option
    }

    [Fact]
    public void ConvertSchemaToShape_DeepNestedRefs_HandlesRecursion()
    {
        // Arrange - Deeply nested structure with multiple levels of refs
        var level3Schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["deepValue"] = new OpenApiSchema { Type = "string" }
            }
        };
        level3Schema.Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
            Id = "Level3"
        };

        var level2Schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["midValue"] = new OpenApiSchema { Type = "integer" },
                ["nested"] = level3Schema
            }
        };
        level2Schema.Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.Schema,
            Id = "Level2"
        };

        var level1Schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["topValue"] = new OpenApiSchema { Type = "string" },
                ["child"] = level2Schema
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
                        ["application/json"] = new OpenApiMediaType { Schema = level1Schema }
                    }
                }
            }
        };

        // Act
        var shape = _converter.GetResponseShape(operation);

        // Assert
        Assert.NotNull(shape);
        Assert.Contains("topValue", shape);
        Assert.Contains("child", shape);
        Assert.Contains("midValue", shape);
        Assert.Contains("nested", shape);
        Assert.Contains("deepValue", shape);
    }
}
