# 🚀 Guide de Démarrage Rapide

## Étapes pour lancer l'application

### 1️⃣ Configurer votre clé API OpenAI

**Option A : Fichier de développement (recommandé)**

Éditez `appsettings.Development.json` et remplacez la clé :

```json
{
  "OpenAI": {
    "ApiKey": "sk-proj-VOTRE_CLE_ICI"
  }
}
```

**Option B : Variable d'environnement**

```bash
export OpenAI__ApiKey="sk-proj-VOTRE_CLE_ICI"
```

### 2️⃣ Lancer l'application

```bash
dotnet run
```

### 3️⃣ Ouvrir dans le navigateur

Ouvrez **Chrome** ou **Edge** et allez sur :
- http://localhost:5166 (HTTP)

### 4️⃣ Utiliser l'application

1. **Autorisez** l'accès au microphone quand demandé
2. **Sélectionnez** votre microphone dans la liste
3. **Cliquez** sur "Démarrer l'écoute"
4. **Parlez** naturellement !

## ✅ Vérification

Si tout fonctionne correctement, vous devriez voir :

1. ✅ Message "Connecté à GPT-Live"
2. ✅ Bouton rouge "Arrêter l'écoute"
3. ✅ Indicateur "En écoute..." en haut
4. ✅ Vos paroles transcrites apparaissent en bleu
5. ✅ L'IA répond avec audio + texte en gris

## ⚠️ Problèmes courants

### "OpenAI API Key is not configured"
→ Vous n'avez pas configuré votre clé API dans `appsettings.Development.json`

### "Impossible d'accéder au microphone"
→ Autorisez l'accès dans les paramètres du navigateur

### "Failed to connect to OpenAI"
→ Vérifiez que votre clé API est valide et que votre projet a accès à GPT-Live

### Pas de son
→ Vérifiez le volume de votre navigateur et que vous utilisez Chrome/Edge

## 📝 Personnalisation rapide

### Changer la voix de l'IA

Dans `appsettings.json`, changez :
```json
"Voice": "marin"
```

Exemples de voix Live : `alloy`, `echo`, `marin`, `cedar`, `coral`, `sage`, `shimmer`.

### Changer le comportement de l'IA

Modifiez le fichier indiqué par `SystemPromptFile`, ou utilisez `Instructions` si aucun fichier n'est configuré :
```json
"Instructions": "Vous êtes un expert en cuisine. Répondez avec des conseils culinaires."
```

### Configurer les outils

Les règles de délégation sont séparées de la personnalité vocale :
```json
"DelegationModel": "gpt-5.6-luna",
"DelegationInstructions": "Traite les tâches déléguées. Utilise un outil pour les données externes ou les actions; sinon raisonne directement. Ne confirme jamais une action avant le résultat de l'outil. Retourne un résultat concis et factuel."
```

Gardez le prompt Live court et ajoutez-y des sections explicites `Backend tools`, `Delegate to the backend when` et `Do not delegate to the backend when`. Placez les procédures détaillées et la validation des résultats dans `DelegationInstructions`.

WebRTC et GPT-Live gèrent nativement le média, les tours de parole et les interruptions; aucun réglage PCM ou VAD historique n'est requis.

## 🎯 Prêt !

Vous êtes maintenant prêt à avoir des conversations vocales en temps réel avec GPT ! 🎉

Pour plus de détails, consultez le **README.md**.
