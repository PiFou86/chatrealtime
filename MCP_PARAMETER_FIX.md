# Fix: Structure des paramètres pour les outils MCP dynamiques

## 🐛 Problème

Les outils MCP découverts dynamiquement étaient appelés correctement, mais les paramètres n'étaient pas structurés comme attendu. Par exemple, un outil attendant `{ "key": "ma-clef" }` recevait quelque chose comme `{ "key": JsonElement }` ou un type incorrect.

### Cause racine

Dans `ExecuteMcpDynamicToolAsync()`, le code désérialisait les arguments avec :

```csharp
var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments.GetRawText());
```

Le problème : quand on désérialise en `Dictionary<string, object>`, le `JsonSerializer` ne sait pas quel type concret utiliser pour les valeurs. Il peut donc créer des `JsonElement` au lieu de `string`, `int`, `bool`, etc.

## ✅ Solution

Nouvelle approche : **Parser explicitement chaque type JSON**

### 1. Méthode `ParseJsonElementToObject()`

Convertit un `JsonElement` en type C# approprié selon son `ValueKind` :

```csharp
private object? ParseJsonElementToObject(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),           // "text" → string
        JsonValueKind.Number => /* int, long, ou double */,   // 42 → int
        JsonValueKind.True => true,                           // true → bool
        JsonValueKind.False => false,                         // false → bool
        JsonValueKind.Null => null,                           // null → null
        JsonValueKind.Object => ParseJsonObject(element),     // {...} → Dictionary
        JsonValueKind.Array => ParseJsonArray(element),       // [...] → List
        _ => element.GetRawText()                             // fallback → string
    };
}
```

### 2. Parsing récursif

Pour les objets et tableaux imbriqués :

```csharp
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

### 3. Construction de la requête MCP

```csharp
// Parse arguments element by element to preserve types
var argsDict = new Dictionary<string, object?>();
foreach (var property in arguments.EnumerateObject())
{
    argsDict[property.Name] = ParseJsonElementToObject(property.Value);
}

var mcpRequest = new
{
    jsonrpc = "2.0",
    id = 123456,
    method = "tools/call",
    @params = new
    {
        name = "my_tool",
        arguments = argsDict  // Maintenant avec les bons types !
    }
};
```

## 📊 Avant / Après

### Avant (types incorrects)

OpenAI envoie :
```json
{
  "key": "ma-clef",
  "count": 5,
  "enabled": true
}
```

Le code créait :
```csharp
{
  "key": JsonElement { ValueKind = String },
  "count": JsonElement { ValueKind = Number },
  "enabled": JsonElement { ValueKind = True }
}
```

Le serveur MCP recevait :
```json
{
  "arguments": {
    "key": { /* objet JsonElement sérialisé */ },
    "count": { /* objet JsonElement sérialisé */ },
    ...
  }
}
```

### Après (types corrects)

OpenAI envoie :
```json
{
  "key": "ma-clef",
  "count": 5,
  "enabled": true
}
```

Le code crée :
```csharp
{
  "key": "ma-clef",        // string
  "count": 5,              // int
  "enabled": true          // bool
}
```

Le serveur MCP reçoit :
```json
{
  "jsonrpc": "2.0",
  "id": 123456,
  "method": "tools/call",
  "params": {
    "name": "my_tool",
    "arguments": {
      "key": "ma-clef",
      "count": 5,
      "enabled": true
    }
  }
}
```

## 🎯 Types supportés

| Type JSON | Type C# | Exemple |
|-----------|---------|---------|
| string | `string` | `"hello"` → `"hello"` |
| number (entier) | `int` ou `long` | `42` → `42` |
| number (décimal) | `double` | `3.14` → `3.14` |
| true/false | `bool` | `true` → `true` |
| null | `null` | `null` → `null` |
| object | `Dictionary<string, object?>` | `{"a":1}` → `{"a": 1}` |
| array | `List<object?>` | `[1,2,3]` → `[1, 2, 3]` |

## ✅ Résultat

Les outils MCP reçoivent maintenant les paramètres avec les types corrects, exactement comme défini dans leur schéma `inputSchema`.

## 🧪 Test

Pour tester, appelez un outil avec différents types :

```typescript
// Côté OpenAI
{
  "name": "mcp_my_tool",
  "arguments": {
    "text": "hello",
    "count": 42,
    "ratio": 3.14,
    "enabled": true,
    "tags": ["a", "b", "c"],
    "config": {
      "nested": "value"
    }
  }
}
```

Le serveur MCP devrait recevoir exactement ces types, sans `JsonElement` intermédiaires.
