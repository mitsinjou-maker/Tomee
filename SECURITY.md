# Sécurité et confidentialité

## Ce que Tomee fait de vos données

- **Aucune connexion réseau.** Tomee ne contient aucun code réseau : pas de télémétrie, pas de mise à jour automatique, pas d'envoi de données.
- **Clavier.** Le hook clavier global n'est installé que lorsque l'affichage des touches est activé (`Ctrl+Alt+K`), et retiré dès qu'il est désactivé ou mis en pause. Les touches sont seulement affichées à l'écran : elles ne sont jamais écrites sur le disque ni transmises.
  - Par défaut, seuls les **raccourcis** sont affichés (Ctrl, Alt, Win, Échap, F1…) : le texte tapé, mots de passe compris, n'apparaît pas.
  - Le mode « toutes les touches » affiche ce que vous tapez, **y compris dans les champs de mot de passe**, et donc dans vos partages d'écran. Ne l'activez que pour une démonstration de saisie.
- **Souris.** Le hook souris sert uniquement aux effets (clics, traînée, zoom à la molette) ; rien n'est enregistré.
- **Fichiers écrits** :
  - `%APPDATA%\Tomee\settings.ini` : vos réglages ;
  - `%APPDATA%\Tomee\erreurs.log` : messages d'erreur techniques, limité à 1 Mo, sans contenu saisi ;
  - `Images\Tomee\Annotation_*.png` : uniquement quand vous faites une capture en mode dessin (`Ctrl+S`).
- **Presse-papiers.** Les captures (`Ctrl+S` / `Ctrl+C` en mode dessin) y sont copiées, avec exclusion de la synchronisation vers le presse-papiers cloud de Windows.
- **Registre** (compte courant uniquement) : `HKCU\…\Run\Tomee` si le lancement au démarrage est activé, et l'entrée de désinstallation créée par l'installateur.

## Installation

- L'installateur s'exécute **sans droits administrateur** et n'écrit que dans le profil de l'utilisateur.
- Il n'est **pas signé numériquement** : vérifiez que l'empreinte SHA-256 du fichier téléchargé correspond à celle publiée dans les notes de la Release.

  ```powershell
  Get-FileHash .\Tomee-Setup.exe -Algorithm SHA256
  ```

- Pour un contrôle complet, compilez vous-même depuis les sources avec `build.cmd` (compilateur C# intégré à Windows, aucune dépendance externe).

## Signaler une vulnérabilité

Merci de ne pas publier les détails d'une faille dans une issue publique. Ouvrez une issue indiquant seulement qu'il s'agit d'un problème de sécurité ; un moyen d'échanger en privé vous sera proposé.
