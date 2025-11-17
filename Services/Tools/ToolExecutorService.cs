using System.Text.Json;
using chatrealtime.Configuration;
using Microsoft.Extensions.Options;

namespace chatrealtime.Services.Tools;

public class ToolExecutorService : IToolExecutor
{
    private readonly ILogger<ToolExecutorService> _logger;
    private readonly OpenAISettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public ToolExecutorService(
        ILogger<ToolExecutorService> logger,
        IOptions<OpenAISettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<object> ExecuteAsync(string toolName, JsonElement arguments)
    {
        _logger.LogInformation("Executing tool: {ToolName} with arguments: {Arguments}", 
            toolName, arguments.ToString());

        // Find tool configuration
        var toolConfig = _settings.Tools?.FirstOrDefault(t => 
            t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

        if (toolConfig == null)
        {
            throw new ArgumentException($"Tool configuration not found: {toolName}");
        }

        // Execute based on tool type
        return toolConfig.Type.ToLowerInvariant() switch
        {
            "http" => await ExecuteHttpToolAsync(toolConfig, arguments),
            "mcp" => await ExecuteMcpToolAsync(toolConfig, arguments),
            "builtin" => await ExecuteBuiltinToolAsync(toolName, arguments),
            _ => throw new ArgumentException($"Unknown tool type: {toolConfig.Type}")
        };
    }

    private async Task<object> ExecuteMcpToolAsync(ToolConfig toolConfig, JsonElement arguments)
    {
        if (toolConfig.Http == null)
        {
            throw new InvalidOperationException($"HTTP configuration missing for MCP tool: {toolConfig.Name}");
        }

        var httpConfig = toolConfig.Http;
        _logger.LogInformation("Executing MCP tool: {Url}", httpConfig.Url);

        // Use named client with Polly policies
        var httpClient = _httpClientFactory.CreateClient("ToolsHttpClient");
        
        // Add custom headers
        foreach (var header in httpConfig.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Extract MCP method and params from arguments
        string mcpMethod = "resources/list"; // default
        object? mcpParams = null;

        var argumentsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments.GetRawText());
        if (argumentsDict != null)
        {
            if (argumentsDict.TryGetValue("method", out var methodElement))
            {
                mcpMethod = methodElement.GetString() ?? "resources/list";
            }
            
            if (argumentsDict.TryGetValue("params", out var paramsElement))
            {
                // Parse params as a dictionary to properly serialize it
                try
                {
                    var paramsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paramsElement.GetRawText());
                    mcpParams = paramsDict;
                }
                catch
                {
                    mcpParams = JsonSerializer.Deserialize<object>(paramsElement.GetRawText());
                }
            }
            // If no explicit "params", check if other keys exist (like "uri" directly)
            else if (argumentsDict.Count > 1 || (argumentsDict.Count == 1 && !argumentsDict.ContainsKey("method")))
            {
                // Extract all other keys as params
                var paramDict = new Dictionary<string, object?>();
                foreach (var kvp in argumentsDict)
                {
                    if (kvp.Key != "method")
                    {
                        paramDict[kvp.Key] = JsonSerializer.Deserialize<object>(kvp.Value.GetRawText());
                    }
                }
                if (paramDict.Count > 0)
                {
                    mcpParams = paramDict;
                }
            }
        }

        // Determine the actual method based on tool name if not explicitly provided
        if (mcpMethod == "resources/list" && toolConfig.Name == "mcp_read_resource")
        {
            mcpMethod = "resources/read";
        }
        else if (mcpMethod == "resources/list" && toolConfig.Name == "mcp_list_tools")
        {
            mcpMethod = "tools/list";
        }
        else if (mcpMethod == "resources/list" && toolConfig.Name == "mcp_call_tool")
        {
            mcpMethod = "tools/call";
        }

        // Build JSON-RPC request with numeric ID (required by MCP protocol)
        var mcpRequest = new
        {
            jsonrpc = "2.0",
            id = Random.Shared.Next(1, 1000000),
            method = mcpMethod,
            @params = mcpParams
        };

        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var requestJson = JsonSerializer.Serialize(mcpRequest, jsonOptions);
        _logger.LogInformation("MCP Request: {Request}", requestJson);

        var content = new StringContent(
            requestJson,
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(httpConfig.Url, content);
        response.EnsureSuccessStatusCode();
        
        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("MCP Response: {Response}", responseBody);

        try
        {
            // Parse JSON-RPC response
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Check for error in JSON-RPC response
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.TryGetProperty("message", out var msg) 
                    ? msg.GetString() 
                    : "Unknown MCP error";
                throw new InvalidOperationException($"MCP Error: {errorMessage}");
            }

            // Return the result field from JSON-RPC response
            if (root.TryGetProperty("result", out var resultElement))
            {
                return JsonSerializer.Deserialize<object>(resultElement.GetRawText()) ?? new { };
            }

            return new { response = responseBody };
        }
        catch (JsonException)
        {
            // Return as string if not valid JSON
            return new { response = responseBody };
        }
    }

    private async Task<object> ExecuteHttpToolAsync(ToolConfig toolConfig, JsonElement arguments)
    {
        if (toolConfig.Http == null)
        {
            throw new InvalidOperationException($"HTTP configuration missing for tool: {toolConfig.Name}");
        }

        var httpConfig = toolConfig.Http;
        _logger.LogInformation("Executing HTTP tool: {Method} {Url}", httpConfig.Method, httpConfig.Url);

        // Use named client with Polly policies
        var httpClient = _httpClientFactory.CreateClient("ToolsHttpClient");
        
        // Add custom headers
        foreach (var header in httpConfig.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpResponseMessage response;
        var url = httpConfig.Url;

        // Replace URL parameters from arguments (for GET requests with path params)
        var argumentsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments.GetRawText());
        if (argumentsDict != null)
        {
            foreach (var arg in argumentsDict)
            {
                url = url.Replace($"{{{arg.Key}}}", arg.Value.ToString());
            }
        }

        switch (httpConfig.Method.ToUpperInvariant())
        {
            case "GET":
                // Add query parameters
                if (argumentsDict != null && argumentsDict.Any())
                {
                    var queryString = string.Join("&", argumentsDict.Select(kvp => 
                        $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value.ToString())}"));
                    url = url.Contains("?") ? $"{url}&{queryString}" : $"{url}?{queryString}";
                }
                response = await httpClient.GetAsync(url);
                break;

            case "POST":
            case "PUT":
                var content = new StringContent(
                    arguments.GetRawText(),
                    System.Text.Encoding.UTF8,
                    "application/json");
                
                response = httpConfig.Method.ToUpperInvariant() == "POST"
                    ? await httpClient.PostAsync(url, content)
                    : await httpClient.PutAsync(url, content);
                break;

            case "DELETE":
                response = await httpClient.DeleteAsync(url);
                break;

            default:
                throw new ArgumentException($"Unsupported HTTP method: {httpConfig.Method}");
        }

        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            // Try to parse as JSON
            var jsonResponse = JsonSerializer.Deserialize<object>(responseBody);
            return jsonResponse ?? responseBody;
        }
        catch
        {
            // Return as string if not valid JSON
            return new { response = responseBody };
        }
    }

    private async Task<object> ExecuteBuiltinToolAsync(string toolName, JsonElement arguments)
    {
        return toolName switch
        {
            "get_weather" => await GetWeatherAsync(arguments),
            "get_time" => await GetTimeAsync(arguments),
            "calculate" => await CalculateAsync(arguments),
            _ => throw new ArgumentException($"Unknown builtin tool: {toolName}")
        };
    }

    public List<string> GetAvailableTools()
    {
        return _settings.Tools?.Select(t => t.Name).ToList() ?? new List<string>();
    }

    private async Task<object> GetWeatherAsync(JsonElement arguments)
    {
        // Extract parameters
        var location = arguments.TryGetProperty("location", out var loc) 
            ? loc.GetString() 
            : throw new ArgumentException("Missing required parameter: location");

        var unit = arguments.TryGetProperty("unit", out var u) && u.GetString() == "fahrenheit" 
            ? "fahrenheit" 
            : "celsius";

        _logger.LogInformation("Getting weather for {Location} in {Unit}", location, unit);

        // Simulate API call (replace with real weather API)
        await Task.Delay(100);

        // Mock weather data
        var temperature = unit == "celsius" ? 22 : 72;
        var tempUnit = unit == "celsius" ? "°C" : "°F";

        return new
        {
            location = location,
            temperature = temperature,
            unit = tempUnit,
            condition = "Ensoleillé",
            humidity = 65,
            wind_speed = 15,
            description = $"Il fait actuellement {temperature}{tempUnit} à {location} avec un temps ensoleillé."
        };
    }

    private async Task<object> GetTimeAsync(JsonElement arguments)
    {
        var timezone = arguments.TryGetProperty("timezone", out var tz) 
            ? tz.GetString() 
            : throw new ArgumentException("Missing required parameter: timezone");

        _logger.LogInformation("Getting time for timezone: {Timezone}", timezone);

        await Task.Delay(50);

        try
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var currentTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZoneInfo);

            return new
            {
                timezone = timezone,
                datetime = currentTime.ToString("o"),
                formatted = currentTime.ToString("dddd d MMMM yyyy HH:mm:ss"),
                hour = currentTime.Hour,
                minute = currentTime.Minute,
                second = currentTime.Second
            };
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException($"Timezone not found: {timezone}");
        }
    }

    private async Task<object> CalculateAsync(JsonElement arguments)
    {
        var expression = arguments.TryGetProperty("expression", out var expr) 
            ? expr.GetString() 
            : throw new ArgumentException("Missing required parameter: expression");

        _logger.LogInformation("Calculating expression: {Expression}", expression);

        await Task.Delay(50);

        try
        {
            // Simple calculator using DataTable.Compute (for basic expressions)
            var dataTable = new System.Data.DataTable();
            var result = dataTable.Compute(expression, "");

            return new
            {
                expression = expression,
                result = result,
                formatted = $"{expression} = {result}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating expression: {Expression}", expression);
            throw new ArgumentException($"Invalid expression: {expression}");
        }
    }
}
