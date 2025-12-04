# Découverte Automatique des MCPs

## 📋 Vue d'ensemble

Le système de découverte automatique des MCPs (Model Context Protocol) permet de configurer simplement un serveur MCP en spécifiant uniquement son URL. Le système découvre automatiquement tous les outils, ressources et prompts disponibles.

## ✨ Fonctionnalités

### Avant (Configuration manuelle)

Vous deviez configurer manuellement chaque outil MCP dans `appsettings.json` :

```json
{
  "Tools": [
    { "Name": "mcp_ping", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_list_tools", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_call_tool", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_read_resource", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_list_resources", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_list_prompts", "Type": "mcp", "Parameters": {...}, "Http": {...} },
    { "Name": "mcp_get_prompt", "Type": "mcp", "Parameters": {...}, "Http": {...} }
  ],
  "McpServers": [
    { "Name": "mcp", "Url": "http://localhost:5175/mcp", "Description": "serveur MCP local" }
  ]
}
```

### Maintenant (Configuration automatique)

Il suffit de spécifier l'URL du serveur MCP :

```json
{
  "Tools": [],
  "McpServers": [
    { "Name": "mcp", "Url": "http://localhost:5175/mcp", "Description": "serveur MCP local" }
  ]
}
```

Le système découvre automatiquement :
- ✅ Tous les outils exposés par le serveur MCP
- ✅ Les schémas de paramètres de chaque outil
- ✅ Les ressources disponibles
- ✅ Les prompts disponibles

## 🏗️ Architecture

### Nouveaux composants

1. **`McpDiscoveryService`** (`Services/McpDiscoveryService.cs`)
   - Service responsable de la découverte des capacités MCP
   - Interroge chaque serveur MCP au démarrage
   - Parse les réponses et crée dynamiquement les configurations d'outils
   - Génère un résumé des capacités découvertes

2. **Type d'outil `mcp_dynamic`**
   - Nouveau type d'outil pour les outils découverts dynamiquement
   - Géré par `ToolExecutorService.ExecuteMcpDynamicToolAsync()`
   - Convertit automatiquement les appels en requêtes `tools/call` MCP

### Modifications des composants existants

1. **`OpenAIRealtimeService`**
   - Injecte `McpDiscoveryService`
   - Appelle la découverte au démarrage (avant `ConfigureSessionAsync`)
   - Combine les outils configurés manuellement avec les outils découverts
   - Ajoute un résumé des capacités au prompt système

2. **`ToolExecutorService`**
   - Nouveau cas `mcp_dynamic` dans le switch
   - Méthode `ExecuteMcpDynamicToolAsync()` pour gérer les outils dynamiques
   - Extrait le nom d'outil original et appelle `tools/call`

3. **`Program.cs`**
   - Enregistre `McpDiscoveryService` comme singleton
   - Génère toujours les outils système MCP (ping, list_tools, etc.)

## 🔄 Flux de découverte

```
1. Démarrage de l'application
   ↓
2. Program.cs génère les outils système MCP
   (mcp_ping, mcp_list_tools, mcp_list_resources, mcp_list_prompts, 
    mcp_get_prompt, mcp_read_resource, mcp_call_tool)
   ↓
3. OpenAIRealtimeService.ConnectAsync()
   ↓
4. DiscoverMcpCapabilitiesAsync()
   ↓
5. McpDiscoveryService.DiscoverAllServersAsync()
   ↓
   Pour chaque serveur MCP:
     a. Ping (test de connectivité)
     b. list_tools (découverte des outils)
     c. Parse chaque outil et création de ToolConfig
     d. list_resources (pour info)
     e. list_prompts (pour info)
   ↓
6. Ajout des outils découverts à _settings.Tools
   ↓
7. ConfigureSessionAsync() envoie tous les outils à OpenAI
   ↓
8. L'assistant peut maintenant utiliser tous les outils découverts
```

## 📦 Format des outils découverts

Les outils MCP découverts sont automatiquement wrappés :

**Outil MCP original** : `my_custom_tool`
**Outil exposé à OpenAI** : `mcp_my_custom_tool`

Quand OpenAI appelle `mcp_my_custom_tool(args)`, le système :
1. Extrait le nom original : `my_custom_tool`
2. Crée une requête JSON-RPC `tools/call` :
   ```json
   {
     "jsonrpc": "2.0",
     "id": 123456,
     "method": "tools/call",
     "params": {
       "name": "my_custom_tool",
       "arguments": { ...args }
     }
   }
   ```
3. Envoie la requête au serveur MCP
4. Retourne le résultat à OpenAI

## 🎯 Avantages

1. **Configuration minimale** : Une seule ligne par serveur MCP
2. **Découverte automatique** : Plus besoin de définir manuellement les outils
3. **Synchronisation automatique** : Les nouveaux outils sont découverts au redémarrage
4. **Schémas de paramètres** : Les types et validations sont préservés
5. **Multi-serveurs** : Support de plusieurs serveurs MCP simultanément
6. **Logs détaillés** : Suivi complet du processus de découverte
7. **Résilience** : Continue même si un serveur est indisponible

## 🔧 Configuration

### Configuration minimale

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_API_KEY",
    "McpServers": [
      {
        "Name": "mcp",
        "Url": "http://localhost:5175/mcp",
        "Description": "Mon serveur MCP"
      }
    ]
  }
}
```

### Configuration avec plusieurs serveurs

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_API_KEY",
    "McpServers": [
      {
        "Name": "local",
        "Url": "http://localhost:5175/mcp",
        "Description": "Serveur MCP local"
      },
      {
        "Name": "remote",
        "Url": "https://api.example.com/mcp",
        "Description": "Serveur MCP distant"
      }
    ]
  }
}
```

Les outils seront préfixés par le nom du serveur :
- `local_my_tool`, `local_another_tool`, ...
- `remote_tool_1`, `remote_tool_2`, ...

### Ajout d'outils manuels (optionnel)

Vous pouvez toujours ajouter des outils manuels (builtin, http) :

```json
{
  "Tools": [
    {
      "Name": "get_weather",
      "Description": "Obtenir la météo",
      "Type": "builtin",
      "Parameters": { ... }
    }
  ],
  "McpServers": [ ... ]
}
```

Les outils manuels et découverts coexistent.

## 🐛 Dépannage

### Les outils ne sont pas découverts

1. **Vérifier les logs** : Rechercher "MCP discovery" dans la console
2. **Tester la connectivité** : Vérifier que le serveur MCP est accessible
3. **Vérifier le format de réponse** : Le serveur doit respecter le protocole MCP

### Erreur "Tool configuration not found"

L'outil n'a pas été découvert. Vérifications :
1. Le serveur MCP retourne bien l'outil dans `list_tools`
2. Les logs montrent la découverte de l'outil
3. Redémarrer l'application pour forcer une nouvelle découverte

### Les paramètres ne sont pas validés

Le serveur MCP doit fournir un `inputSchema` valide (JSON Schema).

## 📝 Logs

Le système log les étapes importantes :

```
info: Starting MCP discovery for 1 server(s)
info: Discovering tools from MCP server: mcp (http://localhost:5175/mcp)
info: ✅ MCP server mcp is accessible
info: Fetching tool list from MCP server: mcp
info:   ✓ Discovered tool: mcp_my_tool
info:   ✓ Discovered tool: mcp_another_tool
info:   ℹ️ Server has 5 resources available
info:   ℹ️ Server has 2 prompts available
info: Successfully discovered 2 tools from mcp
info: MCP discovery completed. Total tools discovered: 2
info: Configured 9 total tools for OpenAI: mcp_ping, mcp_list_tools, mcp_list_resources, mcp_read_resource, mcp_call_tool, mcp_list_prompts, mcp_get_prompt, mcp_my_tool, mcp_another_tool
```

## 🚀 Prochaines étapes

Le système est maintenant prêt à utiliser ! Au démarrage, il découvrira automatiquement tous les outils de vos serveurs MCP configurés.

Pour ajouter un nouveau serveur MCP, ajoutez simplement une entrée dans `McpServers` et redémarrez l'application.
