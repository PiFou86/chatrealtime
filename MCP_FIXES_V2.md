# Corrections MCP v2: Arguments et Double Appels

## 🐛 Problèmes identifiés

### 1. Structure des paramètres incorrecte
Les arguments n'étaient toujours pas correctement structurés malgré la première tentative de correction.

### 2. Double appel à `list_tools`
Le système appelait `list_tools` deux fois pour chaque serveur MCP :
- Une fois dans `DiscoverServerAsync()` pour découvrir les outils
- Une fois dans `GenerateCapabilitiesSummaryAsync()` pour le résumé

## ✅ Solutions implémentées

### 1. Amélioration du parsing des arguments

#### Nouveau code dans `ExecuteMcpDynamicToolAsync()`

```csharp
// Parse arguments element by element to preserve types
var argsDict = new Dictionary<string, object?>();
foreach (var property in arguments.EnumerateObject())
{
    argsDict[property.Name] = ParseJsonElementToObject(property.Value);
}

var paramsObj = new Dictionary<string, object>
{
    ["name"] = originalToolName
};

// Only add arguments if there are any
if (arguments.ValueKind != JsonValueKind.Undefined && 
    arguments.ValueKind != JsonValueKind.Null &&
    !(arguments.ValueKind == JsonValueKind.Object && arguments.EnumerateObject().Any() == false))
{
    paramsObj["arguments"] = argsDict;
}
```

#### Méthodes helpers améliorées

```csharp
private object? ParseJsonElementToObject(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : 
                               element.TryGetInt64(out var longVal) ? longVal : 
                               element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ParseJsonObject(element),
        JsonValueKind.Array => ParseJsonArray(element),
        _ => element.GetRawText()
    };
}

private Dictionary<string, object?> ParseJsonObject(JsonElement element)
{
    var dict = new Dictionary<string, object?>();
    foreach (var property in element.EnumerateObject())
    {
        dict[property.Name] = ParseJsonElementToObject(property.Value);
    }
    return dict;
}

private List<object?> ParseJsonArray(JsonElement element)
{
    var list = new List<object?>();
    foreach (var item in element.EnumerateArray())
    {
        list.Add(ParseJsonElementToObject(item));
    }
    return list;
}
```

### 2. Cache des capacités MCP

#### Nouvelle structure de cache

```csharp
private Dictionary<string, ServerCapabilities> _capabilitiesCache = new();

private class ServerCapabilities
{
    public List<JsonElement> Tools { get; set; } = new();
    public JsonElement? ToolsResponse { get; set; }
    public JsonElement? ResourcesResponse { get; set; }
    public JsonElement? PromptsResponse { get; set; }
}
```

#### Mise en cache lors de la découverte

```csharp
// In DiscoverServerAsync()
var serverKey = $"{mcpServer.Name}_{mcpServer.Url}";

// After fetching tools
var root = doc.RootElement.Clone(); // Clone to keep after doc is disposed
_capabilitiesCache[serverKey] = new ServerCapabilities
{
    ToolsResponse = root
};

// After fetching resources
var resRoot = resDoc.RootElement.Clone();
_capabilitiesCache[serverKey].ResourcesResponse = resRoot;

// After fetching prompts
var promptRoot = promptDoc.RootElement.Clone();
_capabilitiesCache[serverKey].PromptsResponse = promptRoot;
```

#### Utilisation du cache dans le résumé

```csharp
public Task<string> GenerateCapabilitiesSummaryAsync(CancellationToken cancellationToken = default)
{
    // ...
    foreach (var mcpServer in _settings.McpServers)
    {
        var serverKey = $"{mcpServer.Name}_{mcpServer.Url}";
        
        // Use cached capabilities instead of making new API calls
        if (!_capabilitiesCache.TryGetValue(serverKey, out var capabilities))
        {
            _logger.LogWarning("No cached capabilities for server {Name}", mcpServer.Name);
            continue;
        }

        // Use capabilities.ToolsResponse, capabilities.ResourcesResponse, etc.
        // No more API calls!
    }
    // ...
}
```

### 3. Logs améliorés

Ajout de logs détaillés pour debugger :

```csharp
_logger.LogInformation("[MCP Dynamic] Calling tool '{OriginalName}' on server '{Prefix}'", 
    originalToolName, serverPrefix);
_logger.LogInformation("[MCP Dynamic] RAW arguments from OpenAI: {Arguments}", 
    arguments.GetRawText());
_logger.LogInformation("[MCP Dynamic] Final JSON-RPC request being sent:");
_logger.LogInformation("[MCP Dynamic] {Request}", requestJson);
```

## 📊 Impact des corrections

### Avant

```
1. Découverte
   → list_tools (appel #1)
   → list_resources
   → list_prompts
   
2. Génération résumé
   → list_tools (appel #2) ❌ DUPLIQUÉ
   → list_resources (appel #2) ❌ DUPLIQUÉ

Total: 6 appels API
Arguments: types incorrects ❌
```

### Après

```
1. Découverte + mise en cache
   → list_tools (appel #1) ✅
   → list_resources
   → list_prompts
   
2. Génération résumé
   → Utilise le cache ✅ PAS D'APPEL

Total: 3 appels API (50% de réduction)
Arguments: types corrects ✅
```

## 🎯 Bénéfices

1. **Performance** : 50% moins d'appels API au démarrage
2. **Fiabilité** : Arguments correctement typés
3. **Débogage** : Logs détaillés pour identifier les problèmes
4. **Consistance** : Même données utilisées pour découverte et résumé

## 🧪 Test

Pour vérifier que tout fonctionne :

### 1. Vérifier les logs au démarrage

Vous devriez voir :

```
info: Fetching tool list from MCP server: mcp
info: ✅ MCP server mcp is accessible
info: Discovered tool: mcp_my_tool
```

**PAS** de deuxième appel à `list_tools` !

### 2. Vérifier les arguments lors d'un appel d'outil

Logs attendus :

```
info: [MCP Dynamic] Calling tool 'my_tool' on server 'mcp'
info: [MCP Dynamic] RAW arguments from OpenAI: {"key":"my-value","count":42}
info: [MCP Dynamic] Final JSON-RPC request being sent:
info: [MCP Dynamic] {"jsonrpc":"2.0","id":123456,"method":"tools/call","params":{"name":"my_tool","arguments":{"key":"my-value","count":42}}}
```

Les types doivent être corrects :
- `"key": "my-value"` (string, pas JsonElement)
- `"count": 42` (number, pas JsonElement)

### 3. Vérifier la réponse du serveur MCP

Le serveur MCP devrait maintenant recevoir les bons types et répondre correctement.

## ✅ Résultat attendu

- ✅ Un seul appel à `list_tools` par serveur au démarrage
- ✅ Arguments correctement typés (string, int, bool, etc.)
- ✅ Logs détaillés pour le débogage
- ✅ Performance améliorée (moins d'appels réseau)
