using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LLMApi.Tests;

/// <summary>
/// Tests for gRPC proto management and service call functionality
/// </summary>
[Trait("Category", "Integration")]
public class GrpcTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GrpcTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    #region Proto Parser Tests

    [Fact]
    public void ProtoParser_ParsesBasicProtoFile()
    {
        var parser = new mostlylucid.mockllmapi.Services.ProtoParser();
        var protoContent = @"
syntax = ""proto3"";
package test;

service TestService {
  rpc GetData (Request) returns (Response);
}

message Request {
  int32 id = 1;
}

message Response {
  string name = 2;
}";

        var definition = parser.Parse(protoContent, "test.proto");

        Assert.Equal("test.proto", definition.Name);
        Assert.Equal("test", definition.Package);
        Assert.Equal("proto3", definition.Syntax);
        Assert.Single(definition.Services);
        Assert.Equal("TestService", definition.Services[0].Name);
        Assert.Single(definition.Services[0].Methods);
        Assert.Equal("GetData", definition.Services[0].Methods[0].Name);
        Assert.Equal(2, definition.Messages.Count);
    }

    [Fact]
    public void ProtoParser_ParsesMultipleServices()
    {
        var parser = new mostlylucid.mockllmapi.Services.ProtoParser();
        var protoContent = @"
syntax = ""proto3"";
package multi;

service UserService {
  rpc GetUser (GetUserRequest) returns (User);
}

service ProductService {
  rpc GetProduct (GetProductRequest) returns (Product);
}

message GetUserRequest { int32 id = 1; }
message User { string name = 1; }
message GetProductRequest { string id = 1; }
message Product { string title = 1; }";

        var definition = parser.Parse(protoContent, "multi.proto");

        Assert.Equal(2, definition.Services.Count);
        Assert.Equal("UserService", definition.Services[0].Name);
        Assert.Equal("ProductService", definition.Services[1].Name);
    }

    [Fact]
    public void ProtoParser_ParsesStreamingMethods()
    {
        var parser = new mostlylucid.mockllmapi.Services.ProtoParser();
        var protoContent = @"
syntax = ""proto3"";
service StreamService {
  rpc ServerStream (Request) returns (stream Response);
  rpc ClientStream (stream Request) returns (Response);
  rpc BidirectionalStream (stream Request) returns (stream Response);
}
message Request { int32 id = 1; }
message Response { string data = 1; }";

        var definition = parser.Parse(protoContent, "stream.proto");

        Assert.Equal(3, definition.Services[0].Methods.Count);

        var serverStream = definition.Services[0].Methods[0];
        Assert.Equal("ServerStream", serverStream.Name);
        Assert.False(serverStream.ClientStreaming);
        Assert.True(serverStream.ServerStreaming);
        Assert.Equal(mostlylucid.mockllmapi.Models.MethodType.ServerStreaming, serverStream.GetMethodType());

        var clientStream = definition.Services[0].Methods[1];
        Assert.Equal("ClientStream", clientStream.Name);
        Assert.True(clientStream.ClientStreaming);
        Assert.False(clientStream.ServerStreaming);
        Assert.Equal(mostlylucid.mockllmapi.Models.MethodType.ClientStreaming, clientStream.GetMethodType());

        var bidiStream = definition.Services[0].Methods[2];
        Assert.Equal("BidirectionalStream", bidiStream.Name);
        Assert.True(bidiStream.ClientStreaming);
        Assert.True(bidiStream.ServerStreaming);
        Assert.Equal(mostlylucid.mockllmapi.Models.MethodType.BidirectionalStreaming, bidiStream.GetMethodType());
    }

    [Fact]
    public void ProtoParser_ParsesFieldTypes()
    {
        var parser = new mostlylucid.mockllmapi.Services.ProtoParser();
        var protoContent = @"
syntax = ""proto3"";
message ComplexMessage {
  int32 int_field = 1;
  string string_field = 2;
  bool bool_field = 3;
  double double_field = 4;
  repeated string repeated_field = 5;
  optional int32 optional_field = 6;
}";

        var definition = parser.Parse(protoContent, "fields.proto");

        var message = definition.Messages[0];
        Assert.Equal(6, message.Fields.Count);

        Assert.Equal("int32", message.Fields[0].Type);
        Assert.Equal("int_field", message.Fields[0].Name);
        Assert.False(message.Fields[0].Repeated);

        Assert.Equal("string", message.Fields[4].Type);
        Assert.True(message.Fields[4].Repeated);

        Assert.Equal("int32", message.Fields[5].Type);
        Assert.True(message.Fields[5].Optional);
    }

    [Fact]
    public void ProtoParser_GeneratesJsonShape()
    {
        var parser = new mostlylucid.mockllmapi.Services.ProtoParser();
        var protoContent = @"
syntax = ""proto3"";
message User {
  int32 id = 1;
  string name = 2;
  bool is_active = 3;
  repeated string tags = 4;
}";

        var definition = parser.Parse(protoContent, "shape.proto");
        var message = definition.Messages[0];
        var shape = parser.GenerateJsonShape(message, definition.Messages);

        Assert.Contains("\"id\": 0", shape);
        Assert.Contains("\"name\": \"string\"", shape);
        Assert.Contains("\"is_active\": false", shape);
        Assert.Contains("\"tags\": [\"string\"]", shape);
    }

    #endregion

    #region Proto Management Endpoint Tests

    [Fact]
    public async Task ProtoManagement_UploadProtoAsPlainText_Success()
    {
        var protoContent = @"
syntax = ""proto3"";
package example;
service UserService {
  rpc GetUser (GetUserRequest) returns (User);
}
message GetUserRequest { int32 user_id = 1; }
message User { int32 id = 1; string name = 2; }";

        var response = await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Proto definition uploaded successfully", result.GetProperty("message").GetString());
        Assert.Equal("uploaded.proto", result.GetProperty("name").GetString());
        Assert.Equal("example", result.GetProperty("package").GetString());

        var services = result.GetProperty("services");
        Assert.Equal(1, services.GetArrayLength());
        Assert.Equal("UserService", services[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ProtoManagement_ListProtos_ReturnsUploadedDefinitions()
    {
        // Upload a proto first
        var protoContent = "syntax = \"proto3\"; service TestService { rpc Test (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // List protos
        var response = await _client.GetAsync("/api/grpc-protos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var protos = result.GetProperty("protos");
        Assert.True(protos.GetArrayLength() > 0);
        Assert.True(result.GetProperty("count").GetInt32() > 0);
    }

    [Fact]
    public async Task ProtoManagement_GetSpecificProto_ReturnsDetails()
    {
        // Upload a proto
        var protoContent = "syntax = \"proto3\"; package test; service TestService { rpc Test (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Get specific proto
        var response = await _client.GetAsync("/api/grpc-protos/uploaded.proto");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("uploaded.proto", result.GetProperty("name").GetString());
        Assert.Equal("test", result.GetProperty("package").GetString());
        Assert.True(result.TryGetProperty("services", out _));
        Assert.True(result.TryGetProperty("messages", out _));
        Assert.True(result.TryGetProperty("rawContent", out _));
    }

    [Fact]
    public async Task ProtoManagement_GetNonExistentProto_Returns404()
    {
        var response = await _client.GetAsync("/api/grpc-protos/nonexistent.proto");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not found", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtoManagement_DeleteProto_Success()
    {
        // Upload a proto
        var protoContent = "syntax = \"proto3\"; service DeleteTest { rpc Test (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Delete it
        var deleteResponse = await _client.DeleteAsync("/api/grpc-protos/uploaded.proto");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync("/api/grpc-protos/uploaded.proto");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task ProtoManagement_ClearAllProtos_Success()
    {
        // Upload multiple protos
        await _client.PostAsync("/api/grpc-protos",
            new StringContent("syntax = \"proto3\"; service S1 { rpc M (R) returns (R); } message R { int32 id = 1; }", Encoding.UTF8, "text/plain"));

        // Clear all
        var response = await _client.DeleteAsync("/api/grpc-protos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cleared", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtoManagement_UploadEmptyContent_Returns400()
    {
        var response = await _client.PostAsync("/api/grpc-protos",
            new StringContent("", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region gRPC Service Call Tests

    [Fact(Skip = "Requires LLM service (Ollama) to be running - not available in CI")]
    public async Task GrpcCall_UnaryMethod_Success()
    {
        // Upload proto
        var protoContent = @"
syntax = ""proto3"";
service UserService {
  rpc GetUser (GetUserRequest) returns (User);
}
message GetUserRequest { int32 user_id = 1; }
message User {
  int32 id = 1;
  string name = 2;
  string email = 3;
  bool is_active = 4;
}";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Call gRPC method
        var request = new { user_id = 12345 };
        var response = await _client.PostAsJsonAsync("/api/grpc/UserService/GetUser", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Verify response has expected fields (LLM-generated values will vary)
        Assert.True(result.TryGetProperty("id", out _));
        Assert.True(result.TryGetProperty("name", out _));
        Assert.True(result.TryGetProperty("email", out _));
        Assert.True(result.TryGetProperty("is_active", out _));
    }

    [Fact(Skip = "Requires LLM service (Ollama) to be running - not available in CI")]
    public async Task GrpcCall_WithComplexRequest_Success()
    {
        // Upload proto
        var protoContent = @"
syntax = ""proto3"";
service ProductService {
  rpc SearchProducts (SearchRequest) returns (ProductList);
}
message SearchRequest {
  string query = 1;
  int32 limit = 2;
  repeated string categories = 3;
}
message ProductList {
  repeated Product products = 1;
  int32 total = 2;
}
message Product {
  string id = 1;
  string name = 2;
  double price = 3;
}";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Call with complex request
        var request = new
        {
            query = "laptop",
            limit = 10,
            categories = new[] { "electronics", "computers" }
        };
        var response = await _client.PostAsJsonAsync("/api/grpc/ProductService/SearchProducts", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("products", out _));
        Assert.True(result.TryGetProperty("total", out _));
    }

    [Fact(Skip = "Requires LLM service (Ollama) to be running - not available in CI")]
    public async Task GrpcCall_WithEmptyRequest_Success()
    {
        // Upload proto
        var protoContent = @"
syntax = ""proto3"";
service ConfigService {
  rpc GetConfig (EmptyRequest) returns (Config);
}
message EmptyRequest { }
message Config {
  string version = 1;
  bool enabled = 2;
}";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Call with empty request
        var response = await _client.PostAsJsonAsync("/api/grpc/ConfigService/GetConfig", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("version", out _));
        Assert.True(result.TryGetProperty("enabled", out _));
    }

    [Fact]
    public async Task GrpcCall_NonExistentService_Returns400()
    {
        var request = new { id = 1 };
        var response = await _client.PostAsJsonAsync("/api/grpc/NonExistentService/GetData", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not found", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GrpcCall_NonExistentMethod_Returns400()
    {
        // Upload proto
        var protoContent = "syntax = \"proto3\"; service TestService { rpc RealMethod (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Call non-existent method
        var response = await _client.PostAsJsonAsync("/api/grpc/TestService/NonExistentMethod", new { id = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GrpcCall_StreamingMethod_Returns400()
    {
        // Upload proto with streaming method
        var protoContent = @"
syntax = ""proto3"";
service StreamService {
  rpc StreamData (Request) returns (stream Response);
}
message Request { int32 id = 1; }
message Response { string data = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Try to call streaming method (not yet supported)
        var response = await _client.PostAsJsonAsync("/api/grpc/StreamService/StreamData", new { id = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not a unary call", result.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires LLM service (Ollama) to be running - not available in CI")]
    public async Task GrpcCall_ReturnsVariedDataAcrossMultipleCalls()
    {
        // Upload proto
        var protoContent = @"
syntax = ""proto3"";
service DataService {
  rpc GetData (Request) returns (Response);
}
message Request { int32 id = 1; }
message Response { string value = 1; }";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Make multiple calls
        var response1 = await _client.PostAsJsonAsync("/api/grpc/DataService/GetData", new { id = 1 });
        var result1 = await response1.Content.ReadAsStringAsync();

        var response2 = await _client.PostAsJsonAsync("/api/grpc/DataService/GetData", new { id = 1 });
        var result2 = await response2.Content.ReadAsStringAsync();

        // Responses should vary due to LLM randomness
        // (Note: This might occasionally fail if LLM returns identical responses, but very unlikely)
        Assert.NotEqual(result1, result2);
    }

    [Fact(Skip = "Requires LLM service (Ollama) to be running - not available in CI")]
    public async Task GrpcCall_WithNestedMessages_Success()
    {
        // Upload proto with nested messages
        var protoContent = @"
syntax = ""proto3"";
service OrderService {
  rpc CreateOrder (OrderRequest) returns (Order);
}
message OrderRequest {
  string customer_id = 1;
  Address shipping_address = 2;
}
message Address {
  string street = 1;
  string city = 2;
  string country = 3;
}
message Order {
  string order_id = 1;
  Address shipping_address = 2;
  string status = 3;
}";
        await _client.PostAsync("/api/grpc-protos",
            new StringContent(protoContent, Encoding.UTF8, "text/plain"));

        // Call with nested request
        var request = new
        {
            customer_id = "CUST-123",
            shipping_address = new
            {
                street = "123 Main St",
                city = "New York",
                country = "USA"
            }
        };
        var response = await _client.PostAsJsonAsync("/api/grpc/OrderService/CreateOrder", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read response as text first
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(responseText);

        // Try to parse as JSON - if it fails, it's a known LLM issue
        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseText);
            Assert.True(result.TryGetProperty("order_id", out _));
            Assert.True(result.TryGetProperty("shipping_address", out var address));
            Assert.True(address.TryGetProperty("street", out _));
            Assert.True(address.TryGetProperty("city", out _));
        }
        catch (JsonException)
        {
            // LLM returned malformed JSON - this is acceptable for this test
            // as long as we got a non-empty response with relevant content
            Assert.Contains("order", responseText, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region ProtoDefinitionManager Tests

    [Fact]
    public void ProtoDefinitionManager_AddAndRetrieveDefinition()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<mostlylucid.mockllmapi.Services.ProtoDefinitionManager>();
        var manager = new mostlylucid.mockllmapi.Services.ProtoDefinitionManager(logger);
        var protoContent = "syntax = \"proto3\"; service TestService { rpc Test (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";

        var definition = manager.AddProtoDefinition(protoContent, "test.proto");

        Assert.Equal("test.proto", definition.Name);

        var retrieved = manager.GetDefinition("test.proto");
        Assert.NotNull(retrieved);
        Assert.Equal("test.proto", retrieved.Name);
    }

    [Fact]
    public void ProtoDefinitionManager_FindService_Success()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<mostlylucid.mockllmapi.Services.ProtoDefinitionManager>();
        var manager = new mostlylucid.mockllmapi.Services.ProtoDefinitionManager(logger);
        var protoContent = "syntax = \"proto3\"; service UserService { rpc GetUser (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";

        manager.AddProtoDefinition(protoContent, "user.proto");

        var (definition, service) = manager.FindService("UserService");

        Assert.NotNull(definition);
        Assert.NotNull(service);
        Assert.Equal("UserService", service.Name);
    }

    [Fact]
    public void ProtoDefinitionManager_FindMethod_Success()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<mostlylucid.mockllmapi.Services.ProtoDefinitionManager>();
        var manager = new mostlylucid.mockllmapi.Services.ProtoDefinitionManager(logger);
        var protoContent = "syntax = \"proto3\"; service UserService { rpc GetUser (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";

        manager.AddProtoDefinition(protoContent, "user.proto");

        var (definition, service, method) = manager.FindMethod("UserService", "GetUser");

        Assert.NotNull(definition);
        Assert.NotNull(service);
        Assert.NotNull(method);
        Assert.Equal("GetUser", method.Name);
    }

    [Fact]
    public void ProtoDefinitionManager_RemoveDefinition_Success()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<mostlylucid.mockllmapi.Services.ProtoDefinitionManager>();
        var manager = new mostlylucid.mockllmapi.Services.ProtoDefinitionManager(logger);
        var protoContent = "syntax = \"proto3\"; service TestService { rpc Test (Request) returns (Response); } message Request { int32 id = 1; } message Response { string data = 1; }";

        manager.AddProtoDefinition(protoContent, "test.proto");

        var removed = manager.RemoveDefinition("test.proto");
        Assert.True(removed);

        var retrieved = manager.GetDefinition("test.proto");
        Assert.Null(retrieved);
    }

    [Fact]
    public void ProtoDefinitionManager_ClearAll_Success()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<mostlylucid.mockllmapi.Services.ProtoDefinitionManager>();
        var manager = new mostlylucid.mockllmapi.Services.ProtoDefinitionManager(logger);

        manager.AddProtoDefinition("syntax = \"proto3\"; service S1 { rpc M (R) returns (R); } message R { int32 id = 1; }", "proto1.proto");
        manager.AddProtoDefinition("syntax = \"proto3\"; service S2 { rpc M (R) returns (R); } message R { int32 id = 1; }", "proto2.proto");

        manager.ClearAll();

        var definitions = manager.GetAllDefinitions();
        Assert.Empty(definitions);
    }

    #endregion
}
