# Chat vocal GPT-Live

Application ASP.NET Core 9 de conversation vocale et textuelle avec `gpt-live-1`.

Le navigateur échange le média directement avec OpenAI par WebRTC. Le serveur conserve la clé API, crée la session avec `POST /v1/live/sessions`, attache une connexion sideband de confiance et exécute les outils HTTP/MCP demandés par le backend Responses.

## Fonctionnalités

- audio bidirectionnel WebRTC et interruptions naturelles;
- transcriptions utilisateur et assistant regroupées par événement et horodatage Live;
- messages texte via `response.item.create`, puis `response.create`;
- délégation Responses distincte de la personnalité vocale;
- outils HTTP et découverte dynamique MCP exécutés sur le sideband serveur;
- permissions minimales sur le canal de données du navigateur;
- fermeture des sessions et libération du microphone.

## Architecture

```text
Navigateur ── WebRTC audio ─────────────── OpenAI GPT-Live
    │                                           │
    ├─ POST /api/live/session (offre SDP) ── Serveur ASP.NET Core
    │                                           │
    └─ canal de données Live              sideband WebSocket
                                                │
                                         outils HTTP / MCP
```

Le serveur attache le sideband avant de retourner la réponse SDP au navigateur. La clé OpenAI n'est jamais envoyée au client.

## Démarrage

Prérequis : SDK .NET 9, navigateur moderne avec WebRTC, microphone et clé ayant accès à GPT-Live.

```bash
dotnet restore
dotnet run
```

Ouvrez ensuite l’URL affichée par ASP.NET Core (par défaut dans ce dépôt : `http://localhost:5166`).

Configurez la clé de préférence hors du fichier versionné :

```bash
export OpenAI__ApiKey="sk-proj-..."
```

## Configuration

La section `OpenAI` de `appsettings.json` accepte notamment :

| Paramètre | Rôle | Défaut |
|---|---|---|
| `Model` | Modèle de conversation Live | `gpt-live-1` |
| `LiveSessionsUrl` | Création WebRTC | `https://api.openai.com/v1/live/sessions` |
| `LiveSidebandUrl` | Racine utilisée pour `/sessions/{id}/attach` | `wss://api.openai.com/v1/live` |
| `Voice` | Voix Live, fixée au démarrage | `alloy` |
| `SystemPromptFile` | Personnalité et conduite de la conversation | `Prompts/Marvin.md` |
| `DelegationModel` | Modèle Responses pour texte et outils | `gpt-5.6-luna` |
| `DelegationInstructions` | Règles métier du backend délégué | voir `appsettings.json` |
| `MaxResponseOutputTokens` | Limite par réponse déléguée | `4096` |
| `Tools` | Fonctions HTTP/MCP configurées | `[]` |
| `McpServers` | Serveurs MCP découverts au premier démarrage de session | `[]` |

Les anciens paramètres PCM, transcription externe, température, VAD historique et vitesse de lecture ne sont plus utilisés : WebRTC négocie le média et GPT-Live gère les tours de parole.

Le prompt indiqué par `SystemPromptFile` doit rester court : rôle, ton vocal, interruptions et conditions concrètes de délégation. Les procédures détaillées, règles métier et consignes d'utilisation des outils appartiennent à `DelegationInstructions`. GPT-Live traite directement la conversation courante et délègue au backend Responses les outils, les données externes et le raisonnement approfondi. `tool_choice` reste à `auto` : il contrôle l'emploi des outils après délégation, pas la décision de déléguer.

## API locale

- `POST /api/live/session` — corps JSON `{ "sdp": "<offre>" }`; retourne `{ sessionId, sdp, type }`.
- `DELETE /api/live/session/{sessionId}` — ferme et nettoie la session/sideband.
- `GET /api/tools` — liste les outils configurés.
- `POST /api/tools/{toolName}` — exécute un outil directement.

## Outils et MCP

Les entrées `Tools` sont exposées comme outils `function` au backend Responses. Les outils découverts sur les serveurs `McpServers` sont ajoutés sans dupliquer les noms configurés. Lorsqu'un événement imbriqué `response.output_item.done` contient un `function_call`, le serveur :

1. valide les arguments JSON;
2. appelle `IToolExecutor`;
3. renvoie un `function_call_output` avec `response.item.create`;
4. poursuit avec `response.create`.

Un échec d’outil est renvoyé au modèle sous forme d’un résultat d’erreur; un nouveau type d’événement inconnu est ignoré sans fermer la session.

## Tests

```bash
dotnet test
```

Les tests automatisés n'appellent pas OpenAI. Un essai réel reste manuel et facultatif : vérifiez la négociation, le micro, l'audio distant, l'interruption, les transcriptions, le texte, un outil HTTP/MCP et la libération du microphone à l'arrêt.

Références : [modèle GPT-Live 1](https://developers.openai.com/api/docs/models/gpt-live-1), [API Live](https://developers.openai.com/api/reference/typescript/resources/live).
