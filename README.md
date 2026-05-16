# lconnect-auto-profiler

> Daemon Windows qui applique automatiquement un profil RGB/ventilation Lian Li L-Connect
> en fonction de l'application active au premier plan.

## Architecture

```
LConnect.AutoProfiler.Core/           — Modèles POCO + Interfaces (0 dépendance externe)
LConnect.AutoProfiler.Application/    — Orchestrateur + RuleEngine (logique métier pure)
LConnect.AutoProfiler.Infrastructure/ — Win32, JSON, HTTP (implémentations techniques)
LConnect.AutoProfiler.Host/           — Point d'entrée, DI, configuration
```

## Prérequis

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- L-Connect 3 installé et démarré

## Configuration

Éditer `LConnect.AutoProfiler.Host/appsettings.json` :

```json
{
  "ProfileRules": {
    "Mappings": {
      "cyberpunk2077.exe": "cyberpunk-pink-blue",
      "default":           "girl-boy"
    }
  },
  "ProfileParser": {
    "ProfilesDirectory": "./profiles"
  }
}
```

Placer les fichiers JSON de profil (décodés) dans le dossier `./profiles/` :
```
profiles/
├── cyberpunk-pink-blue.json
└── girl-boy.json
```

## Démarrage en développement

```powershell
cd LConnect.AutoProfiler.Host
dotnet run
```

## Installation comme service Windows

```powershell
dotnet publish -c Release -r win-x64 --self-contained
sc create "LConnectAutoProfiler" binPath="C:\path\to\publish\LConnect.AutoProfiler.Host.exe"
sc start "LConnectAutoProfiler"
```

## Ajouter un nouveau profil

1. Exporter et décoder le profil depuis L-Connect
2. Déposer le fichier `mon-profil.json` dans `./profiles/`
3. Ajouter la règle dans `appsettings.json` :
   ```json
   "MonJeu.exe": "mon-profil"
   ```
4. Redémarrer le service — aucune recompilation nécessaire
