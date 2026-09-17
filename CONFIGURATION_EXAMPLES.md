# 📚 Exemples de Configuration

Ce document récapitule tous les fichiers d'exemple de configuration disponibles dans le projet.

## 📂 Fichiers disponibles

### 🔧 Configuration principale
- **`appsettings.json`** - Configuration active (à personnaliser avec votre clé API)

### 📋 Exemples de résilience HTTP (Polly)

#### 1️⃣ APIs externes instables
**Fichier** : `appsettings.example-external-unstable.json`

**Quand l'utiliser** :
- Appels vers des APIs tierces (météo, stock, paiement)
- Services externes avec temps de réponse variables
- APIs connues pour avoir des interruptions

**Caractéristiques** :
- ✅ 5 retries maximum
- ✅ Délai initial de 500ms (jusqu'à 15s)
- ✅ Circuit breaker tolérant (10 échecs avant ouverture)
- ✅ Timeout de 60 secondes

**Exemples d'outils configurés** :
- Vérification de stock externe
- Notifications via API tierce

```bash
cp appsettings.example-external-unstable.json appsettings.json
```

---

#### 2️⃣ Appels locaux rapides
**Fichier** : `appsettings.example-local-fast.json`

**Quand l'utiliser** :
- Appels vers `localhost` (Redis, PostgreSQL, etc.)
- Microservices dans le même réseau local
- APIs de cache ou recherche rapide

**Caractéristiques** :
- ✅ 2 retries seulement
- ✅ Délai initial de 50ms (max 500ms)
- ✅ Circuit breaker réactif (3 échecs)
- ✅ Timeout de 5 secondes

**Exemples d'outils configurés** :
- Recherche dans base locale
- Mise à jour de cache

```bash
cp appsettings.example-local-fast.json appsettings.json
```

---

#### 3️⃣ Tests et développement
**Fichier** : `appsettings.example-no-resilience.json`

**Quand l'utiliser** :
- Tests unitaires / intégration
- Développement local
- Debugging d'APIs
- Vérification de la logique

**Caractéristiques** :
- ❌ Retry désactivé
- ❌ Circuit breaker désactivé
- ❌ Timeout désactivé

**⚠️ NE JAMAIS utiliser en production !**

```bash
cp appsettings.example-no-resilience.json appsettings.json
```

---

#### 4️⃣ Production équilibrée
**Fichier** : `appsettings.example-production.json`

**Quand l'utiliser** :
- Environnement de production
- Mix d'APIs externes et services internes
- Configuration équilibrée fiabilité/performance

**Caractéristiques** :
- ✅ 4 retries
- ✅ Délai initial de 200ms (max 10s)
- ✅ Circuit breaker modéré (7 échecs)
- ✅ Timeout de 30 secondes
- ✅ Logs optimisés pour la production

**Exemples d'outils configurés** :
- API météo externe
- Recherche en base de données
- Système d'alertes

```bash
cp appsettings.example-production.json appsettings.json
```

---

### 🔌 Exemples d'outils HTTP

#### 5️⃣ Outils HTTP configurables
**Fichier** : `appsettings.example-http.json`

**Quand l'utiliser** :
- Besoin d'appeler vos propres APIs sans modifier le code C#
- Intégration avec des services externes
- Webhooks et callbacks

**Exemples d'outils configurés** :
- `search_database` - POST vers une base de données
- `get_user_info` - GET avec paramètres dans l'URL
- `external_api` - POST avec headers personnalisés

```bash
# Voir les exemples d'outils HTTP
cat appsettings.example-http.json
```

---

## 📖 Guide détaillé

Pour comprendre en profondeur chaque paramètre et faire le meilleur choix :

```bash
# Lire le guide complet de résilience
cat Prompts/ResilienceGuide.md
```

Le guide contient :
- 📊 Tableau de décision rapide
- 🔍 Explication détaillée de chaque paramètre
- ⚠️ Pièges courants à éviter
- 📈 Conseils de monitoring
- 🎓 Ressources supplémentaires

---

## 🚀 Utilisation rapide

### Copier un exemple complet
```bash
# Production
cp appsettings.example-production.json appsettings.json

# APIs externes
cp appsettings.example-external-unstable.json appsettings.json

# Local/LAN
cp appsettings.example-local-fast.json appsettings.json

# Tests
cp appsettings.example-no-resilience.json appsettings.json
```

### Fusionner uniquement la section Resilience
```bash
# 1. Ouvrez votre appsettings.json
# 2. Copiez la section "Resilience" depuis l'exemple choisi
# 3. Remplacez ou ajoutez-la dans votre configuration
```

### Personnaliser
```json
{
  "OpenAI": {
    "Resilience": {
      "Retry": {
        "Enabled": true,
        "MaxRetryAttempts": 3,     // Ajustez selon vos besoins
        "InitialDelayMs": 100,
        "MaxDelayMs": 5000
      },
      "CircuitBreaker": {
        "Enabled": true,
        "FailureThreshold": 5,
        "BreakDurationSeconds": 30,
        "SamplingDurationSeconds": 60
      },
      "Timeout": {
        "Enabled": true,
        "TimeoutSeconds": 30
      }
    }
  }
}
```

---

## 📊 Tableau comparatif

| Exemple | Retries | Délai initial | Circuit Breaker | Timeout | Cas d'usage |
|---------|---------|---------------|-----------------|---------|-------------|
| **External Unstable** | 5 | 500ms | 10 échecs / 60s | 60s | APIs tierces |
| **Local Fast** | 2 | 50ms | 3 échecs / 10s | 5s | Localhost/LAN |
| **No Resilience** | ❌ | - | ❌ | ❌ | Tests/Debug |
| **Production** | 4 | 200ms | 7 échecs / 45s | 30s | Production |
| **HTTP Tools** | 3 | 100ms | 5 échecs / 30s | 30s | Outils HTTP |

---

## 🔄 Migration depuis une version antérieure

Si vous avez déjà un `appsettings.json` sans résilience :

1. **Ajoutez la section Resilience** :
```json
{
  "OpenAI": {
    "ApiKey": "votre-clé",
    "Model": "gpt-live-1",
    "LiveSessionsUrl": "https://api.openai.com/v1/live/sessions",
    "LiveSidebandUrl": "wss://api.openai.com/v1/live",
    "DelegationModel": "gpt-5.6-luna",
    // ... vos paramètres existants ...
    
    // ⬇️ Ajoutez ceci
    "Resilience": {
      "Retry": {
        "Enabled": true,
        "MaxRetryAttempts": 3,
        "InitialDelayMs": 100,
        "MaxDelayMs": 5000
      },
      "CircuitBreaker": {
        "Enabled": true,
        "FailureThreshold": 5,
        "BreakDurationSeconds": 30,
        "SamplingDurationSeconds": 60
      },
      "Timeout": {
        "Enabled": true,
        "TimeoutSeconds": 30
      }
    }
  }
}
```

2. **Redémarrez l'application** :
```bash
dotnet run
```

3. **Vérifiez les logs** :
```
[Polly Retry] Retry 1/3 after 100ms. Reason: ...
```

---

## 🧪 Tester la résilience

### Simuler des échecs
```bash
# 1. Configurez un outil HTTP vers une URL invalide
{
  "Name": "test_failure",
  "Type": "http",
  "Http": {
    "Url": "http://localhost:9999/fail",
    "Method": "GET"
  }
}

# 2. Appelez l'outil via l'API REST
curl -X POST http://localhost:5166/api/tools/test_failure \
  -H "Content-Type: application/json" \
  -d '{}'

# 3. Observez les logs Polly
```

### Vérifier le circuit breaker
```bash
# Appelez plusieurs fois un outil qui échoue
for i in {1..10}; do
  curl -X POST http://localhost:5166/api/tools/test_failure \
    -H "Content-Type: application/json" \
    -d '{}'
  sleep 1
done

# Logs attendus :
# [Polly Retry] Retry 1/3...
# [Polly Retry] Retry 2/3...
# [Polly Circuit Breaker] Circuit opened for 30s...
```

---

## 📞 Support

- **Documentation Polly** : https://github.com/App-vNext/Polly
- **Guide de résilience** : `Prompts/ResilienceGuide.md`
- **README principal** : `README.md`

---

**Bon développement ! 🚀**
