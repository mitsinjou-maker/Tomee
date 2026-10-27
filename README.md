# Tomee

Alternative à Mouzz pour Windows : met en valeur le curseur pour les **réunions, présentations, formations en ligne et tutoriels vidéo**. Petite application native (≈ 110 Ko), sans dépendance : elle tourne dans la zone de notification.

## Fonctionnalités

| Fonction | Raccourci | Détail |
|---|---|---|
| Projecteur | `Ctrl+Alt+S` | Assombrit l'écran sauf un cercle autour du curseur (rayon, opacité, douceur du bord réglables) |
| Halo | `Ctrl+Alt+H` | Anneau coloré autour du curseur, remplissage optionnel ; masqué quand le curseur l'est (diaporama, vidéo) |
| Effets de clic | `Ctrl+Alt+C` | Onde animée, couleur différente pour clic gauche / droit / milieu, indicateur de bouton maintenu, son de clic optionnel |
| Traînée | `Ctrl+Alt+T` | Traînée qui suit les mouvements du curseur |
| Touches | `Ctrl+Alt+K` | Affiche les raccourcis pressés (« Ctrl + C », « Alt + Tab ×3 », « Ctrl + Clic gauche »). Mode « raccourcis uniquement » par défaut : le texte tapé n'est jamais affiché |
| Dessin à l'écran | `Ctrl+Alt+D` | Stylo, surligneur, ligne, flèche, rectangle, ellipse, texte ; 8 couleurs ; annuler/rétablir ; capture annotée (Images\Tomee + presse-papiers) |
| Tableau blanc | `Ctrl+Alt+W` | Même outil sur fond blanc (`K` en mode dessin : tableau noir) |
| Zoom | `Ctrl+Alt+Z` | Loupe plein écran qui suit le curseur ; `Ctrl+Alt+molette` ou `Ctrl+Alt+↑/↓` pour le niveau |
| Localiser le curseur | `Ctrl+Alt+F` | Anneaux qui se resserrent sur le curseur |
| Pause | `Ctrl+Alt+X` | Suspend tous les effets |

**Tablette graphique** : en mode dessin, l'épaisseur du stylo suit la pression du stylet (Windows Ink), les traits sont lissés, et l'appui long (clic droit) de Windows est désactivé. Le halo reste visible même quand Windows masque le curseur pendant l'utilisation du stylet. Réglages dans *Zoom et dessin*.

En mode dessin : `P` `M` `L` `A` `R` `E` `T` (outils), `1`–`8` (couleurs), molette ou `+`/`−` (épaisseur), `Maj` (carré / cercle / 45°), `Ctrl+Z` / `Ctrl+Y`, `Suppr` (tout effacer), `Ctrl+S` (capture), `Tab` (barre d'outils), `Échap` (quitter).

Les effets sont visibles dans les partages d'**écran entier** (Teams, Zoom, Meet, OBS, enregistreurs). Multi-écrans et écrans haute densité (DPI par écran) pris en charge.

Réglages : double-clic sur l'icône de notification, ou relancer `Tomee.exe`. Ils sont enregistrés dans `%APPDATA%\Tomee\settings.ini`, et les erreurs éventuelles dans `erreurs.log` au même endroit.

## Confidentialité

Tomee ne se connecte jamais à Internet et n'enregistre aucune frappe. Détails, fichiers écrits et vérification de l'installateur : [SECURITY.md](SECURITY.md).

## Installer

Lancez `Tomee-Setup.exe` : installation pour l'utilisateur courant, **sans droits administrateur**, dans `%LOCALAPPDATA%\Programs\Tomee`. L'assistant crée le raccourci du menu Démarrer (et, au choix, celui du Bureau et le lancement au démarrage de Windows), puis ajoute Tomee à *Paramètres → Applications* pour la désinstallation. Relancer l'installateur met à jour une version existante en conservant les réglages.

Ligne de commande : `Tomee-Setup.exe /S [/desktop] [/autostart] [/launch]` (installation silencieuse, avec raccourci Bureau, lancement au démarrage, lancement immédiat), `Desinstaller.exe /uninstall [/S]` (désinstallation ; les réglages dans `%APPDATA%\Tomee` sont conservés).

## Compiler

Aucun SDK à installer : le compilateur C# de .NET Framework 4.8, fourni avec Windows 10/11, suffit.

```bash
build.cmd
```

Produit `bin\Tomee.exe` et l'installateur `bin\Tomee-Setup.exe`, qui embarque l'application (l'icône `assets\tomee.ico` est générée au premier build par `tools\make-icon.ps1`). Le nom et la version sont définis dans `src\AppInfo.cs`.

## Organisation du code (`src/`)

- `TrayApp.cs` : icône de notification, menu, raccourcis, boucle d'animation (~60 i/s, ne redessine que si quelque chose change)
- `EffectsEngine.cs` : projecteur, halo, clics, traînée, bulles ; une fenêtre transparente par écran, mise à jour uniquement sur les zones modifiées
- `LayeredWindow.cs` : fenêtre « layered » à alpha par pixel (`UpdateLayeredWindowIndirect`)
- `DrawBoard.cs`, `Shape.cs` : mode dessin / tableau blanc
- `ScreenZoom.cs` : loupe (API Magnification)
- `KeyDisplay.cs`, `InputHooks.cs` : affichage des touches, hooks souris/clavier bas niveau
- `SettingsForm.cs`, `Settings.cs` : réglages et leur persistance
