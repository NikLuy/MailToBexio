# MailToBexio

Automatisiert die Erfassung neuer Kundenkontakte: Eingehende E-Mails in einem definierten Outlook-Ordner werden via KI ausgelesen und die extrahierten Kontaktdaten direkt in bexio angelegt — ohne manuellen Aufwand.

```
Outlook (Kunden_Erfassung)
        │
        ▼
  Microsoft Graph API
  (App-Only Auth)
        │
        ▼
  AI-Service (Copilot / Gemini / Ollama)
  → Extraktion: Name, Firma, E-Mail, Adresse
        │
        ▼
  bexio API
  → 3-stufige Dublettenprüfung
  → Firma + Kontaktperson anlegen
```

## Features

- **App-Only Auth** — läuft headless auf dem Server, kein eingeloggter Benutzer nötig
- **Austauschbarer KI-Provider** — Azure OpenAI (Copilot), Google Gemini oder lokales Ollama
- **Intelligente Dublettenprüfung** — sucht nach E-Mail, Firmenname und Personenname bevor ein Kontakt angelegt wird
- **Fehlerbehandlung** — nicht parsbare Mails landen in einem Fehler-Ordner, werden niemals gelöscht
- **Input Sanitization** — KI-Output wird vor der Übergabe an bexio bereinigt
- **Docker-ready** — Single Container, läuft auf Synology NAS via Container Manager

## Tech Stack

| Komponente | Technologie |
|---|---|
| Runtime | .NET 10 Worker Service |
| E-Mail | Microsoft Graph SDK 5.x |
| Auth | Azure.Identity (ClientSecretCredential) |
| KI (Standard) | Azure OpenAI 2.x (Copilot) |
| KI (alternativ) | Google Gemini REST, Ollama REST |
| Logging | Serilog (Console + Rolling File) |
| Tests | xUnit, NSubstitute, MockHttp |
| Deployment | Docker, docker-compose |

## Dokumentation

| Dokument | Inhalt |
|---|---|
| **[INSTALLATION.md](INSTALLATION.md)** | Azure App Registration, bexio API-Key, AI-Provider einrichten, Docker-Deployment auf Synology |
| **[DATENSCHUTZ.md](DATENSCHUTZ.md)** | Datenschutz-Vergleich der drei AI-Varianten, DSG/nDSG-Konformität, Handlungsempfehlungen |

## Projektstruktur

```
MailToBexio/
├── src/MailToBexio/
│   ├── Configuration/      ← stark typisierte Options-Klassen
│   ├── Models/             ← CustomerData, BexioContact
│   ├── Services/
│   │   ├── AI/             ← IAIService, CopilotService, GeminiService, OllamaService
│   │   ├── BexioService    ← 3-stufige Dublettenprüfung
│   │   └── GraphMailService← App-Only Auth, Ordner-Management
│   ├── Workers/            ← BackgroundService mit konfigurierbarem Intervall
│   └── Program.cs          ← DI, HttpClients, Serilog
├── tests/MailToBexio.Tests/
├── .env.example            ← Vorlage für Secrets (ins Git)
├── docker-compose.yml
└── Dockerfile
```

## Schnellstart (Entwicklung)

```bash
# 1. Repo klonen
git clone https://github.com/your-org/MailToBexio.git
cd MailToBexio

# 2. Secrets konfigurieren
cp .env.example .env
# .env mit echten Werten befüllen (siehe INSTALLATION.md)

# 3. Abhängigkeiten wiederherstellen & starten
dotnet restore
dotnet run --project src/MailToBexio

# Tests ausführen
dotnet test
```

## Updates auf dem Server

```bash
git pull
docker compose up -d --build
```

## Haftungsausschluss

Dieses Projekt wird **ohne jegliche Gewährleistung** bereitgestellt — weder ausdrücklich noch stillschweigend. Die Nutzung erfolgt auf **eigenes Risiko**.

Der Autor übernimmt keine Haftung für:
- Datenverlust oder fehlerhafte Kontaktanlage in bexio
- Fehlinterpretationen durch den eingesetzten KI-Provider (Halluzinationen)
- Datenschutzverstösse durch falsche Konfiguration des AI-Providers (siehe [DATENSCHUTZ.md](DATENSCHUTZ.md))
- Unterbrechungen oder Fehler durch Änderungen an den APIs von Microsoft Graph, bexio oder den KI-Anbietern
- Direkte, indirekte oder Folgeschäden jeglicher Art

Es liegt in der **Verantwortung des Betreibers**, die Konfiguration zu prüfen, den gewählten AI-Provider datenschutzkonform einzusetzen und den Betrieb regelmässig zu überwachen.

## Lizenz

MIT
