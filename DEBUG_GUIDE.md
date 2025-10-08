# 🐛 Guide de Débogage

## Problèmes identifiés

1. **Les messages utilisateur ne s'affichent pas**
2. **L'audio ne joue plus**

## Comment déboguer

### Étape 1 : Lancer l'application avec les logs

```bash
dotnet run
```

### Étape 2 : Ouvrir la console du navigateur

1. Ouvrez Chrome/Edge
2. Allez sur `https://localhost:5001`
3. Appuyez sur **F12** pour ouvrir les outils développeur
4. Allez dans l'onglet **Console**

### Étape 3 : Tester une conversation

1. Sélectionnez votre microphone
2. Cliquez sur "Démarrer l'écoute"
3. **Dites quelque chose** (ex: "Bonjour")
4. **Observez les logs** dans la console du navigateur ET dans le terminal

## Logs à vérifier

### Dans le navigateur (Console F12)

Vous devriez voir :
```
[Client] Message du serveur: ready
[Client] Message du serveur: status
[Client] Message du serveur: transcript {role: "user", transcript: "Bonjour"}
[Client] Transcript reçu - Role: user Texte: Bonjour
[Client] Audio reçu, taille: XXXX
[Client] Démarrage lecture audio
```

### Dans le terminal (dotnet run)

Vous devriez voir :
```
[OpenAI Event] session.created
[OpenAI Event] input_audio_buffer.speech_started
[OpenAI Event] input_audio_buffer.speech_stopped
[OpenAI Event] conversation.item.created
[Item Created] Type: message, Role: user
[User Transcript] Bonjour
[Handler] Sending transcript to client - Role: user, Text: Bonjour
[OpenAI Event] response.audio.delta
[Audio Delta] Size: XXXX bytes
[Handler] Sending audio to client, size: XXXX
```

## Solutions selon les symptômes

### Symptôme 1 : Pas de transcript utilisateur

**Si vous voyez dans le terminal :**
- ❌ `[Item Created] Type: message, Role: user` MAIS PAS `[User Transcript]`
  → Le transcript n'est pas dans l'événement `conversation.item.created`

**Si vous voyez dans le navigateur :**
- ❌ `[Client] Message du serveur: transcript` MAIS PAS `[Client] Transcript reçu`
  → Problème de parsing côté client

### Symptôme 2 : Pas d'audio

**Si vous voyez dans le terminal :**
- ✅ `[Audio Delta]` et `[Handler] Sending audio to client`
  → L'audio est envoyé

**Si vous voyez dans le navigateur :**
- ❌ `[Client] Message du serveur: audio` MAIS PAS `[Client] Audio reçu`
  → Le message n'a pas de champ audio
  
- ✅ `[Client] Audio reçu` MAIS PAS de son
  → Problème de décodage/lecture audio

## Prochaines étapes

Une fois que vous avez identifié où ça bloque, envoyez-moi :
1. Les logs du terminal (côté serveur)
2. Les logs de la console navigateur (côté client)
3. À quelle étape ça bloque

Je pourrai alors corriger le problème précis !
