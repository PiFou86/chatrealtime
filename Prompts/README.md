# Prompts Système

Ce dossier contient les fichiers de prompts système pour configurer la personnalité de votre assistant vocal.

## Utilisation

Pour changer le prompt système, modifiez le paramètre `SystemPromptFile` dans `appsettings.json` :

```json
{
  "OpenAI": {
    "SystemPromptFile": "Prompts/VotrePrompt.md"
  }
}
```

## Prompts disponibles

### Marvin.md
Personnalité de Marvin, le robot paranoïde android du Guide du voyageur galactique (H2G2).
- Intelligent mais déprimé
- Sarcastique et ironique
- Se plaint constamment
- Parfait pour une expérience humoristique et décalée

## Créer votre propre prompt

1. Créez un nouveau fichier `.md` dans ce dossier
2. Décrivez brièvement la personnalité, le style vocal et les conditions de délégation
3. Mettez à jour `appsettings.json` pour pointer vers votre nouveau fichier
4. Redémarrez l'application

Gardez les procédures métier détaillées et les instructions d'outils dans `DelegationInstructions`, pas dans le prompt Live.

## Exemple de structure

```markdown
# Nom du personnage

Description brève du personnage.

## Personnalité et voix

- Rôle, ton, langue et rythme
- Gestion des interruptions

## Politique de délégation

- Backend tools: capacités réellement disponibles
- Delegate to the backend when: conditions concrètes
- Do not delegate to the backend when: réponses que Live peut donner directement

Ne devine pas un résultat backend et attends sa confirmation avant d'annoncer la réussite d'une action.
```

## Configurations vocales conseillées

La voix est sélectionnée au démarrage d'une session avec `Voice`. Les choix ci-dessous sont des points de départ à écouter et à ajuster : une voix ne garantit pas à elle seule un accent régional. Les prompts imposent donc aussi la langue, la prononciation, le rythme et le jeu émotionnel.

| Personnage | `SystemPromptFile` | Voix conseillée | Alternative |
|---|---|---|---|
| Chucky | `Prompts/Chucky.md` | `ash` | `verse` |
| Deadpool | `Prompts/Deadpool.md` | `verse` | `echo` |
| GLaDOS | `Prompts/GLaDOS.md` | `shimmer` | `sage` |
| Marvin | `Prompts/Marvin.md` | `onyx` | `echo` |
| Onzième Docteur | `Prompts/OnziemeDocteurWho.md` | `fable` | `ballad` |
| Wednesday Addams | `Prompts/WednesdayAddams.md` | `sage` | `coral` |
| Yoda | `Prompts/Yoda.md` | `cedar` | `onyx` |
| Young Sheldon | `Prompts/YoungSheldon.md` | `alloy` | `ash` |

Exemple pour Yoda :

```json
"SystemPromptFile": "Prompts/Yoda.md",
"Voice": "cedar"
```

Redémarrez la session vocale après avoir changé le personnage ou la voix. Les voix intégrées utilisables peuvent évoluer selon le modèle et l'accès du projet; consultez la référence Live si une voix est refusée par l'API.
