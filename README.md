# Chat Realtime avec OpenAI GPT

Application de chat vocal en temps réel utilisant l'API OpenAI Realtime pour des conversations audio bidirectionnelles.

## 🎯 Fonctionnalités

- 🎤 **Capture audio en temps réel** depuis le microphone
- 🤖 **Intégration OpenAI Realtime API** avec le modèle `gpt-realtime-mini-2025-10-06`
- 💬 **Transcription automatique** des conversations (utilisateur et IA)
- 🔊 **Réponses audio** jouées directement dans le navigateur
- 🎨 **Interface moderne et responsive** avec animations
- ⚡ **Communication WebSocket** bidirectionnelle (Client ↔ Serveur C# ↔ OpenAI)

## 🏗️ Architecture

```
Client Web (Browser)
    ↓ WebSocket
Serveur ASP.NET Core 9.0
    ↓ WebSocket
OpenAI Realtime API
```

### Flux de données

1. **Audio utilisateur** : Microphone → Client → WebSocket → Serveur C# → OpenAI
2. **Réponse IA** : OpenAI → Serveur C# → WebSocket → Client → Haut-parleurs

## 📋 Prérequis

- **.NET 9.0 SDK** ou supérieur
- **Clé API OpenAI** avec accès à l'API Realtime
- **Navigateur moderne** supportant WebSocket et Web Audio API (Chrome, Edge recommandés)
- **Microphone** fonctionnel

## 🚀 Installation

### 1. Cloner ou télécharger le projet

```bash
cd /chemin/vers/chatrealtime
```

### 2. Configurer la clé API OpenAI

Ouvrez le fichier `appsettings.json` et remplacez `YOUR_OPENAI_API_KEY_HERE` par votre clé API :

```json
{
  "OpenAI": {
    "ApiKey": "sk-proj-xxxxxxxxxxxxxxxxxxxxx",
    "Model": "gpt-realtime-mini-2025-10-06",
    ...
  }
}
```

### 3. Restaurer les dépendances

```bash
dotnet restore
```

### 4. Lancer l'application

```bash
dotnet run
```

L'application sera disponible à :
- **HTTP** : http://localhost:5000
- **HTTPS** : https://localhost:5001

## ⚙️ Configuration

Toutes les configurations se trouvent dans `appsettings.json` :

| Paramètre | Description | Valeur par défaut |
|-----------|-------------|-------------------|
| `ApiKey` | Votre clé API OpenAI | À configurer |
| `Model` | Modèle OpenAI à utiliser | `gpt-realtime-mini-2025-10-06` |
| `Voice` | Voix de l'IA (alloy, echo, fable, onyx, nova, shimmer) | `alloy` |
| `Temperature` | Créativité des réponses (0.0 - 2.0) | `0.8` |
| `Instructions` | Instructions système pour l'IA | Texte personnalisable |
| `TurnDetection.Type` | Type de détection de tour de parole | `server_vad` |
| `TurnDetection.Threshold` | Seuil de détection vocale (0.0 - 1.0) | `0.5` |
| `TurnDetection.SilenceDurationMs` | Durée de silence pour fin de phrase | `500` ms |

### Exemple de personnalisation

Pour changer le comportement de l'IA, modifiez `Instructions` :

```json
"Instructions": "Vous êtes un expert en programmation. Répondez de manière technique et concise."
```

Pour changer la voix :

```json
"Voice": "nova"
```

## 🎮 Utilisation

1. **Ouvrez l'application** dans votre navigateur
2. **Sélectionnez un microphone** dans la liste déroulante
3. **Cliquez sur "Démarrer l'écoute"** (le bouton devient rouge)
4. **Parlez naturellement** - l'IA vous répondra automatiquement
5. **Les transcriptions** apparaissent en temps réel dans le chat
6. **Cliquez sur "Arrêter l'écoute"** pour terminer

### Indicateurs visuels

- 🟢 **Vert** : Prêt à écouter
- 🔴 **Rouge** : En écoute active
- 💬 **Messages bleus** : Vos paroles
- 💬 **Messages gris** : Réponses de l'IA

## 📁 Structure du projet

```
chatrealtime/
├── Configuration/
│   └── OpenAISettings.cs          # Configuration OpenAI
├── Models/
│   ├── RealtimeEvents.cs          # Événements API Realtime
│   └── ClientMessage.cs           # Messages WebSocket
├── Services/
│   ├── OpenAIRealtimeService.cs   # Service connexion OpenAI
│   └── RealtimeWebSocketHandler.cs # Gestion WebSocket client
├── wwwroot/
│   ├── index.html                 # Interface utilisateur
│   ├── styles.css                 # Styles CSS
│   └── app.js                     # Logique JavaScript
├── Program.cs                     # Point d'entrée
├── appsettings.json              # Configuration
└── chatrealtime.csproj           # Fichier projet
```

## 🔧 Spécifications techniques

### Audio

- **Format** : PCM16 (16-bit linear PCM)
- **Sample Rate** : 24 000 Hz
- **Canaux** : Mono (1 canal)
- **Taille buffer** : 4096 samples

### WebSocket

- **Endpoint client** : `ws(s)://host/ws/realtime`
- **Keep-alive** : 120 secondes
- **Format messages** : JSON

### Messages WebSocket

#### Client → Serveur

```json
{
  "type": "audio",
  "audio": "base64_encoded_pcm16_data"
}
```

#### Serveur → Client

```json
{
  "type": "audio|transcript|status|error",
  "audio": "base64_audio",
  "transcript": "texte transcrit",
  "role": "user|assistant",
  "status": "message de statut"
}
```

## 🐛 Résolution des problèmes

### "OpenAI API Key is not configured"

➡️ Vérifiez que vous avez bien configuré votre clé API dans `appsettings.json`

### "Impossible d'accéder au microphone"

➡️ Autorisez l'accès au microphone dans les paramètres de votre navigateur

### "Failed to connect to OpenAI"

➡️ Vérifiez :
- Votre clé API est valide
- Vous avez accès à l'API Realtime
- Votre connexion Internet fonctionne

### Pas de son dans les réponses

➡️ Vérifiez :
- Le volume de votre navigateur
- Les permissions audio du navigateur
- Que vous utilisez Chrome ou Edge

## 📝 Notes importantes

- **Coût** : L'API OpenAI Realtime est facturée à l'usage. Surveillez votre consommation.
- **Navigateurs** : Chrome et Edge recommandés pour une meilleure compatibilité
- **Sécurité** : Ne commitez JAMAIS votre clé API dans un repository public
- **Production** : Pour la production, utilisez des variables d'environnement pour stocker la clé API

## 🔒 Sécurité

Pour la production, utilisez des variables d'environnement :

```bash
export OpenAI__ApiKey="sk-proj-xxxxx"
dotnet run
```

Ou configurez dans `appsettings.Development.json` (non versionné) :

```json
{
  "OpenAI": {
    "ApiKey": "votre-clé-ici"
  }
}
```

## 📚 Ressources

- [Documentation OpenAI Realtime API](https://platform.openai.com/docs/guides/realtime)
- [ASP.NET Core WebSockets](https://learn.microsoft.com/aspnet/core/fundamentals/websockets)
- [Web Audio API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API)

## 📄 Licence

Ce projet est fourni à des fins éducatives et de démonstration.
